using System.IO;

namespace Forge.App;

/// <summary>
/// Optional debug log (S6): when launched with "--debug-log &lt;path&gt;", app
/// lifecycle events are appended to that file so the overlay/hotkey flow can be
/// smoke-tested without a human at the keyboard. No-op when no path is set.
/// </summary>
internal static class DebugLog
{
    private static readonly object Gate = new();
    private static string? _path;

    public static void SetPath(string? path) => _path = path;

    public static void Write(string message)
    {
        var path = _path;
        if (path is null)
            return;
        lock (Gate)
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
    }
}
