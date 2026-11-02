using Forge.Core;
using Xunit;

namespace Forge.Core.Tests;

public class TextCaptureTests
{
    // ---- Constructor guards ----

    [Fact]
    public void Constructor_RejectsNonPositiveContextSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextCapture(contextSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextCapture(contextSize: -1));
    }

    [Fact]
    public void Constructor_RejectsNegativeSafetyMargin()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextCapture(safetyMargin: -1));
    }

    [Fact]
    public void Constructor_RejectsSafetyMarginNotSmallerThanContext()
    {
        Assert.Throws<ArgumentException>(() => new TextCapture(contextSize: 8192, safetyMargin: 8192));
        Assert.Throws<ArgumentException>(() => new TextCapture(contextSize: 8192, safetyMargin: 9000));
    }

    [Fact]
    public void Constructor_DefaultsGive4096MaxInputTokens()
    {
        var capture = new TextCapture();
        Assert.Equal(4096, capture.MaxInputTokens); // 8192 - 4096
    }

    // ---- Empty / no-text paths ----

    [Fact]
    public void Capture_NullText_ThrowsEmptyClipboard()
    {
        var capture = new TextCapture();
        var ex = Assert.Throws<TextCaptureException>(() => capture.Capture((string?)null));
        Assert.Equal(TextCaptureError.EmptyClipboard, ex.Error);
    }

    [Fact]
    public void Capture_EmptyString_ThrowsEmptyClipboard()
    {
        var capture = new TextCapture();
        var ex = Assert.Throws<TextCaptureException>(() => capture.Capture(""));
        Assert.Equal(TextCaptureError.EmptyClipboard, ex.Error);
    }

    [Fact]
    public void Capture_WhitespaceOnly_ThrowsEmptyClipboard()
    {
        var capture = new TextCapture();
        var ex = Assert.Throws<TextCaptureException>(() => capture.Capture("   \n\t "));
        Assert.Equal(TextCaptureError.EmptyClipboard, ex.Error);
    }

    // ---- Valid path: exact text, no trimming ----

    [Fact]
    public void Capture_ValidText_ReturnsExactString()
    {
        var capture = new TextCapture();
        const string input = "  오늘 날씨가 좋다.  ";
        Assert.Equal(input, capture.Capture(input)); // returned untrimmed
    }

    // ---- Input-too-long guard ----

    [Fact]
    public void Capture_OverLimit_ThrowsInputTooLong()
    {
        // cap = 8 - 4 = 4 tokens. "한글네자다" is 5 CJK chars ≈ 5 tokens > 4.
        var capture = new TextCapture(contextSize: 8, safetyMargin: 4);
        var ex = Assert.Throws<TextCaptureException>(() => capture.Capture("한글네자다"));
        Assert.Equal(TextCaptureError.InputTooLong, ex.Error);
    }

    [Fact]
    public void Capture_AtLimit_IsAccepted()
    {
        // cap = 4 tokens. "한글네" is 4 CJK chars ≈ 4 tokens == limit → accepted.
        var capture = new TextCapture(contextSize: 8, safetyMargin: 4);
        Assert.Equal("한글네", capture.Capture("한글네"));
    }

    // ---- Token estimate (conservative upper bound) ----

    [Fact]
    public void EstimateTokens_Empty_IsZero()
    {
        Assert.Equal(0, TextCapture.EstimateTokens(""));
        Assert.Equal(0, TextCapture.EstimateTokens(null!));
    }

    [Fact]
    public void EstimateTokens_Ascii_UsesFourCharsPerToken()
    {
        Assert.Equal(2, TextCapture.EstimateTokens("hello"));     // 5 ascii -> (5+3)/4 = 2
        Assert.Equal(3, TextCapture.EstimateTokens("hello world")); // 11 ascii -> 14/4 = 3
    }

    [Fact]
    public void EstimateTokens_Cjk_UsesOneCharPerToken()
    {
        Assert.Equal(5, TextCapture.EstimateTokens("안녕하세요")); // 5 CJK chars
    }

    [Fact]
    public void EstimateTokens_Mixed_SumsBoth()
    {
        // "hello" (2) + " " (ascii) + "한글" (2) -> ascii=6 => (6+3)/4 = 2, + 2 CJK = 4
        Assert.Equal(4, TextCapture.EstimateTokens("hello 한글"));
    }

    // ---- Exception carries the reason ----

    [Fact]
    public void TextCaptureException_ExposesError()
    {
        var ex = new TextCaptureException(TextCaptureError.InputTooLong, "msg");
        Assert.Equal(TextCaptureError.InputTooLong, ex.Error);
        Assert.Equal("msg", ex.Message);
    }
}
