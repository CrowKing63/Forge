using Forge.Core;
using Xunit;

namespace Forge.Core.Tests;

/// <summary>
/// S11 ReplaceAction tests. The focus/paste path is verified live (Notepad smoke);
/// here we cover the clipboard mechanics and the fallback/matrix logic, which need
/// no real target window.
/// </summary>
public class ReplaceActionTests
{
    [Fact]
    public void SetClipboardText_WritesReadableText()
    {
        Assert.True(ReplaceAction.SetClipboardText("forge-s11-test-abc"));
        Assert.Equal("forge-s11-test-abc", TextCapture.ReadClipboardText());
    }

    [Fact]
    public void TryReplace_EmptyResult_FallsBackWithoutTouchingClipboard()
    {
        ReplaceAction.SetClipboardText("keep-me");

        var result = ReplaceAction.TryReplace("   ", IntPtr.Zero);

        Assert.Equal(ReplaceAction.Outcome.ClipboardFallback, result.Outcome);
        Assert.Equal("keep-me", TextCapture.ReadClipboardText()); // untouched
    }

    [Fact]
    public void TryReplace_InvalidWindow_FallsBackAndLeavesResultOnClipboard()
    {
        var result = ReplaceAction.TryReplace("hello-from-forge", IntPtr.Zero);

        Assert.Equal(ReplaceAction.Outcome.ClipboardFallback, result.Outcome);
        Assert.Contains("clipboard", result.Message, StringComparison.OrdinalIgnoreCase);
        // The fallback contract: the result is on the clipboard for a manual paste.
        Assert.Equal("hello-from-forge", TextCapture.ReadClipboardText());
    }

    [Fact]
    public void TryReplace_NonZeroInvalidHandle_FallsBack()
    {
        // A non-zero handle that is not a real window must fall back, not crash.
        var result = ReplaceAction.TryReplace("hello", new IntPtr(0xDEAD));

        Assert.Equal(ReplaceAction.Outcome.ClipboardFallback, result.Outcome);
        Assert.Equal("hello", TextCapture.ReadClipboardText());
    }

    [Theory]
    [InlineData("notepad.exe", ReplaceAction.AppSupportLevel.Full)]
    [InlineData("winword.exe", ReplaceAction.AppSupportLevel.Full)]
    [InlineData("chrome.exe", ReplaceAction.AppSupportLevel.Partial)]
    [InlineData("msedge.exe", ReplaceAction.AppSupportLevel.Partial)]
    [InlineData("some-other-app.exe", ReplaceAction.AppSupportLevel.Unknown)]
    public void Matrix_LookupByProcessName(string process, ReplaceAction.AppSupportLevel expected)
    {
        Assert.Equal(expected, ReplaceAction.Lookup(process));
    }

    [Fact]
    public void Matrix_LookupIsCaseInsensitive()
    {
        Assert.Equal(ReplaceAction.AppSupportLevel.Full, ReplaceAction.Lookup("NOTEPAD.EXE"));
    }
}
