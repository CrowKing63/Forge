namespace Forge.Core;

/// <summary>
/// Idle-unload lifecycle (S8): after <c>IdleUnloadSeconds</c> of no inference, the loaded
/// model is unloaded so its memory is reclaimed; the next request reloads it on demand.
///
/// Pure state machine — the clock, load-state check, and unload action are injected, so
/// tests drive time manually with no real timers or native models. The app wires a 1-second
/// <see cref="System.Threading.Timer"/> to <see cref="Tick"/>.
/// </summary>
public sealed class IdleUnloadManager
{
    private readonly TimeSpan _timeout;
    private readonly Func<bool> _isLoaded;
    private readonly Action _unload;
    private readonly Func<DateTime> _now;

    private DateTime? _lastActivity;

    /// <param name="timeoutSeconds">Idle period before the model is unloaded.</param>
    /// <param name="isLoaded">Returns true while a model is loaded.</param>
    /// <param name="unload">Releases the model (engine <c>Unload</c>).</param>
    /// <param name="clock">Current time; defaults to <see cref="DateTime.UtcNow"/>.</param>
    public IdleUnloadManager(int timeoutSeconds, Func<bool> isLoaded, Action unload, Func<DateTime>? clock = null)
    {
        if (timeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), timeoutSeconds, "Idle timeout must be positive.");
        _timeout = TimeSpan.FromSeconds(timeoutSeconds);
        _isLoaded = isLoaded;
        _unload = unload;
        _now = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>Arms (or resets) the idle timer after a finished inference.</summary>
    public void NotifyInferenceCompleted()
    {
        _lastActivity = _now();
    }

    /// <summary>
    /// Advances the state machine. Unloads the model when the timeout has elapsed and a
    /// model is still loaded. Safe to call repeatedly; returns true exactly once, when
    /// the unload fires.
    /// </summary>
    public bool Tick()
    {
        var last = _lastActivity;
        if (last is null)
            return false;

        if (_now() - last.Value < _timeout)
            return false;

        if (!_isLoaded())
            return false;

        _unload();
        _lastActivity = null;
        return true;
    }
}
