using Forge.Core;
using Xunit;

namespace Forge.Core.Tests;

/// <summary>
/// Re-entry guard (plan §7): while a task is processing, further hotkey presses
/// are ignored — i.e. TryBegin succeeds exactly once per run.
/// </summary>
public class ProcessingGuardTests
{
    [Fact]
    public void IdleByDefault()
    {
        var guard = new ProcessingGuard();
        Assert.False(guard.IsProcessing);
    }

    [Fact]
    public void TryBegin_Succeeds_WhenIdle()
    {
        var guard = new ProcessingGuard();
        Assert.True(guard.TryBegin());
        guard.End();
    }

    [Fact]
    public void TryBegin_Fails_WhileProcessing_ReEntryGuard()
    {
        var guard = new ProcessingGuard();
        Assert.True(guard.TryBegin());

        // A second hotkey press during processing must be ignored.
        Assert.True(guard.IsProcessing);
        Assert.False(guard.TryBegin());
        Assert.False(guard.TryBegin());

        guard.End();
    }

    [Fact]
    public void End_ResetsToIdle_AndNextRunCanBegin()
    {
        var guard = new ProcessingGuard();
        Assert.True(guard.TryBegin());
        guard.End();

        Assert.False(guard.IsProcessing);
        Assert.True(guard.TryBegin());
        guard.End();
    }
}
