using System.IO;
using System.Text.Json;
using Forge.Core;

namespace Forge.App;

/// <summary>
/// An immediate-type hotkey: pressing it runs one task directly, without the
/// overlay (S6). Read from a simple JSON config file; the settings UI that
/// edits that file is S7 — here we only read it.
/// </summary>
public sealed record ImmediateHotkey(ForgeTask Task, HotkeyBinding Binding);

/// <summary>
/// Reads hotkey bindings from %APPDATA%\Forge\hotkeys.json:
///   { "popup": "Ctrl+E",
///     "immediate": [ { "task": "proofread", "hotkey": "Ctrl+Shift+P" } ] }
/// A missing or invalid file yields the default popup binding and no immediate
/// hotkeys. Entries that fail to parse are skipped.
/// </summary>
public static class HotkeyConfig
{
    /// <summary>Default popup-type binding (S6 spec).</summary>
    public const string DefaultPopupBinding = "Ctrl+E";

    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Forge", "hotkeys.json");

    public static (string Popup, ImmediateHotkey[] Immediate) Load()
    {
        var popup = DefaultPopupBinding;
        var immediate = new List<ImmediateHotkey>();

        try
        {
            if (!File.Exists(ConfigPath))
                return (popup, immediate.ToArray());

            using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
            if (doc.RootElement.TryGetProperty("popup", out var p) && p.ValueKind == JsonValueKind.String)
                popup = p.GetString()!;

            if (doc.RootElement.TryGetProperty("immediate", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in list.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object)
                        continue;
                    if (!entry.TryGetProperty("task", out var taskProp) || taskProp.ValueKind != JsonValueKind.String)
                        continue;
                    if (!entry.TryGetProperty("hotkey", out var hotkeyProp) || hotkeyProp.ValueKind != JsonValueKind.String)
                        continue;

                    if (TryParseTask(taskProp.GetString()!, out var task)
                        && HotkeyBinding.TryParse(hotkeyProp.GetString()!, out var binding))
                        immediate.Add(new ImmediateHotkey(task, binding));
                }
            }
        }
        catch (Exception)
        {
            // Unreadable/invalid config: fall back to defaults.
        }

        return (popup, immediate.ToArray());
    }

    private static bool TryParseTask(string name, out ForgeTask task)
    {
        switch (name.ToLowerInvariant())
        {
            case "proofread":
                task = ForgeTask.Proofread;
                return true;
            case "translate":
                task = ForgeTask.TranslateKrToEn;
                return true;
            case "rewrite":
                task = ForgeTask.Rewrite;
                return true;
            case "summarize":
                task = ForgeTask.Summarize;
                return true;
            default:
                task = default;
                return false;
        }
    }
}
