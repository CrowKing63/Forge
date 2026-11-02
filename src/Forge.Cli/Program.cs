using Forge.Core;

// S1 console harness: drive InferenceEngine end-to-end and measure first-token latency.
// Usage: dotnet run -- <modelPath> [prompt text...]
// Defaults match the S0 model and a Korean proofreading prompt.

const string defaultModel = @"C:\Users\uitaek\.cache\huggingface\hub\models--unsloth--gemma-4-E2B-it-qat-GGUF\snapshots\66a399f68ddd113b06dff02fca9523e55465d11d\gemma-4-E2B-it-qat-UD-Q4_K_XL.gguf";
const string systemPrompt = "Fix grammar, spelling, and punctuation. Do not change meaning, tone, or language. Output only the corrected text.";

string modelPath = args.Length > 0 ? args[0] : defaultModel;
string userText = args.Length > 1 ? string.Join(' ', args.Skip(1)) : "오늘 날씨가 너무 더워서 밖에 나가기 싫다.";

// Gemma 4 role markers, hand-assembled (the engine does not template). Mirrors S0.
string prompt = "<|turn>system\n" + systemPrompt + "\n<turn|>\n<|turn>user\n" + userText + "\n<turn|>\n<|turn>model\n";

var engine = new InferenceEngine(contextSize: 8192, gpuLayerCount: 0);

Console.WriteLine($"[S1] Loading model on CPU (GpuLayerCount=0): {modelPath}");
var swLoad = System.Diagnostics.Stopwatch.StartNew();
engine.LoadModel(modelPath);
swLoad.Stop();
Console.WriteLine($"[S1] Load: {swLoad.Elapsed.TotalSeconds:F1}s");

Console.WriteLine($"\n[S1] IN: {userText}");
Console.Write("[S1] OUT: ");
var swFirst = System.Diagnostics.Stopwatch.StartNew();
int tokenCount = 0;
string? firstToken = null;
await foreach (var token in engine.Complete(prompt, maxTokens: 300))
{
    if (firstToken is null) { firstToken = token; swFirst.Stop(); }
    Console.Write(token);
    tokenCount++;
}
Console.WriteLine();

engine.Unload();
Console.WriteLine($"\n[S1] Done. {tokenCount} tokens generated; first token after {swFirst.Elapsed.TotalSeconds:F2}s. Unloaded.");
