using Microsoft.Win32;

namespace Forge.App;

/// <summary>
/// Auto-start (S20): writes or removes the per-user Run key that starts Forge at sign-in.
/// HKCU — no administrator rights required. The value points at the running exe
/// (<see cref="Environment.ProcessPath"/>) so it works for both dev builds and published ones.
/// </summary>
public static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Forge";

    /// <summary>True when the Run key currently points at this process's exe.</summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) as string == Environment.ProcessPath;
    }

    /// <summary>Writes (or updates) the Run key when enabled; removes it when disabled.</summary>
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
            key.SetValue(ValueName, Environment.ProcessPath ?? "");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
