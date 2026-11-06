namespace Forge.Core;

/// <summary>
/// Resolves where Forge keeps its user data (settings.json, Models/, CrashDumps/).
///
/// Portable mode: an empty <c>portable.txt</c> marker next to the executable switches
/// the data directory from <c>%LOCALAPPDATA%\Forge</c> to the folder containing the
/// exe, so a portable copy (single exe + portable.txt) is fully self-contained — copy
/// the folder to a USB drive and go. The marker check lives in
/// <see cref="ResolveDataDir"/> so tests can exercise both branches without touching
/// the real executable's directory.
/// </summary>
public static class AppPaths
{
    /// <summary>The portable marker file name, placed next to the executable.</summary>
    public const string PortableMarker = "portable.txt";

    /// <summary>
    /// The data directory: the exe's folder when the portable marker is present,
    /// otherwise <c>%LOCALAPPDATA%\Forge</c>.
    /// </summary>
    public static string DataDir => ResolveDataDir(AppContext.BaseDirectory);

    /// <summary>True when the portable marker sits next to the executable.</summary>
    public static bool IsPortable => File.Exists(Path.Combine(AppContext.BaseDirectory, PortableMarker));

    internal static string ResolveDataDir(string baseDirectory)
    {
        if (File.Exists(Path.Combine(baseDirectory, PortableMarker)))
            return Path.GetFullPath(baseDirectory);
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Forge");
    }
}
