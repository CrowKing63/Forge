using System.IO;
using System.Text.Json;

namespace Forge.App;

/// <summary>
/// Hotkey configuration for the single translate action (S16: the app has one hotkey).
/// Stored as JSON at %LOCALAPPDATA%\Forge\hotkeys.json. When the file is missing or
/// corrupt, the default binding (<see cref="DefaultTranslateBinding"/>) is used —
/// the config can never break the app.
/// </summary>
public static class HotkeyConfig
{
    /// <summary>The default translate hotkey.</summary>
    public const string DefaultTranslateBinding = "Ctrl+E";

    /// <summary>%LOCALAPPDATA%\Forge\hotkeys.json</summary>
    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Forge", "hotkeys.json");

    /// <summary>
    /// Loads the translate binding. Reads the "translate" key, falling back to the
    /// legacy "popup" key (same binding, pre-S16 name) so existing configs keep working.
    /// </summary>
    public static string Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return DefaultTranslateBinding;

            using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
            JsonElement? el = null;
            if (doc.RootElement.TryGetProperty("translate", out var t))
                el = t;
            else if (doc.RootElement.TryGetProperty("popup", out var p))
                el = p;
            if (el.HasValue && el.Value.ValueKind == JsonValueKind.String)
                return el.Value.GetString()!;
        }
        catch
        {
            // Corrupt config → default. The app must stay usable.
        }
        return DefaultTranslateBinding;
    }

    /// <summary>Writes the translate binding to the config file (S20: saved from the wizard).</summary>
    public static void Save(string binding)
    {
        var payload = new { translate = binding };
        var dir = Path.GetDirectoryName(ConfigPath);
        if (dir is not null)
            Directory.CreateDirectory(dir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>The hotkey id used when registering the translate binding.</summary>
    public const int TranslateId = 1;

    /// <summary>
    /// Parses a "Modifiers+Key" binding (e.g. "Ctrl+V") into Win32 RegisterHotKey
    /// values: MOD_* modifier flags and a virtual key code. Returns false when the
    /// binding is not supported (only Ctrl/Alt/Shift/Win + A-Z / 0-9 are valid).
    /// </summary>
    public static bool TryParse(string binding, out int modifiers, out int key)
    {
        modifiers = 0;
        key = 0;

        var parts = binding.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;

        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl": modifiers |= 0x0002; break; // MOD_CONTROL
                case "alt": modifiers |= 0x0001; break; // MOD_ALT
                case "shift": modifiers |= 0x0004; break; // MOD_SHIFT
                case "win": modifiers |= 0x0008; break; // MOD_WIN
                default: return false;
            }
        }

        var last = parts[^1];
        if (last.Length == 1)
        {
            var c = last[0];
            if (c is >= 'A' and <= 'Z') { key = c; return true; } // VK codes for A-Z are the ASCII values
            if (c is >= '0' and <= '9') { key = c; return true; }
        }
        return false;
    }
}
