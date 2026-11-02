namespace Forge.Core;

/// <summary>
/// Re-entry guard (plan §7): while a task is being processed, further hotkey
/// presses are ignored. The InferenceEngine is single-consumer, so at most one
/// inference may run at a time; this guard enforces that from the app layer.
/// </summary>
public sealed class ProcessingGuard
{
    private int _state; // 0 = idle, 1 = processing

    /// <summary>True when a task is currently being processed.</summary>
    public bool IsProcessing => Volatile.Read(ref _state) == 1;

    /// <summary>
    /// Attempts to begin processing. Returns true only if no task was in
    /// progress; the caller must call <see cref="End"/> exactly once when done.
    /// </summary>
    public bool TryBegin() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;

    /// <summary>Marks processing as finished.</summary>
    public void End() => Interlocked.Exchange(ref _state, 0);
}
