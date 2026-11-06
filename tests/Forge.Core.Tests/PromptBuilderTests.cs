using Forge.Core;
using Xunit;

namespace Forge.Core.Tests;

public class PromptBuilderTests
{
    [Fact]
    public void Build_Gemma4_AssemblesRoleMarkers()
    {
        var text = "안녕하세요";
        var prompt = PromptBuilder.Build(text, ModelFamily.Gemma4);
        Assert.Contains("<|turn>system", prompt);
        Assert.Contains("<|turn>user", prompt);
        Assert.Contains("<|turn>model", prompt);
        // The user turn contains the selected text verbatim.
        Assert.Contains(text, prompt);
    }

    [Fact]
    public void Build_HyMt2_UsesReadmeInstructionAndMarkers()
    {
        var text = "안녕하세요";
        var prompt = PromptBuilder.Build(text, ModelFamily.HyMt2);
        Assert.StartsWith("<|hy_begin_of_sentence|><|hy_User|>", prompt);
        Assert.EndsWith("<|hy_Assistant|>", prompt);
        // The README instruction is kept verbatim — MT models are sensitive to wording.
        Assert.Contains("Translate the following segment into English", prompt);
        Assert.Contains(text, prompt);
    }

    [Fact]
    public void Build_IsDeterministic()
    {
        var a = PromptBuilder.Build("테스트", ModelFamily.Gemma4);
        var b = PromptBuilder.Build("테스트", ModelFamily.Gemma4);
        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public void Build_EmptyText_Throws(string text) =>
        Assert.Throws<ArgumentException>(() => PromptBuilder.Build(text, ModelFamily.Gemma4));
}
