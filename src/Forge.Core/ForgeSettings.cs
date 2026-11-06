using System.Text.Json;

namespace Forge.Core;

/// <summary>Where Forge runs its inference. The detection logic is S8's; this is the setting surface.</summary>
public enum HardwareMode
{
    Cpu,
    Gpu,
    Auto,
}

/// <summary>When the loaded model is unloaded after idle time (S17); reload happens on demand at the next translation.</summary>
public enum ModelUnloadAfter
{
    /// <summary>The model is released as soon as each translation finishes.</summary>
    Immediate,
    OneMinute,
    FiveMinutes,
    /// <summary>No automatic unload — the model stays in memory until Forge exits.</summary>
    None,
}

/// <summary>
/// Forge user settings with JSON persistence.
///
/// Pure: no WPF dependency, so it is testable from the core test project. The app shell (S5)
/// loads it at startup; the settings window (S7) edits and saves it. GPU detection (S8) binds
/// to <see cref="Hardware"/>; the model catalog (S9) will bind to <see cref="ModelPath"/> later.
/// </summary>
public sealed class ForgeSettings
{
    /// <summary>
    /// Default GGUF path: the built-in model inside the managed models directory
    /// (<c>%LOCALAPPDATA%\Forge\Models</c>, S9). First-run onboarding (S10) downloads
    /// it there when missing.
    /// </summary>
    public static string DefaultModelPath => Path.Combine(
        ModelManager.DefaultModelsDir, ModelManager.DefaultModelId + ".gguf");

    /// <summary>Model file path (the model catalog in S9 will populate the choice list).</summary>
    public string ModelPath { get; set; } = DefaultModelPath;

    /// <summary>Context window size in tokens.</summary>
    public int ContextSize { get; set; } = 8192;

    /// <summary>
    /// When the loaded model is unloaded after idle time (S17); reload happens on demand
    /// at the next translation. Old settings files may carry a leftover "IdleUnloadSeconds"
    /// key — System.Text.Json ignores unknown properties, so it deserializes to the default.
    /// </summary>
    public ModelUnloadAfter UnloadAfter { get; set; } = ModelUnloadAfter.OneMinute;

    /// <summary>Hardware selection (CPU / GPU / Auto); resolved by <see cref="HardwareDetector"/> (S8).</summary>
    public HardwareMode Hardware { get; set; } = HardwareMode.Cpu;

    /// <summary>
    /// Opt-in crash dumps (plan §7): when true, a fatal exception writes a minimal dump
    /// (stack + model path + timestamp, no text content) to %LOCALAPPDATA%\Forge\CrashDumps.
    /// </summary>
    public bool CrashDumpEnabled { get; set; } = false;

    /// <summary>%LOCALAPPDATA%\Forge\settings.json</summary>
    public static string DefaultConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Forge", "settings.json");

    /// <summary>
    /// Loads settings from <paramref name="path"/> (default: <see cref="DefaultConfigPath"/>).
    /// Returns defaults when the file is missing, empty, or corrupt.
    /// </summary>
    public static ForgeSettings Load(string? path = null)
    {
        string file = path ?? DefaultConfigPath;
        try
        {
            if (!File.Exists(file)) return new ForgeSettings();
            var loaded = JsonSerializer.Deserialize<ForgeSettings>(File.ReadAllText(file), Options);
            return loaded ?? new ForgeSettings();
        }
        catch (Exception)
        {
            // Corrupt or partial file — fall back to defaults rather than crashing startup.
            return new ForgeSettings();
        }
    }

    /// <summary>Saves settings to <paramref name="path"/> (default: <see cref="DefaultConfigPath"/>).</summary>
    public void Save(string? path = null)
    {
        string file = path ?? DefaultConfigPath;
        var dir = Path.GetDirectoryName(file);
        if (dir is not null) Directory.CreateDirectory(dir);
        File.WriteAllText(file, JsonSerializer.Serialize(this, Options));
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };
}
