using Forge.Core;

// S4 console E2E (M1): selected text -> task -> PromptBuilder -> InferenceEngine (streaming) -> ResultParser.
// Usage: dotnet run -- [task] [--model <path>] [--gpu] [--detect]
//   task: proofread | translate | rewrite | summarize  (default: proofread)
//   --gpu: use the S8-detected GPU layer count instead of CPU
//   --detect: print hardware detection and exit
// Exit codes: 0 = success, 1 = pipeline failure, 2 = usage error.

const string defaultModel = @"C:\Users\uitaek\.cache\huggingface\hub\models--unsloth--gemma-4-E2B-it-qat-GGUF\snapshots\66a399f68ddd113b06dff02fca9523e55465d11d\gemma-4-E2B-it-qat-UD-Q4_K_XL.gguf";

// Suppress llama.cpp's native console log (S0/S1 harness noise) — M1 output must stay clean.
LLama.Native.NativeLogConfig.llama_log_set(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
Console.OutputEncoding = System.Text.Encoding.UTF8; // Korean output stays readable in any terminal / redirect

// ---- S13 eval mode: run the Korean eval set against the current prompts ----
if (args.Length > 0 && args[0] == "eval")
    return await RunEval(args.Skip(1).ToArray());

// ---- args ----
string? modelPath = null;
ForgeTask? parsedTask = null;
bool wantGpu = false;
bool detectOnly = false;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--model")
    {
        if (i + 1 >= args.Length)
        {
            Console.Error.WriteLine("Usage: forge [proofread|translate|rewrite|summarize] [--model <path>] [--gpu] [--detect]");
            return 2;
        }
        modelPath = args[++i];
        continue;
    }
    if (args[i] == "--gpu") { wantGpu = true; continue; }
    if (args[i] == "--detect") { detectOnly = true; continue; }

    parsedTask = args[i] switch
    {
        "proofread" => ForgeTask.Proofread,
        "translate" => ForgeTask.TranslateKrToEn,
        "rewrite" => ForgeTask.Rewrite,
        "summarize" => ForgeTask.Summarize,
        _ => null,
    };
    if (parsedTask is null)
    {
        Console.Error.WriteLine($"Unknown task '{args[i]}'. Usage: forge [proofread|translate|rewrite|summarize] [--model <path>] [--gpu] [--detect]");
        return 2;
    }
}
var task = parsedTask ?? ForgeTask.Proofread;

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

// ---- prompt assembly (S3) ----
var family = ModelProfile.Detect(path);
string prompt = PromptBuilder.Build(task, selected, family);

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
var result = ResultParser.Parse(OutputPostProcessor.Clean(sb.ToString(), family), selected, task);
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

// ---- S13 eval (M5): the Korean eval set against the current prompts ----
// Usage: dotnet run -- eval [--model <path>] [--gpu] [--out <file>]
// Mechanical gates per item: non-empty, no refusal/error markers, no language
// mismatch, summaries <= 3 sentences, translations contain no Hangul. The report
// pairs every output with its manual label for human review (plan §7).

