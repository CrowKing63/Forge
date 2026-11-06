using Forge.Core;
using Xunit;

namespace Forge.Core.Tests;

public class ReplaceActionTests
{
    [Theory]
    [InlineData("chrome.exe", ReplaceAction.AppSupportLevel.Partial)]
    [InlineData("notepad.exe", ReplaceAction.AppSupportLevel.Full)]
    [InlineData("unknown-app", ReplaceAction.AppSupportLevel.Unknown)]
    public void Lookup_ReturnsExpectedLevel(string processName, ReplaceAction.AppSupportLevel expected) =>
        Assert.Equal(expected, ReplaceAction.Lookup(processName));
}
