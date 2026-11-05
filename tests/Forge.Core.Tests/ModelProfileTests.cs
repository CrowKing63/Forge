using Forge.Core;
using Xunit;

namespace Forge.Core.Tests;

public class ModelProfileTests
{
    // ---- Detect: file name → family ----

    [Fact]
    public void Detect_HyMt2FileName_ReturnsHyMt2()
    {
        Assert.Equal(ModelFamily.HyMt2,
            ModelProfile.Detect(@"C:\Users\uitaek\AppData\Local\Forge\Models\Hy-MT2-1.8B-Q8_0.gguf"));
    }

    [Fact]
    public void Detect_GemmaFileName_ReturnsGemma4()
    {
        Assert.Equal(ModelFamily.Gemma4,
            ModelProfile.Detect(@"C:\Users\uitaek\AppData\Local\Forge\Models\gemma-4-e2b-qat-ud-q4_k_xl.gguf"));
    }

    [Fact]
    public void Detect_IsCaseInsensitive()
    {
        Assert.Equal(ModelFamily.HyMt2, ModelProfile.Detect("hy-mt2-1.8b-q8_0.gguf"));
    }

    // ---- MaxOutputTokens: runaway guard (S15) ----

    [Fact]
    public void MaxOutputTokens_HyMt2_CapsAt512()
    {
        Assert.Equal(512, ModelProfile.MaxOutputTokens(ModelFamily.HyMt2, 7000));
        Assert.Equal(200, ModelProfile.MaxOutputTokens(ModelFamily.HyMt2, 200));
    }

    [Fact]
    public void MaxOutputTokens_Gemma4_PassesThrough()
    {
        Assert.Equal(7000, ModelProfile.MaxOutputTokens(ModelFamily.Gemma4, 7000));
    }
}