static async Task<int> RunEval(string[] args)
{
    string? modelPath = null;
    bool wantGpu = false;
    string outPath = "eval-report.txt";
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--model")
        {
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine("Usage: forge eval [--model <path>] [--gpu] [--out <file>]");
                return 2;
            }
            modelPath = args[++i];
            continue;
        }
        if (args[i] == "--gpu") { wantGpu = true; continue; }
        if (args[i] == "--out")
        {
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine("Usage: forge eval [--model <path>] [--gpu] [--out <file>]");
                return 2;
            }
            outPath = args[++i];
            continue;
        }
        Console.Error.WriteLine($"Unknown eval argument '{args[i]}'. Usage: forge eval [--model <path>] [--gpu] [--out <file>]");
        return 2;
    }

    string setPath = Path.Combine(AppContext.BaseDirectory, "eval-set.json");
    if (!File.Exists(setPath))
    {
        Console.Error.WriteLine($"[eval] eval set not found: {setPath}");
        return 1;
    }
    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(setPath));
    var items = new List<(string Id, ForgeTask Task, string Input, string Expected)>();
    foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
    {
        var taskName = item.GetProperty("task").GetString();
        var task = taskName switch
        {
            "proofread" => ForgeTask.Proofread,
            "translate" => ForgeTask.TranslateKrToEn,
            "rewrite" => ForgeTask.Rewrite,
            "summarize" => ForgeTask.Summarize,
            _ => throw new InvalidOperationException($"Unknown task '{taskName}' in eval set."),
        };
        items.Add((item.GetProperty("id").GetString()!, task,
                  item.GetProperty("input").GetString()!, item.GetProperty("expected").GetString()!));
    }

    string path = modelPath ?? defaultModel;
    HardwareInfo hardware = HardwareDetector.Detect();
    long modelSize = File.Exists(path) ? new FileInfo(path).Length : 0;
    int gpuLayers = wantGpu ? HardwareDetector.RecommendGpuLayerCount(hardware, modelSize) : 0;
    Console.WriteLine($"[load] {path} (gpu layers: {gpuLayers})");
    var engine = new InferenceEngine(contextSize: 8192, gpuLayerCount: gpuLayers);
    try
    {
        engine.LoadModel(path);
    }
    catch (Exception ex) when (ex is FileNotFoundException or ArgumentException)
    {
        Console.Error.WriteLine($"[load] {ex.Message}");
        return 1;
    }

    var family = ModelProfile.Detect(path);
    var report = new System.Text.StringBuilder();
    int failures = 0;
    foreach (var (id, task, input, expected) in items)
    {
        string prompt = PromptBuilder.Build(task, input, family);
        int promptTokens = engine.CountTokens(prompt);
        int maxTokens = ModelProfile.MaxOutputTokens(family, Math.Max(16, engine.ContextSize - promptTokens - 256));

        var sb = new System.Text.StringBuilder();
        await foreach (var token in engine.Complete(prompt, maxTokens: maxTokens))
            sb.Append(token);

        var result = ResultParser.Parse(OutputPostProcessor.Clean(sb.ToString(), family), input, task);
        var problems = new List<string>();
        if (result.IsEmpty) problems.Add("empty output");
        if (result.HasError) problems.Add("refusal/error markers");
        if (result.LanguageMismatch) problems.Add("language mismatch");
        if (task == ForgeTask.Summarize && CountSentences(result.Text) > 3)
            problems.Add($"summary has {CountSentences(result.Text)} sentences (> 3)");
        if (task == ForgeTask.TranslateKrToEn && ContainsHangul(result.Text))
            problems.Add("translation contains Hangul");

        bool pass = problems.Count == 0;
        if (!pass) failures++;
        Console.WriteLine($"[{(pass ? "PASS" : "FAIL")}] {id}{(problems.Count > 0 ? " — " + string.Join("; ", problems) : "")}");

        report.AppendLine($"=== {id} ({task}) [{(pass ? "PASS" : "FAIL")}]\n");
        report.AppendLine("input:    " + input);
        report.AppendLine("expected: " + expected);
        report.AppendLine("actual:   " + result.Text);
        if (problems.Count > 0)
            report.AppendLine("flags:    " + string.Join("; ", problems));
        report.AppendLine();
    }

    engine.Unload();
    File.WriteAllText(outPath, report.ToString());
    Console.WriteLine($"[eval] {items.Count - failures}/{items.Count} passed; report written to {outPath}");
    return failures == 0 ? 0 : 1;
}

static int CountSentences(string text) =>
    text.Split('.', '?', '!').Where(s => !string.IsNullOrWhiteSpace(s)).Count();

static bool ContainsHangul(string text) =>
    text.Any(c => c >= 0xAC00 && c <= 0xD7A3);
