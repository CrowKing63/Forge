using System.Runtime.InteropServices;
using System.Text;

namespace Forge.Core;

/// <summary>
/// Owns one loaded llama.cpp model + context and the native buffers for decoding.
/// Low-level synchronous API consumed by <see cref="InferenceEngine"/>; one session
/// per engine instance, one inference at a time (the engine's gate enforces this).
///
/// Stateless between generations: each <see cref="PreparePrompt"/> clears the KV
/// cache first, so calls never accumulate context (matches the LLamaSharp
/// StatelessExecutor semantics the app was built on).
/// </summary>
internal sealed class LlamaSession : IDisposable
{
    private readonly IntPtr _model;
    private readonly IntPtr _ctx;
    private readonly IntPtr _vocab;

    // Reusable prompt buffers (sized for the largest prompt this session sees).
    private IntPtr _tokBuf;
    private int _tokBufCap;      // in bytes
    private IntPtr _posArr;
    private IntPtr _nSeqArr;
    private IntPtr _seqIdArr;
    private IntPtr _logitFlag;
    private int _arrCap;         // in tokens

    // Single-token generation buffers (allocated once, reused every step).
    private readonly IntPtr _oneTok;
    private readonly IntPtr _onePos;
    private readonly IntPtr _oneNSeq;
    private readonly IntPtr _oneSeqId;
    private readonly IntPtr _oneLogit;
    private readonly IntPtr _seq0;

    // Token → text decode buffer.
    private readonly IntPtr _pieceBuf;
    private const int PieceBufSize = 256;

    public LlamaSession(string modelPath, int contextSize)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
            throw new ArgumentException("Model path must not be empty.", nameof(modelPath));
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("Model file not found.", modelPath);
        if (contextSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(contextSize), contextSize, "Context size must be positive.");

        LlamaNative.EnsureBackendsLoaded();

        var mp = LlamaNative.llama_model_default_params();
        mp.n_gpu_layers = 0; // CPU-only
        _model = LlamaNative.llama_model_load_from_file(Utf8Ptr(modelPath), mp);
        if (_model == IntPtr.Zero)
            throw new IOException($"Failed to load model: {modelPath}");

        _vocab = LlamaNative.llama_model_get_vocab(_model);
        VocabSize = LlamaNative.llama_vocab_n_tokens(_vocab);
        EosTokenId = LlamaNative.llama_vocab_eos(_vocab);
        BosTokenId = LlamaNative.llama_vocab_bos(_vocab);

        var cp = LlamaNative.llama_context_default_params();
        cp.n_ctx = (uint)contextSize;
        cp.n_batch = 512;
        cp.n_threads = Environment.ProcessorCount;
        _ctx = LlamaNative.llama_init_from_model(_model, cp);
        if (_ctx == IntPtr.Zero)
        {
            LlamaNative.llama_model_free(_model);
            throw new IOException($"Failed to initialize context for: {modelPath}");
        }

        // Single-token generation buffers.
        _seq0 = Marshal.AllocHGlobal(4);
        Marshal.WriteInt32(_seq0, 0);
        _oneTok = Marshal.AllocHGlobal(4);
        _onePos = Marshal.AllocHGlobal(4);
        _oneNSeq = Marshal.AllocHGlobal(4);
        Marshal.WriteInt32(_oneNSeq, 1);
        _oneSeqId = Marshal.AllocHGlobal(8);
        Marshal.WriteInt64(_oneSeqId, (long)_seq0);
        _oneLogit = Marshal.AllocHGlobal(1);
        Marshal.WriteByte(_oneLogit, 0, 1);

