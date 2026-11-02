using System.Runtime.CompilerServices;
using LLama;
using LLama.Common;

namespace Forge.Core;

/// <summary>
/// Reusable, on-demand local-LLM inference service wrapping LLamaSharp.
///
/// CPU-first: <see cref="GpuLayerCount"/> defaults to 0 (no GPU offload). The engine
/// owns the model's lifetime — <see cref="LoadModel"/> loads on demand and
/// <see cref="Unload"/> releases native resources. One <see cref="Complete"/> call
/// runs one inference, streaming decoded tokens as an <see cref="IAsyncEnumerable{T}"/>.
///
/// This engine does NOT apply chat templates. LLamaSharp 0.30.0 cannot execute Gemma 4's
/// embedded Jinja template (it dumps the template source and crashes), so prompt assembly
/// with Gemma 4 role markers is the caller's responsibility (see S3 PromptBuilder).
/// <c>ApplyTemplate</c> is pinned to <c>false</c>.
///
/// Single-consumer: run one inference at a time. Concurrent <see cref="Complete"/> calls
/// on the same instance are not supported (the app enforces a re-entry guard, plan §4).
/// </summary>
public sealed class InferenceEngine : IAsyncDisposable
{
    private readonly object _gate = new();
    private LLama.LLamaWeights? _weights;
    private StatelessExecutor? _executor;

    /// <summary>Context window size in tokens (default 8192).</summary>
    public int ContextSize { get; }

    /// <summary>Number of layers offloaded to GPU. 0 = CPU-only (default).</summary>
    public int GpuLayerCount { get; }

    /// <summary>True when a model is currently loaded.</summary>
    public bool IsLoaded
    {
        get { lock (_gate) { return _weights is not null; } }
    }

    public InferenceEngine(int contextSize = 8192, int gpuLayerCount = 0)
    {
        if (contextSize <= 0) throw new ArgumentOutOfRangeException(nameof(contextSize), contextSize, "Context size must be positive.");
        if (gpuLayerCount < 0) throw new ArgumentOutOfRangeException(nameof(gpuLayerCount), gpuLayerCount, "GPU layer count cannot be negative.");
        ContextSize = contextSize;
        GpuLayerCount = gpuLayerCount;
    }

    /// <summary>
    /// Loads the GGUF model at <paramref name="modelPath"/>. Any previously loaded model
    /// is unloaded first, so this is safe to call repeatedly (reload).
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

            var parameters = new ModelParams(modelPath)
            {
                ContextSize = (uint)ContextSize,
                GpuLayerCount = GpuLayerCount,
            };

            _weights = LLama.LLamaWeights.LoadFromFile(parameters);
            _executor = new StatelessExecutor(_weights, parameters, null!)
            {
                SystemMessage = null,
                ApplyTemplate = false,
            };
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

        LLama.LLamaWeights weights;
        lock (_gate)
        {
            if (_weights is null)
                throw new InvalidOperationException("Model is not loaded. Call LoadModel first.");
            weights = _weights;
        }

        // Positional args: (text, addBos, special, encoding) per LLamaSharp 0.30.0.
        return weights.Tokenize(text, false, false, System.Text.Encoding.UTF8).Length;
    }

    /// <summary>
    /// Runs one inference on the fully-assembled <paramref name="prompt"/> (role markers
    /// included — this engine does not template), streaming decoded tokens.
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

        StatelessExecutor executor;
        lock (_gate)
        {
            if (_executor is null)
                throw new InvalidOperationException("Model is not loaded. Call LoadModel first.");
            executor = _executor;
        }

        var inferenceParams = new InferenceParams { MaxTokens = maxTokens };
        await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken))
        {
            yield return token;
        }
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
        // StatelessExecutor is not IDisposable in LLamaSharp 0.30.0; disposing the
        // weights releases the native model + context (matches S0's unload path).
        _executor = null;
        _weights?.Dispose();
        _weights = null;
    }

    public ValueTask DisposeAsync()
    {
        Unload();
        return ValueTask.CompletedTask;
    }
}
