namespace Forge.Core;

/// <summary>
/// A parsed global-hotkey binding: Win32 modifier flags (MOD_*) plus a virtual
/// key code. Parsed from a simple "Modifier+...+Key" string read from the app's
/// config file (S6); the settings UI that writes that config is S7.
/// </summary>
public sealed record HotkeyBinding(int Modifiers, int Key)
{
    /// <summary>Win32 MOD_SHIFT.</summary>
    public const int ModShift = 0x0001;

    /// <summary>Win32 MOD_CONTROL.</summary>
    public const int ModControl = 0x0002;

    /// <summary>Win32 MOD_ALT.</summary>
    public const int ModAlt = 0x0004;

    /// <summary>Win32 MOD_WIN.</summary>
    public const int ModWin = 0x0008;

    /// <summary>
    /// Parses a binding string like "Ctrl+E" or "Ctrl+Shift+1". A valid binding
    /// has exactly one key (letter, digit, or F-key) and at least one modifier.
    /// Returns false for anything else.
    /// </summary>
    public static bool TryParse(string text, out HotkeyBinding binding)
    {
        binding = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        int modifiers = 0;
        int? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                    modifiers |= ModControl;
                    break;
                case "shift":
                    modifiers |= ModShift;
                    break;
                case "alt":
                    modifiers |= ModAlt;
                    break;
                case "win":
                    modifiers |= ModWin;
                    break;
                default:
                    if (!TryParseKey(part, out int vk))
                        return false;
                    if (key is not null)
                        return false; // more than one key
                    key = vk;
                    break;
            }
        }

        if (key is null || modifiers == 0)
            return false; // a bare key is not a global hotkey

        binding = new HotkeyBinding(modifiers, key.Value);
        return true;
    }

    private static bool TryParseKey(string part, out int vk)
    {
        if (part.Length == 1 && char.IsAsciiLetter(part[0]))
        {
            vk = char.ToUpperInvariant(part[0]);
            return true;
        }
        if (part.Length == 1 && char.IsDigit(part[0]))
        {
            vk = part[0]; // '0'..'9' are their own virtual key codes
            return true;
        }
        if (part.StartsWith("F", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(part.AsSpan(1), out int number)
            && number is >= 1 and <= 24)
        {
            vk = 0x70 + number - 1; // VK_F1 = 0x70
            return true;
        }
        vk = 0;
        return false;
    }
}
