using Forge.Core;
using Xunit;

namespace Forge.Core.Tests;

public class ResultParserTests
{
    [Fact]
    public void Parse_PlainText_ReturnsTrimmed()
    {
        var result = ResultParser.Parse("  The weather is nice today.  ");
        Assert.Equal("The weather is nice today.", result.Text);
        Assert.False(result.IsEmpty);
        Assert.False(result.HasError);
        Assert.False(result.LanguageMismatch);
    }

    [Fact]
    public void Parse_Null_OrEmpty_IsIsEmpty()
    {
        Assert.True(ResultParser.Parse(null!).IsEmpty);
        Assert.True(ResultParser.Parse("").IsEmpty);
        Assert.True(ResultParser.Parse("   ").IsEmpty);
    }

    [Fact]
    public void Parse_CodeFenceStripped()
    {
        var result = ResultParser.Parse("```\nThe weather is nice today.\n```");
        Assert.Equal("The weather is nice today.", result.Text);
    }

    [Fact]
    public void Parse_Refusal_Detected()
    {
        Assert.True(ResultParser.Parse("I'm sorry, I cannot help with that.").HasError);
        Assert.True(ResultParser.Parse("죄송합니다. 할 수 없습니다.").HasError);
        Assert.False(ResultParser.Parse("The weather is nice today.").HasError);
        Assert.False(ResultParser.Parse("오늘 날씨가 좋다.").HasError);
    }

    [Fact]
    public void Parse_KoreanOutput_IsLanguageMismatch()
    {
        // The translation must come out in English — Korean output means it did not translate.
        var result = ResultParser.Parse("오늘 날씨가 좋다.");
        Assert.True(result.LanguageMismatch);
    }

    [Fact]
    public void Parse_EnglishOutput_NoMismatch()
    {
        var result = ResultParser.Parse("The weather is nice today.");
        Assert.False(result.LanguageMismatch);
    }

    [Fact]
    public void Parse_SpanishOutputWithSpanishSource_IsLanguageMismatch()
    {
        // A Spanish input that comes back in Spanish did not translate.
        var result = ResultParser.Parse("Hace buen tiempo hoy.", "Hoy hace buen tiempo.");
        Assert.True(result.LanguageMismatch);
    }

    [Fact]
    public void Parse_EnglishOutputWithSpanishSource_NoMismatch()
    {
        var result = ResultParser.Parse("The weather is nice today.", "Hoy hace buen tiempo.");
        Assert.False(result.LanguageMismatch);
    }

    [Fact]
    public void Parse_KoreanSourceEnglishOutput_NoMismatch()
    {
        var result = ResultParser.Parse("The weather is nice today.", "오늘 날씨가 좋다.");
        Assert.False(result.LanguageMismatch);
    }

    [Fact]
    public void Parse_DigitOnlyText_NeverMismatches()
    {
        var result = ResultParser.Parse("12345", "67890");
        Assert.False(result.LanguageMismatch);
    }
}
