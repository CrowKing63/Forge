using Forge.Core;
using Xunit;

namespace Forge.Core.Tests;

public class InferenceEngineTests
{
    // ---- Fast unit tests (no model required) ----

    [Fact]
    public void Constructor_RejectsNonPositiveContextSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InferenceEngine(contextSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InferenceEngine(contextSize: -1));
    }

    [Fact]
    public void Constructor_RejectsNegativeGpuLayerCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InferenceEngine(gpuLayerCount: -1));
    }

    [Fact]
    public void Constructor_DefaultsAreCpuFirst()
    {
        var engine = new InferenceEngine();
        Assert.Equal(8192, engine.ContextSize);
        Assert.Equal(0, engine.GpuLayerCount);
        Assert.False(engine.IsLoaded);
    }

    [Fact]
    public async Task Complete_WhenNotLoaded_Throws()
    {
        var engine = new InferenceEngine();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ConsumeFirst(engine.Complete("hello")));
    }

    [Fact]
    public async Task Complete_EmptyPrompt_Throws()
    {
        // Empty/whitespace prompt is rejected before any model access.
        var engine = new InferenceEngine();
        await Assert.ThrowsAsync<ArgumentException>(() => ConsumeFirst(engine.Complete("   ")));
    }

    [Fact]
    public void LoadModel_MissingFile_Throws()
    {
        var engine = new InferenceEngine();
        Assert.Throws<FileNotFoundException>(() => engine.LoadModel(@"C:\does\not\exist.gguf"));
    }

    // ---- Integration tests (require the real GGUF model; skipped if absent) ----

    [Fact]
    public async Task Load_Complete_Stream_Unload_Reload()
    {
        var modelPath = ResolveModelPath();
        if (modelPath is null) throw Xunit.Sdk.SkipException.ForSkip("Model file not found; skipping integration test.");
        string path = modelPath;

        var engine = new InferenceEngine();
        try
        {
            engine.LoadModel(path);
            Assert.True(engine.IsLoaded);

            // Two independent inferences must each produce non-empty output. The second
            // must not leak state from the first (StatelessExecutor semantics).
            string first = await Concatenate(engine.Complete(BuildPrompt("안녕하세요."), maxTokens: 16));
            string second = await Concatenate(engine.Complete(BuildPrompt("오늘 날씨가 좋다."), maxTokens: 16));

            Assert.False(string.IsNullOrWhiteSpace(first), "First inference produced no output.");
            Assert.False(string.IsNullOrWhiteSpace(second), "Second inference produced no output.");

            engine.Unload();
            Assert.False(engine.IsLoaded);

            // Reload after unload must work.
            engine.LoadModel(path);
            Assert.True(engine.IsLoaded);
            string third = await Concatenate(engine.Complete(BuildPrompt("감사합니다."), maxTokens: 16));
            Assert.False(string.IsNullOrWhiteSpace(third), "Reloaded inference produced no output.");
        }
        finally
        {
            engine.Unload();
        }
    }

    [Fact]
    public async Task Complete_FirstTokenLatency_IsReasonable()
    {
        var modelPath = ResolveModelPath();
        if (modelPath is null) throw Xunit.Sdk.SkipException.ForSkip("Model file not found; skipping integration test.");
        string path = modelPath;

        // S0 measured ~2.5s load and ~10-11 tok/s decode on this machine. Guard against
        // gross regression: the first token should arrive well under 60s.
        var engine = new InferenceEngine();
        try
        {
            engine.LoadModel(path);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            string? firstToken = null;
            await foreach (var token in engine.Complete(BuildPrompt("안녕하세요 세계."), maxTokens: 8))
            {
                firstToken = token;
                break;
            }
            sw.Stop();

            Assert.NotNull(firstToken);
            Assert.True(sw.Elapsed.TotalSeconds < 60, $"First token took {sw.Elapsed.TotalSeconds:F1}s (expected well under 60s).");
        }
        finally
        {
            engine.Unload();
        }
    }

    // ---- helpers ----

    private static string BuildPrompt(string userText)
    {
        // Gemma 4 role markers, hand-assembled (the engine does not template). Mirrors S0.
        const string system = "Fix grammar, spelling, and punctuation. Do not change meaning, tone, or language. Output only the corrected text.";
        return "<|turn>system\n" + system + "\n<turn|>\n<|turn>user\n" + userText + "\n<turn|>\n<|turn>model\n";
    }

    private static string? ResolveModelPath()
    {
        var env = Environment.GetEnvironmentVariable("FORGE_MODEL_PATH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
        const string defaultPath = @"C:\Users\uitaek\.cache\huggingface\hub\models--unsloth--gemma-4-E2B-it-qat-GGUF\snapshots\66a399f68ddd113b06dff02fca9523e55465d11d\gemma-4-E2B-it-qat-UD-Q4_K_XL.gguf";
        return File.Exists(defaultPath) ? defaultPath : null;
    }

    private static async Task<string> Concatenate(IAsyncEnumerable<string> tokens, int maxTokens = 300)
    {
        var sb = new System.Text.StringBuilder();
        await foreach (var t in tokens)
        {
            sb.Append(t);
        }
        return sb.ToString();
    }

    private static async Task<string?> ConsumeFirst(IAsyncEnumerable<string> tokens)
    {
        string? first = null;
        await foreach (var t in tokens)
        {
            first = t;
            break;
        }
        return first;
    }
}
