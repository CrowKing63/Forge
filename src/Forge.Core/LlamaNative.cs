using System.Runtime.InteropServices;

namespace Forge.Core;

/// <summary>
/// P/Invoke bindings over the self-built llama.cpp native library (llama.dll + ggml).
/// Replaces LLamaSharp 0.30.0 — see docs/forge/S21-runtime-limitations.md for why:
/// the bundled NuGet snapshot cannot load new architectures (LFM3) and cannot apply
/// Gemma 4's embedded Jinja chat template.
///
/// The native DLLs are bundled next to the executable (portable zip). Backends
/// (ggml-cpu, ggml-cuda, ...) are loaded once per process via ggml_backend_load_all
/// before any model load. All signatures and struct layouts below were verified
/// against the llama.h header of the pinned build (e71c8f673) by the FFI prototype.
/// </summary>
internal static class LlamaNative
{
    internal const string LlamaDllName = "llama.dll";
    internal const string GgmlDllName = "ggml.dll";

    private static bool _backendsLoaded;
    private static readonly object InitGate = new();

    /// <summary>Directory containing llama.dll + the ggml backend DLLs.</summary>
    public static string RuntimeDir { get; } = ResolveRuntimeDir();

    static LlamaNative()
    {
        // Add our runtime dir to the native search path so DllImport resolves the
        // bundled DLLs regardless of how the app was launched.
        AddDllDirectory(RuntimeDir);
        PreloadCoreDlls();
    }

    /// <summary>
    /// Loads ggml-base.dll, ggml.dll and llama.dll by full path up front.
    /// Windows loader quirk: a DLL found via search-path lookup does not get its
    /// discovery directory added to the dependency-search context, so a bare-name
    /// load of ggml.dll fails with ERROR_MOD_NOT_FOUND (126) even when
    /// ggml-base.dll sits right next to it. Once the modules are loaded by full
    /// path, every later bare-name DllImport resolves against the loaded module.
    /// </summary>
    private static void PreloadCoreDlls()
    {
        LoadByFullPath("ggml-base.dll");
        LoadByFullPath("ggml.dll");
        LoadByFullPath(LlamaDllName);
    }

    private static IntPtr LoadByFullPath(string name)
    {
        string path = Path.Combine(RuntimeDir, name);
        IntPtr h = LoadLibraryEx(path, IntPtr.Zero, 0);
        if (h == IntPtr.Zero)
            throw new DllNotFoundException(
                $"Failed to load {name} from {RuntimeDir} (Win32 error {Marshal.GetLastWin32Error()}).");
        return h;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

    /// <summary>
    /// Loads all ggml backends (CPU always; CUDA/Vulkan when present in the build).
    /// Must run before the first model load; safe to call repeatedly.
    ///
    /// llama.dll statically links its own copy of ggml, so backends must be loaded
    /// through llama.dll's registry — calling ggml_backend_load_all_from_path via
    /// ggml.dll loads them into a separate instance that llama_model_load never sees.
    /// llama_backend_init() triggers the internal ggml_backend_load_all(), which
    /// searches the executable directory and CWD; we point CWD at RuntimeDir for
    /// the duration of the call so the bundled backend DLLs are found no matter
    /// where the app runs from.
    /// </summary>
    public static void EnsureBackendsLoaded()
    {
        if (_backendsLoaded) return;
        lock (InitGate)
        {
            if (_backendsLoaded) return;
            string origCwd = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(RuntimeDir);
                llama_backend_init();
            }
            finally
            {
                Directory.SetCurrentDirectory(origCwd);
            }
            _backendsLoaded = true;
        }
    }

