using LLama;
using LLama.Common;
using System.Text;

// S0 gate: CPU-only PoC. GpuLayerCount=0 because the agent is using the GPU.
string modelPath = @"C:\Users\uitaek\.cache\huggingface\hub\models--unsloth--gemma-4-E2B-it-qat-GGUF\snapshots\66a399f68ddd113b06dff02fca9523e55465d11d\gemma-4-E2B-it-qat-UD-Q4_K_XL.gguf";
string outPath = @"C:\Users\uitaek\AppData\Local\hermes\cache\scratch\s0-results.txt";

var parameters = new ModelParams(modelPath)
{
    ContextSize = 8192,
    GpuLayerCount = 0, // force CPU
};

Console.WriteLine("[S0] Loading model on CPU (GpuLayerCount=0)...");
var swLoad = System.Diagnostics.Stopwatch.StartNew();
var model = LLama.LLamaWeights.LoadFromFile(parameters);
swLoad.Stop();
double loadSec = swLoad.Elapsed.TotalSeconds;

string systemPrompt = "Fix grammar, spelling, and punctuation. Do not change meaning, tone, or language. Output only the corrected text.";
string[] samples = new[]
{
    "오늘 날씨가 너무 더워서 밖에 나가기 싫다.",
    "그 사람은 어제 밤에 집에 돌아왔다.",
    "저는 이 프로젝트가 매우 중요하다라고 생각한다.",
    "우리는 내일 회의실에서 모인후 논의를 시작할 것이다.",
    "컴퓨터가 갑자기 꺼져서 모든 작업이 사라졌다.",
};

var inferenceParams = new InferenceParams { MaxTokens = 300 };
// LLamaSharp 0.30.0 does not execute Gemma 4's embedded Jinja chat template
// (ApplyTemplate=true dumps the template source as output). So we hand-build
// the prompt with Gemma 4's role markers: <start_of_turn>user\n ... <end_of_turn>\n...
var executor = new StatelessExecutor(model, parameters, null!)
{
    SystemMessage = null,
    ApplyTemplate = false,
};

int totalTokens = 0;
var swDecode = System.Diagnostics.Stopwatch.StartNew();
var sb = new StringBuilder();
for (int i = 0; i < samples.Length; i++)
{
    var s = samples[i];
    // Gemma 4 chat format: system turn, user turn, then model generation prompt.
    string prompt = "<|turn>system\n" + systemPrompt + "\n<turn|>\n<|turn>user\n" + s + "\n<turn|>\n<|turn>model\n";
    Console.WriteLine($"\n[S0] IN[{i}]: {s}");
    var outSb = new StringBuilder();
    await foreach (var token in executor.InferAsync(prompt, inferenceParams, CancellationToken.None))
    {
        outSb.Append(token);
        totalTokens++;
    }
    sb.AppendLine($"--- Sample {i + 1} ---");
    sb.AppendLine("IN:  " + s);
    sb.AppendLine("OUT: " + outSb.ToString().Trim());
    sb.AppendLine();
}
swDecode.Stop();
double decSec = swDecode.Elapsed.TotalSeconds;
double tokPerSec = totalTokens / decSec;

var swUnload = System.Diagnostics.Stopwatch.StartNew();
model.Dispose();
swUnload.Stop();
double unloadSec = swUnload.Elapsed.TotalSeconds;

sb.AppendLine("=== TIMING ===");
sb.AppendLine($"load:   {loadSec:F1}s");
sb.AppendLine($"decode: {totalTokens} tokens in {decSec:F1}s = {tokPerSec:F1} tok/s");
sb.AppendLine($"unload: {unloadSec:F1}s");

File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
Console.WriteLine($"\n[S0] Decode: {totalTokens} tokens in {decSec:F1}s = {tokPerSec:F1} tok/s");
Console.WriteLine($"[S0] Unload time: {unloadSec:F1}s");
Console.WriteLine($"[S0] Results written to {outPath}");
