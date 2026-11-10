using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Forge.Core;

/// <summary>
/// Reusable, on-demand local-LLM inference service over the self-built llama.cpp
/// native library (see <see cref="LlamaNative"/> and docs/forge/S21-runtime-limitations.md).
///
/// CPU-only: all inference runs on the CPU. The engine owns the model's lifetime —
/// <see cref="LoadModel"/> loads on demand and <see cref="Unload"/> releases native
/// resources. One <see cref="Complete"/> call runs one inference, streaming decoded
/// tokens as an <see cref="IAsyncEnumerable{T}"/>.
///
/// Prompt assembly is the caller's responsibility (S3 PromptBuilder): this engine feeds
/// the fully-assembled prompt text to the model. Chat prompts are tokenized with
/// special-token parsing (done here) so role markers stay intact (S21 prototype finding).
///
/// Single-consumer: run one inference at a time. Concurrent <see cref="Complete"/> calls
/// on the same instance are not supported (the app enforces a re-entry guard, plan §4).
/// </summary>
public sealed class InferenceEngine : IAsyncDisposable
{
    private readonly object _gate = new();
    private LlamaSession? _session;

    /// <summary>Context window size in tokens (default 8192).</summary>
    public int ContextSize { get; }

    /// <summary>The model's embedded chat template (Jinja), or null if none.</summary>
    public string? ChatTemplate
    {
        get { lock (_gate) { return _session?.ChatTemplate; } }
    }

    /// <summary>The support tier of the loaded model (certified catalog vs unauthenticated).</summary>
    public ModelTier ModelTier { get; private set; } = ModelTier.Unauthenticated;

    /// <summary>The detected model family of the loaded model.</summary>
    public ModelFamily ModelFamily { get; private set; }

    /// <summary>Sampling temperature (default 0.8 — the llama.cpp server default).</summary>
    public float Temperature { get; set; } = 0.8f;

    /// <summary>Top-k sampling cutoff (default 40 — the llama.cpp server default).</summary>
    public int TopK { get; set; } = 40;

    /// <summary>True when a model is currently loaded.</summary>
    public bool IsLoaded
    {
        get { lock (_gate) { return _session is not null; } }
    }

    public InferenceEngine(int contextSize = 8192)
    {
        if (contextSize <= 0) throw new ArgumentOutOfRangeException(nameof(contextSize), contextSize, "Context size must be positive.");
        ContextSize = contextSize;
    }

    /// <summary>
    /// Loads the GGUF model at <paramref name="modelPath"/> (CPU). Any previously loaded
    /// model is unloaded first, so this is safe to call repeatedly (reload).
    /// </summary>
    public void LoadModel(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
            throw new ArgumentException("Model path must not be empty.", nameof(modelPath));
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("Model file not found.", modelPath);

        lock (_gate)
        {
            UnloadCore();

            // Determine tier and family from the catalog.
            var certified = ModelCatalog.CertifiedModel.Match(modelPath);
            ModelTier = certified is not null ? ModelTier.Certified : ModelTier.Unauthenticated;
            ModelFamily = ModelProfile.Detect(modelPath);

            _session = new LlamaSession(modelPath, ContextSize);
        }
    }