    static string ResolveRuntimeDir()
    {
        // 1. Bundled next to the executable (portable zip / installed app).
        var baseDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(baseDir, LlamaDllName)))
            return baseDir;

        // 2. Dev fallback: the local llama.cpp build this project pins to.
        const string dev = @"C:\Users\uitaek\.unsloth\llama.cpp\build\bin\Release";
        if (File.Exists(Path.Combine(dev, LlamaDllName)))
            return dev;

        throw new FileNotFoundException(
            $"{LlamaDllName} not found next to the executable or in the dev build path. " +
            "The llama.cpp native runtime must be bundled with the app.");
    }

    // ---- struct layouts (exact match with include/llama.h @ e71c8f673) ----

    [StructLayout(LayoutKind.Sequential)]
    internal struct ModelParams
    {
        public IntPtr devices;
        public IntPtr tensor_buft_overrides;
        public int n_gpu_layers;
        public int split_mode;
        public int load_mode;
        public int lazy_mode;
        public int main_gpu;
        public IntPtr tensor_split;
        public IntPtr progress_callback;
        public IntPtr progress_callback_user_data;
        public IntPtr kv_overrides;
        [MarshalAs(UnmanagedType.I1)] public bool vocab_only;
        [MarshalAs(UnmanagedType.I1)] public bool check_tensors;
        [MarshalAs(UnmanagedType.I1)] public bool use_extra_bufts;
        [MarshalAs(UnmanagedType.I1)] public bool no_host;
        [MarshalAs(UnmanagedType.I1)] public bool no_alloc;
        [MarshalAs(UnmanagedType.I1)] public bool load_mtp;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CtxParams
    {
        public uint n_ctx;
        public uint n_batch;
        public uint n_ubatch;
        public uint n_seq_max;
        public uint n_rs_seq;
        public uint n_outputs_max;
        public uint n_outputs_max_per_seq;
        public int n_threads;
        public int n_threads_batch;
        public int ctx_type;
        public int rope_scaling_type;
        public int pooling_type;
        public int attention_type;
        public int flash_attn_type;
        public float rope_freq_base;
        public float rope_freq_scale;
        public float yarn_ext_factor;
        public float yarn_attn_factor;
        public float yarn_beta_fast;
        public float yarn_beta_slow;
        public uint yarn_orig_ctx;
        public float defrag_thold;
        public IntPtr cb_eval;
        public IntPtr cb_eval_user_data;
        public int type_k;
        public int type_v;
        public ulong moe_cache_size;
        public IntPtr abort_callback;
        public IntPtr abort_callback_data;
        [MarshalAs(UnmanagedType.I1)] public bool embeddings;
        [MarshalAs(UnmanagedType.I1)] public bool offload_kqv;
        [MarshalAs(UnmanagedType.I1)] public bool no_perf;
        [MarshalAs(UnmanagedType.I1)] public bool op_offload;
        [MarshalAs(UnmanagedType.I1)] public bool swa_full;
        [MarshalAs(UnmanagedType.I1)] public bool kv_unified;
        public IntPtr samplers;
        public ulong n_samplers;
        public IntPtr ctx_other;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Batch
    {
        public int n_tokens;
        public IntPtr token;
        public IntPtr embd;
        public IntPtr pos;
        public IntPtr n_seq_id;
        public IntPtr seq_id;
        public IntPtr logits;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ChatMsg
    {
        public IntPtr role;
        public IntPtr content;
    }

    // ---- P/Invoke (all verified by the FFI prototype) ----

    [DllImport(LlamaDllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ModelParams llama_model_default_params();

    [DllImport(LlamaDllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void llama_backend_init();

    [DllImport(LlamaDllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr llama_model_chat_template(IntPtr model, IntPtr name);

    [DllImport(LlamaDllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int llama_chat_apply_template(
        IntPtr tmpl, IntPtr chat, int nMsg, bool addAss, IntPtr buf, int length);

    [DllImport(LlamaDllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern CtxParams llama_context_default_params();

    [DllImport(LlamaDllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr llama_model_load_from_file(IntPtr path, ModelParams mp);

    [DllImport(LlamaDllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr llama_model_get_vocab(IntPtr model);

    [DllImport(LlamaDllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr llama_init_from_model(IntPtr model, CtxParams cp);

    [DllImport(LlamaDllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void llama_free(IntPtr ctx);

    [DllImport(LlamaDllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void llama_model_free(IntPtr model);

    [DllImport(LlamaDllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr llama_get_memory(IntPtr ctx);

    [DllImport(LlamaDllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void llama_memory_clear(IntPtr mem, [MarshalAs(UnmanagedType.I1)] bool data);

    [DllImport(LlamaDllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int llama_decode(IntPtr ctx, Batch batch);

    [DllImport(LlamaDllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int llama_tokenize(
        IntPtr vocab, IntPtr text, int textLen, IntPtr tokens, int nMax,
        [MarshalAs(UnmanagedType.I1)] bool addSpecial,
        [MarshalAs(UnmanagedType.I1)] bool parseSpecial);

    [DllImport(LlamaDllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int llama_token_to_piece(
        IntPtr vocab, int token, IntPtr buf, int length, int lstrip,
        [MarshalAs(UnmanagedType.I1)] bool special);

    [DllImport(LlamaDllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr llama_get_logits_ith(IntPtr ctx, int i);

    [DllImport(LlamaDllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int llama_vocab_n_tokens(IntPtr vocab);

    [DllImport(LlamaDllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int llama_vocab_eos(IntPtr vocab);

    [DllImport(LlamaDllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int llama_vocab_bos(IntPtr vocab);

    // ---- hardware probes (used by HardwareDetector) ----

    [DllImport(LlamaDllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern bool LlamaSupportsGpuOffload();

    [DllImport(GgmlDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ggml_backend_dev_count")]
    internal static extern ulong GgmlBackendDevCount();

    [DllImport(GgmlDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ggml_backend_dev_get")]
    internal static extern IntPtr GgmlBackendDevGet(ulong index);

    [DllImport(GgmlDllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ggml_backend_dev_name")]
    internal static extern IntPtr GgmlBackendDevName(IntPtr device);

    // ---- native log control ----

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GgmlLogCallback(int level, IntPtr text, IntPtr userData);

    // Kept alive for the process lifetime: native code holds this pointer after
    // ggml_log_set; a GC'd delegate would crash on the next log event.
    private static readonly GgmlLogCallback NoopLog = (_, _, _) => { };

    [DllImport(GgmlDllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ggml_log_set(GgmlLogCallback cb, IntPtr userData);

    /// <summary>
    /// Installs a no-op native log callback so llama.cpp's stderr chatter does not reach
    /// the console (CLI harness and tray app both want clean output). Call once at startup.
    /// </summary>
    public static void SuppressConsoleLog()
    {
        ggml_log_set(NoopLog, IntPtr.Zero);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint AddDllDirectory(string dir);
}
