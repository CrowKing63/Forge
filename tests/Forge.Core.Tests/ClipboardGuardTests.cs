using Forge.Core;
using Xunit;

namespace Forge.Core.Tests;

/// <summary>
/// S12 ClipboardGuard tests: the shared clipboard primitives every replace/copy path
/// goes through. Runs in the "clipboard" collection (see ReplaceActionTests) because
/// the OS clipboard is a single shared resource.
/// </summary>
[Collection("clipboard")]
public class ClipboardGuardTests
{
    [Fact]
    public void WriteThenRead_RoundTrips()
    {
        string text = "clipboard-guard-test-" + Guid.NewGuid();
        Assert.True(ClipboardGuard.WriteText(text));

        var (opened, read) = ClipboardGuard.ReadText();
        Assert.True(opened);
        Assert.Equal(text, read);
    }

    [Fact]
    public void Clear_LeavesNoTextPayload()
    {
        string text = "clipboard-guard-clear-" + Guid.NewGuid();
        Assert.True(ClipboardGuard.WriteText(text));

        ClipboardGuard.Clear();

        var (opened, read) = ClipboardGuard.ReadText();
        Assert.True(opened);
        Assert.Null(read);
    }
}