    /// <summary>
    /// Counts the exact number of tokens <paramref name="text"/> occupies in the loaded
    /// model's vocabulary (no BOS token). This is the precise counter that replaces
    /// <see cref="TextCapture.EstimateTokens"/> for the input guard (S4).
    /// </summary>
    public int CountTokens(string text)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));

        LlamaSession session;
        lock (_gate)
        {
            if (_session is null)
                throw new InvalidOperationException("Model is not loaded. Call LoadModel first.");
            session = _session;
        }

        return session.Tokenize(text, addBos: false, parseSpecial: true);
    }

    /// <summary>
    /// Runs one inference on the fully-assembled <paramref name="prompt"/> (role markers
    /// included — this engine does not template), streaming decoded text pieces.
    /// </summary>
    /// <param name="prompt">The complete prompt text to feed the model.</param>
    /// <param name="maxTokens">Maximum number of tokens to generate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async IAsyncEnumerable<string> Complete(
        string prompt,
        int maxTokens = 300,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt must not be empty.", nameof(prompt));
        if (maxTokens <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTokens), maxTokens, "Max tokens must be positive.");

        LlamaSession session;
        lock (_gate)
        {
            if (_session is null)
                throw new InvalidOperationException("Model is not loaded. Call LoadModel first.");
            session = _session;
        }

        // Bridge the synchronous native decode loop to async streaming. The generation
        // runs on a thread-pool thread; the gate above guarantees one inference at a
        // time, so the session's native state is never touched concurrently.
        var channel = Channel.CreateUnbounded<string>();
        var generation = Task.Run(
            () => GenerateCore(session, prompt, maxTokens, channel.Writer, cancellationToken),
            CancellationToken.None);

        await foreach (var piece in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return piece;
        }

        // The consumer finished normally — surface any generation failure that was not
        // already delivered through the channel.
        if (!generation.IsCompleted)
            await generation.WaitAsync(CancellationToken.None);
    }

    private void GenerateCore(
        LlamaSession session,
        string prompt,
        int maxTokens,
        ChannelWriter<string> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            // Chat prompts must parse special tokens so role markers tokenize intact.
            int nTok = session.Tokenize(prompt, addBos: false, parseSpecial: true);
            var tokens = session.Tokens;
            session.DecodePrompt(tokens);

            int pos = nTok;
            int next = SampleToken(session.GetLogits(nTok - 1), session.VocabSize);

            for (int i = 0; i < maxTokens; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string piece = session.TokenToText(next);
                if (piece.Length > 0)
                    writer.TryWrite(piece);

                if (next == session.EosTokenId)
                    break;

                session.DecodeToken(next, pos);
                pos++;
                next = SampleToken(session.GetLogits(0), session.VocabSize);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is reported to the consumer via ReadAllAsync.
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
            return;
        }
        writer.TryComplete();
    }

    /// <summary>
    /// Samples one token id from the logits: top-k cutoff, then temperature-scaled
    /// softmax over the kept candidates. With Temperature &le; 0 this degenerates to
    /// argmax (greedy).
    /// </summary>
    private int SampleToken(IntPtr logits, int vocabSize)
    {
        if (Temperature <= 0f)
            return ArgMax(logits, vocabSize);

        unsafe
        {
            float* p = (float*)logits;
            int cap = Math.Min(TopK, vocabSize);

            // Find the k-th largest logit value (the cutoff threshold) without
            // mutating the logits pointer.
            var topVals = new float[cap];
            var consumed = new bool[vocabSize];
            for (int k = 0; k < cap; k++)
            {
                float best = float.NegativeInfinity;
                int bestIdx = -1;
                for (int t = 0; t < vocabSize; t++)
                {
                    if (!consumed[t] && p[t] > best) { best = p[t]; bestIdx = t; }
                }
                topVals[k] = best;
                if (bestIdx >= 0) consumed[bestIdx] = true;
            }
            float threshold = topVals[cap - 1];

            // Keep every candidate at or above the threshold.
            var ids = new int[cap];
            int n = 0;
            for (int t = 0; t < vocabSize && n < cap; t++)
                if (p[t] >= threshold) ids[n++] = t;

            // Temperature-scaled softmax, then weighted pick.
            double sum = 0;
            var weights = new double[n];
            for (int i = 0; i < n; i++)
            {
                weights[i] = Math.Exp(p[ids[i]] / Temperature);
                sum += weights[i];
            }

            double r = Random.Shared.NextDouble() * sum;
            double acc = 0;
            for (int i = 0; i < n; i++)
            {
                acc += weights[i];
                if (acc >= r) return ids[i];
            }
            return ids[n - 1];
        }
    }

    private static unsafe int ArgMax(IntPtr logits, int n)
    {
        float* p = (float*)logits;
        int best = 0;
        float bestV = float.NegativeInfinity;
        for (int t = 0; t < n; t++)
            if (p[t] > bestV) { bestV = p[t]; best = t; }
        return best;
    }

    /// <summary>Releases native model resources. Safe to call when nothing is loaded.</summary>
    public void Unload()
    {
        lock (_gate)
        {
            UnloadCore();
        }
    }

    private void UnloadCore()
    {
        _session?.Dispose();
        _session = null;
        ModelTier = ModelTier.Unauthenticated;
        ModelFamily = ModelFamily.Gemma4;
    }

    public ValueTask DisposeAsync()
    {
        Unload();
        return ValueTask.CompletedTask;
    }
}
