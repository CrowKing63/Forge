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
    public void Constructor_DefaultsAreCpuFirst()
    {
        var engine = new InferenceEngine();
        Assert.Equal(8192, engine.ContextSize);
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

    [Fact]
    public void CountTokens_WhenNotLoaded_Throws()
    {
        var engine = new InferenceEngine();
        Assert.Throws<InvalidOperationException>(() => engine.CountTokens("hello"));
    }

    [Fact]
    public void CountTokens_NullText_Throws()
    {
        var engine = new InferenceEngine();
        Assert.Throws<ArgumentNullException>(() => engine.CountTokens(null!));
    }

    // ---- helpers ----

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
