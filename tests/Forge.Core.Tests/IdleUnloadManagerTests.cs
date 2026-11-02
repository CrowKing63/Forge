using Forge.Core;
using Xunit;

namespace Forge.Core.Tests;

/// <summary>
/// S8 idle-unload state machine: timer elapses with a loaded model → unload fires exactly once;
/// a new inference request resets the clock. Driven with an injected fake clock — no real
/// timers, no native models.
/// </summary>
public class IdleUnloadManagerTests
{
    private sealed class FakeClock
    {
        public DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        public void Advance(int seconds) => Now = Now.AddSeconds(seconds);
    }

    [Fact]
    public void TickDoesNothingWhenModelIsNotLoaded()
    {
        var clock = new FakeClock();
        var manager = new IdleUnloadManager(5, () => false, () => throw new InvalidOperationException("must not unload"), () => clock.Now);

        Assert.False(manager.Tick());
        clock.Advance(60);
        Assert.False(manager.Tick());
    }

    [Fact]
    public void UnloadsWhenTimeoutElapsesAfterLastInference()
    {
        var clock = new FakeClock();
        int unloadCalls = 0;
        var manager = new IdleUnloadManager(5, () => true, () => unloadCalls++, () => clock.Now);

        manager.NotifyInferenceCompleted();
        clock.Advance(4);
        Assert.False(manager.Tick()); // before timeout
        clock.Advance(2);
        Assert.True(manager.Tick()); // elapsed
        Assert.Equal(1, unloadCalls);
    }

    [Fact]
    public void UnloadFiresOnlyOnce()
    {
        var clock = new FakeClock();
        int unloadCalls = 0;
        var manager = new IdleUnloadManager(5, () => true, () => unloadCalls++, () => clock.Now);

        manager.NotifyInferenceCompleted();
        clock.Advance(10);
        Assert.True(manager.Tick());
        // After the unload the model is gone: ticks must not fire again.
        var unloaded = new IdleUnloadManager(5, () => false, () => unloadCalls++, () => clock.Now);
        unloaded.NotifyInferenceCompleted();
        clock.Advance(10);
        Assert.False(unloaded.Tick());
        Assert.Equal(1, unloadCalls);
    }

    [Fact]
    public void NewRequestResetsTheTimer()
    {
        var clock = new FakeClock();
        int unloadCalls = 0;
        var manager = new IdleUnloadManager(5, () => true, () => unloadCalls++, () => clock.Now);

        manager.NotifyInferenceCompleted();
        clock.Advance(4);
        manager.NotifyInferenceCompleted(); // request at t=4s resets the clock
        clock.Advance(4);
        Assert.False(manager.Tick()); // only 4s since last request
        clock.Advance(2);
        Assert.True(manager.Tick());
        Assert.Equal(1, unloadCalls);
    }

    [Fact]
    public void ConstructorRejectsNonPositiveTimeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IdleUnloadManager(0, () => true, () => { }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IdleUnloadManager(-1, () => true, () => { }));
    }
}
