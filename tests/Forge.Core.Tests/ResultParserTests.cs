using Forge.Core;
using Xunit;

namespace Forge.Core.Tests;

public class ResultParserTests
{
    // ---- Cleaning: trim + code fences ----

    [Fact]
    public void Parse_TrimsSurroundingWhitespace()
    {
        var r = ResultParser.Parse("  hello  \n");
        Assert.Equal("hello", r.Text);
        Assert.False(r.IsEmpty);
    }

    [Fact]
    public void Parse_StripsBalancedCodeFences()
    {
        var r = ResultParser.Parse("```\ncorrected text\n```");
        Assert.Equal("corrected text", r.Text);
    }

    [Fact]
    public void Parse_StripsFencesWithLanguageTag()
    {
        var r = ResultParser.Parse("```text\ncorrected text\n```");
        Assert.Equal("corrected text", r.Text);
    }

    [Fact]
    public void Parse_SingleLineFence()
    {
        var r = ResultParser.Parse("```hello```");
        Assert.Equal("hello", r.Text);
    }

    [Fact]
    public void Parse_UnbalancedFence_DropsOnlyOpening()
    {
        var r = ResultParser.Parse("```\nhello world");
        Assert.Equal("hello world", r.Text);
    }

    [Fact]
    public void Parse_NoFences_LeavesTextUnchanged()
    {
        var r = ResultParser.Parse("plain text");
        Assert.Equal("plain text", r.Text);
    }

    // ---- Empty detection ----

    [Fact]
    public void Parse_Empty_IsEmptyTrue()
    {
        Assert.True(ResultParser.Parse("").IsEmpty);
        Assert.True(ResultParser.Parse("   ").IsEmpty);
        Assert.True(ResultParser.Parse(null!).IsEmpty);
    }

    [Fact]
    public void Parse_FencedEmpty_IsEmptyTrue()
    {
        // A fence with nothing inside still counts as empty after cleaning.
        Assert.True(ResultParser.Parse("```\n\n```").IsEmpty);
    }

    // ---- Error detection (refusals) ----

    [Fact]
    public void Parse_Refusal_IsErrorTrue()
    {
        Assert.True(ResultParser.Parse("I'm sorry, I cannot help with that.").HasError);
        Assert.True(ResultParser.Parse("죄송합니다. 할 수 없습니다.").HasError);
    }

    [Fact]
    public void Parse_NormalOutput_IsNotError()
    {
        Assert.False(ResultParser.Parse("The weather is nice today.").HasError);
        Assert.False(ResultParser.Parse("오늘 날씨가 좋다.").HasError);
    }

    // ---- Language leakage ----

    [Fact]
    public void Parse_TranslateKoreanOutput_IsMismatch()
    {
        // TranslateKrToEn expects English; a Korean output is a leak.
        var r = ResultParser.Parse("오늘 날씨가 좋습니다.", sourceText: "오늘 날씨", task: ForgeTask.TranslateKrToEn);
        Assert.True(r.LanguageMismatch);
    }

    [Fact]
    public void Parse_TranslateEnglishOutput_NoMismatch()
    {
        var r = ResultParser.Parse("The weather is nice.", sourceText: "오늘 날씨", task: ForgeTask.TranslateKrToEn);
        Assert.False(r.LanguageMismatch);
    }

    [Fact]
    public void Parse_ProofreadEnglishLeak_IsMismatch()
    {
        // Source is Korean, proofread should stay Korean; English output is a leak.
        var r = ResultParser.Parse("The weather is nice today.", sourceText: "오늘 날씨가 좋다", task: ForgeTask.Proofread);
        Assert.True(r.LanguageMismatch);
    }

    [Fact]
    public void Parse_ProofreadKoreanOutput_NoMismatch()
    {
        var r = ResultParser.Parse("오늘 날씨가 좋습니다.", sourceText: "오늘 날씨가 좋다", task: ForgeTask.Proofread);
        Assert.False(r.LanguageMismatch);
    }

    [Fact]
    public void Parse_NoTask_NoLanguageCheck()
    {
        // Without a task, the language check is always off.
        var r = ResultParser.Parse("오늘 날씨가 좋습니다.", sourceText: "today");
        Assert.False(r.LanguageMismatch);
    }

    // ---- Determinism ----

    [Fact]
    public void Parse_IsDeterministic()
    {
        var a = ResultParser.Parse("  hello  ", "src", ForgeTask.Summarize);
        var b = ResultParser.Parse("  hello  ", "src", ForgeTask.Summarize);
        Assert.Equal(a.Text, b.Text);
        Assert.Equal(a.IsEmpty, b.IsEmpty);
        Assert.Equal(a.HasError, b.HasError);
        Assert.Equal(a.LanguageMismatch, b.LanguageMismatch);
    }
}
