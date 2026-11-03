using System.Runtime.InteropServices;

namespace Forge.Core;

/// <summary>
/// Opt-in crash dump (plan §7): when a fatal exception is about to end the app, records
/// just enough to diagnose — timestamp, runtime, model path, GPU layer count, and the
/// exception stack — WITHOUT any text content (no clipboard, selection, result, or
/// prompt; plan §4 privacy). Files land in <c>%LOCALAPPDATA%\Forge\CrashDumps</c>.
/// </summary>
public static class CrashDump
{
    /// <summary>%LOCALAPPDATA%\Forge\CrashDumps</summary>
    public static string DumpDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Forge", "CrashDumps");

    /// <summary>
    /// Writes a crash dump and returns the file path, or null when it could not be
    /// written. Never throws — a dump must never take the app down with it.
    /// </summary>
    public static string? Write(Exception exception, string modelPath, int gpuLayerCount)
    {
        try
        {
            var dir = DumpDirectory;
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Forge crash dump — {DateTime.Now:O}");
            sb.AppendLine($"runtime: {RuntimeInformation.FrameworkDescription}");
            sb.AppendLine($"model path: {modelPath}");
            sb.AppendLine($"gpu layer count: {gpuLayerCount}");
            sb.AppendLine();
            sb.AppendLine(exception.ToString());

            File.WriteAllText(file, sb.ToString());
            return file;
        }
        catch
        {
            return null;
        }
    }
}
