using Forge.Core;
using Xunit;

namespace Forge.Core.Tests;

public class ModelCatalogTests
{
    [Fact]
    public void Match_GemmaFileName_ReturnsCertified()
    {
        var m = ModelCatalog.CertifiedModel.Match("C:/models/gemma-4-e2b-qat-ud-q4_k_xl.gguf");
        Assert.NotNull(m);
        Assert.Equal(ModelFamily.Gemma4, m.Family);
    }

    [Fact]
    public void Match_HyMt2FileName_ReturnsCertified()
    {
        var m = ModelCatalog.CertifiedModel.Match("C:/models/Hy-MT2-7B-Q8.gguf");
        Assert.NotNull(m);
        Assert.Equal(ModelFamily.HyMt2, m.Family);
    }

    [Fact]
    public void Match_UnknownFile_ReturnsNull()
    {
        Assert.Null(ModelCatalog.CertifiedModel.Match("C:/models/LFM3-1.7B-BF16.gguf"));
    }

    [Fact]
    public void IsCertified_Gemma_True()
    {
        Assert.True(ModelCatalog.CertifiedModel.IsCertified("gemma-4-e2b.gguf"));
    }

    [Fact]
    public void IsCertified_Unknown_False()
    {
        Assert.False(ModelCatalog.CertifiedModel.IsCertified("some-random-model.gguf"));
    }

    [Theory]
    [InlineData("gemma-4-e2b-qat-ud-q4_k_xl.gguf", ModelFamily.Gemma4)]
    [InlineData("Hy-MT2-7B-Q8_0.gguf", ModelFamily.HyMt2)]
    public void Match_Patterns_AreCaseInsensitive(string name, ModelFamily expected)
    {
        var m = ModelCatalog.CertifiedModel.Match(name);
        Assert.NotNull(m);
        Assert.Equal(expected, m.Family);
    }
}