        _pieceBuf = Marshal.AllocHGlobal(PieceBufSize);
    }

    /// <summary>Vocabulary size of the loaded model.</summary>
    public int VocabSize { get; }

    /// <summary>The model's end-of-sentence token id, or -1 when the vocab defines none.</summary>
    public int EosTokenId { get; }

    /// <summary>The model's beginning-of-sentence token id, or -1 when the vocab defines none.</summary>
    public int BosTokenId { get; }

    /// <summary>
    /// The chat template (Jinja source) embedded in the GGUF, or null when the file
    /// carries none. Consumers render it with the app's Jinja renderer.
    /// </summary>
    public string? ChatTemplate
    {
        get
        {
            IntPtr p = LlamaNative.llama_model_chat_template(_model, IntPtr.Zero);
            return p == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(p);
        }
    }

    /// <summary>
    /// Tokenizes <paramref name="text"/> (UTF-8) into the session's token buffer.
    /// Returns the token count; the tokens are readable from <see cref="Tokens"/>.
    /// Chat-formatted prompts must use <c>parseSpecial: true</> so role markers
    /// tokenize as single special tokens (S21 prototype finding).
    /// </summary>
    public int Tokenize(string text, bool addBos, bool parseSpecial)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length * 4 > _tokBufCap)
        {
            if (_tokBuf != IntPtr.Zero) Marshal.FreeHGlobal(_tokBuf);
            _tokBufCap = bytes.Length * 4;
            _tokBuf = Marshal.AllocHGlobal(_tokBufCap);
        }

        IntPtr textPtr = Utf8Ptr(text);
        try
        {
            int n = LlamaNative.llama_tokenize(_vocab, textPtr, bytes.Length, _tokBuf, _tokBufCap / 4, addBos, parseSpecial);
            if (n < 0)
                throw new IOException($"Tokenization failed for a {bytes.Length}-byte prompt.");
            _lastCount = n;
            return n;
        }
        finally
        {
            Marshal.FreeHGlobal(textPtr);
        }
    }

    /// <summary>Read-only view of the tokens produced by the last <see cref="Tokenize"/>.</summary>
    public int[] Tokens
    {
        get
        {
            // The token buffer is owned by the session; copy out for the caller.
            var tokens = new int[_lastCount];
            Marshal.Copy(_tokBuf, tokens, 0, tokens.Length);
            return tokens;
        }
    }

    private int _lastCount;

    /// <summary>
    /// Clears the KV cache and decodes the whole prompt in one batch, keeping logits
    /// only for the last token. After this call the next token's logits are at
    /// index <c>tokens.Length - 1</c>.
    /// </summary>
    public void DecodePrompt(int[] tokens)
    {
        LlamaNative.llama_memory_clear(LlamaNative.llama_get_memory(_ctx), false);

        EnsureArrs(tokens.Length);
        for (int i = 0; i < tokens.Length; i++)
        {
            Marshal.WriteInt32(_tokBuf, i * 4, tokens[i]);
            Marshal.WriteInt32(_posArr, i * 4, i);
            Marshal.WriteInt32(_nSeqArr, i * 4, 1);
            Marshal.WriteInt64(_seqIdArr, i * 8, (long)_seq0);
            Marshal.WriteByte(_logitFlag, i, (byte)(i == tokens.Length - 1 ? 1 : 0));
        }

        var batch = new LlamaNative.Batch
        {
            n_tokens = tokens.Length,
            token = _tokBuf,
            embd = IntPtr.Zero,
            pos = _posArr,
            n_seq_id = _nSeqArr,
            seq_id = _seqIdArr,
            logits = _logitFlag,
        };
        int r = LlamaNative.llama_decode(_ctx, batch);
        if (r != 0)
            throw new IOException($"Prompt decode failed (status {r}).");
    }

    /// <summary>
    /// Decodes one generated token at position <paramref name="pos"/> and keeps
    /// logits for it. After the call the new token's logits are at index 0.
    /// </summary>
    public void DecodeToken(int token, int pos)
    {
        Marshal.WriteInt32(_oneTok, 0, token);
        Marshal.WriteInt32(_onePos, 0, pos);
        var batch = new LlamaNative.Batch
        {
            n_tokens = 1,
            token = _oneTok,
            embd = IntPtr.Zero,
            pos = _onePos,
            n_seq_id = _oneNSeq,
            seq_id = _oneSeqId,
            logits = _oneLogit,
        };
        int r = LlamaNative.llama_decode(_ctx, batch);
        if (r != 0)
            throw new IOException($"Token decode failed at position {pos} (status {r}).");
    }

    /// <summary>
    /// Pointer to the raw logits array at output index <paramref name="index"/>.
    /// Valid until the next decode call; length is <see cref="VocabSize"/> floats.
    /// </summary>
    public IntPtr GetLogits(int index) => LlamaNative.llama_get_logits_ith(_ctx, index);

    /// <summary>Decodes one token to its text piece (UTF-8). Special tokens decode empty.</summary>
    public string TokenToText(int token)
    {
        int n = LlamaNative.llama_token_to_piece(_vocab, token, _pieceBuf, PieceBufSize - 1, 0, false);
        if (n <= 0) return string.Empty;
        var bytes = new byte[n];
        Marshal.Copy(_pieceBuf, bytes, 0, n);
        return Encoding.UTF8.GetString(bytes);
    }

    public void Dispose()
    {
        LlamaNative.llama_free(_ctx);
        LlamaNative.llama_model_free(_model);
        if (_tokBuf != IntPtr.Zero) Marshal.FreeHGlobal(_tokBuf);
        if (_posArr != IntPtr.Zero) Marshal.FreeHGlobal(_posArr);
        if (_nSeqArr != IntPtr.Zero) Marshal.FreeHGlobal(_nSeqArr);
        if (_seqIdArr != IntPtr.Zero) Marshal.FreeHGlobal(_seqIdArr);
        if (_logitFlag != IntPtr.Zero) Marshal.FreeHGlobal(_logitFlag);
        Marshal.FreeHGlobal(_oneTok);
        Marshal.FreeHGlobal(_onePos);
        Marshal.FreeHGlobal(_oneNSeq);
        Marshal.FreeHGlobal(_oneSeqId);
        Marshal.FreeHGlobal(_oneLogit);
        Marshal.FreeHGlobal(_seq0);
        Marshal.FreeHGlobal(_pieceBuf);
    }

    private void EnsureArrs(int tokens)
    {
        if (tokens > _arrCap)
        {
            if (_posArr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_posArr);
                Marshal.FreeHGlobal(_nSeqArr);
                Marshal.FreeHGlobal(_seqIdArr);
                Marshal.FreeHGlobal(_logitFlag);
            }
            _arrCap = Math.Max(tokens, 512);
            _posArr = Marshal.AllocHGlobal(4 * _arrCap);
            _nSeqArr = Marshal.AllocHGlobal(4 * _arrCap);
            _seqIdArr = Marshal.AllocHGlobal(8 * _arrCap);
            _logitFlag = Marshal.AllocHGlobal(_arrCap);
        }

        if (tokens * 4 > _tokBufCap)
        {
            if (_tokBuf != IntPtr.Zero) Marshal.FreeHGlobal(_tokBuf);
            _tokBufCap = tokens * 4;
            _tokBuf = Marshal.AllocHGlobal(_tokBufCap);
        }
    }

    private static IntPtr Utf8Ptr(string s)
    {
        byte[] b = Encoding.UTF8.GetBytes(s);
        IntPtr p = Marshal.AllocHGlobal(b.Length + 1);
        Marshal.Copy(b, 0, p, b.Length);
        Marshal.WriteByte(p, b.Length, 0);
        return p;
    }
}
