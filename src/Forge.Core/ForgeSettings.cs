using System.Text.Json;

namespace Forge.Core;

/// <summary>Where Forge runs its inference. The detection logic is S8's; this is the setting surface.</summary>
public enum HardwareMode
{
    Cpu,
    Gpu,
    Auto,
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
    /// <summary>Default GGUF path (the S0-verified Gemma 4 E2B QAT model).</summary>
    public const string DefaultModelPath =
        @"C:\Users\uitaek\.cache\huggingface\hub\models--unsloth--gemma-4-E2B-it-qat-GGUF\snapshots\66a399f68ddd113b06dff02fca9523e55465d11d\gemma-4-E2B-it-qat-UD-Q4_K_XL.gguf";

    /// <summary>Model file path (the model catalog in S9 will populate the choice list).</summary>
    public string ModelPath { get; set; } = DefaultModelPath;

    /// <summary>Context window size in tokens.</summary>
    public int ContextSize { get; set; } = 8192;

    /// <summary>Seconds of idle time before the loaded model is unloaded (S8); reload happens on demand.</summary>
    public int IdleUnloadSeconds { get; set; } = 120;

    /// <summary>Hardware selection (CPU / GPU / Auto); resolved by <see cref="HardwareDetector"/> (S8).</summary>
    public HardwareMode Hardware { get; set; } = HardwareMode.Cpu;

    /// <summary>Per-task system prompts, keyed by <c>ForgeTask</c> name. Defaults from <see cref="PromptBuilder"/>.</summary>
    public Dictionary<string, string> Prompts { get; set; } = DefaultPrompts();

    /// <summary>%LOCALAPPDATA%\Forge\settings.json</summary>
    public static string DefaultConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Forge", "settings.json");

    private static Dictionary<string, string> DefaultPrompts()
    {
        var prompts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in Enum.GetValues<ForgeTask>())
            prompts[task.ToString()] = PromptBuilder.SystemPrompt(task);
        return prompts;
    }

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
