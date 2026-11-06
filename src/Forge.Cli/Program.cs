using Forge.Core;

// S16 console E2E (M1): clipboard text -> PromptBuilder -> InferenceEngine (streaming) -> ResultParser.
// Usage: dotnet run -- [--model <path>] [--gpu] [--detect]
// Exit codes: 0 = success, 1 = pipeline failure, 2 = usage error.

const string defaultModel = @"C:\Users\uitaek\.cache\huggingface\hub\models--unsloth--gemma-4-E2B-it-qat-GGUF\snapshots\66a399f68ddd113b06dff02fca9523e55465d11d\gemma-4-E2B-it-qat-UD-Q4_K_XL.gguf";

// Suppress llama.cpp's native console log (S0/S1 harness noise) — M1 output must stay clean.
LLama.Native.NativeLogConfig.llama_log_set(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
Console.OutputEncoding = System.Text.Encoding.UTF8; // Korean output stays readable in any terminal / redirect

// ---- args ----
string? modelPath = null;
bool wantGpu = false;
bool detectOnly = false;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--model")
    {
        if (i + 1 >= args.Length)
        {
            Console.Error.WriteLine("Usage: forge [--model <path>] [--gpu] [--detect]");
            return 2;
        }
        modelPath = args[++i];
        continue;
    }
    if (args[i] == "--gpu") { wantGpu = true; continue; }
    if (args[i] == "--detect") { detectOnly = true; continue; }

    Console.Error.WriteLine($"Unknown argument '{args[i]}'. Usage: forge [--model <path>] [--gpu] [--detect]");
    return 2;
}

// ---- S8 hardware detection ----
string path = modelPath ?? defaultModel;
HardwareInfo hardware = HardwareDetector.Detect();
long modelSize = 0;
if (File.Exists(path)) modelSize = new FileInfo(path).Length;
int gpuLayers = wantGpu ? HardwareDetector.RecommendGpuLayerCount(hardware, modelSize) : 0;

if (detectOnly)
{
    Console.WriteLine($"[hardware] {hardware.Note}");
    Console.WriteLine($"[hardware] CUDA major: {hardware.CudaMajorVersion}; Vulkan: {hardware.VulkanVersion ?? "none"}; VRAM: {(hardware.VramMb > 0 ? hardware.VramMb + " MB" : "unknown")}");
    Console.WriteLine($"[hardware] recommended GpuLayerCount: {gpuLayers}");
    return 0;
}
Console.WriteLine($"[load] {path} (gpu layers: {gpuLayers})");
var engine = new InferenceEngine(contextSize: 8192, gpuLayerCount: gpuLayers);
var swLoad = System.Diagnostics.Stopwatch.StartNew();
try
{
    engine.LoadModel(path);
}
catch (Exception ex) when (ex is FileNotFoundException or ArgumentException)
{
    Console.Error.WriteLine($"[load] {ex.Message}");
    return 1;
}
swLoad.Stop();
if (engine.FellBackToCpu)
    Console.WriteLine("[load] GPU unavailable — fell back to CPU");
Console.WriteLine($"[load] {swLoad.Elapsed.TotalSeconds:F1}s");

// ---- capture: clipboard text, guarded by the engine's precise tokenizer (S2 + S4) ----
const int safetyMargin = 4096;
var capture = new TextCapture(contextSize: engine.ContextSize, safetyMargin: safetyMargin, tokenCounter: engine.CountTokens);
string selected;
try
{
    selected = capture.Capture();
}
catch (TextCaptureException ex)
{
    Console.Error.WriteLine($"[capture] {ex.Message}");
    engine.Unload();
    return 1;
}
int inputTokens = engine.CountTokens(selected);
string preview = string.Join(' ', selected.Split('\r', '\n')).Trim();
if (preview.Length > 100) preview = preview[..100] + "…";
Console.WriteLine($"[capture] {selected.Length} chars / {inputTokens} tokens: \"{preview}\"");

// ---- prompt assembly (S3, S16: single translate prompt) ----
var family = ModelProfile.Detect(path);
string prompt = PromptBuilder.Build(selected, family);

// ---- output budget: exact context size minus the exact prompt size, with a margin ----
int promptTokens = engine.CountTokens(prompt);
int maxTokens = ModelProfile.MaxOutputTokens(family, Math.Max(16, engine.ContextSize - promptTokens - 256));

// ---- streaming inference (S1) ----
Console.Write("[out] ");
var swInfer = System.Diagnostics.Stopwatch.StartNew();
var swFirst = System.Diagnostics.Stopwatch.StartNew();
int tokenCount = 0;
string? firstToken = null;
var sb = new System.Text.StringBuilder();
await foreach (var token in engine.Complete(prompt, maxTokens: maxTokens))
{
    if (firstToken is null) { firstToken = token; swFirst.Stop(); }
    sb.Append(token);
    Console.Write(token);
    tokenCount++;
}
swInfer.Stop();
Console.WriteLine();

// ---- result parsing and display (S3) ----
var result = ResultParser.Parse(OutputPostProcessor.Clean(sb.ToString(), family), selected);
if (result.IsEmpty)
{
    Console.Error.WriteLine("[error] The model returned no usable output.");
    engine.Unload();
    return 1;
}
if (result.HasError)
    Console.Error.WriteLine("[warn] Output looks like a refusal or error message — review it before use.");
if (result.LanguageMismatch)
    Console.Error.WriteLine("[warn] Output language does not match what this task expects — review it before use.");

double seconds = Math.Max(swInfer.Elapsed.TotalSeconds, 0.001);
Console.WriteLine($"[done] {tokenCount} tokens in {swInfer.Elapsed.TotalSeconds:F1}s ({tokenCount / seconds:F1} tok/s); first token after {swFirst.Elapsed.TotalSeconds:F2}s");
engine.Unload();
return 0;
