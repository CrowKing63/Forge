using System.Windows.Automation;
using System.Runtime.InteropServices;

namespace Forge.App;

/// <summary>
/// Diagnostics probe: reports what UI Automation sees as the focused element's
/// text selection. Used to tell "the selection was gone" apart from "the injected
/// Ctrl+C never reached the source app".
/// </summary>
internal static class UiaProbe
{
    /// <summary>The currently focused UIA element (null when there is none).</summary>
    public static AutomationElement? GetFocusedElement()
    {
        try
        {
            return AutomationElement.FocusedElement;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string DescribeFocusedSelection()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null)
                return "no focused element";
            if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
                return $"'{focused.Current.Name}' has no text pattern";
            var textPattern = (TextPattern)pattern;
            int total = 0;
            foreach (var range in textPattern.GetSelection())
                total += range.GetText(int.MaxValue).Length;
            return $"'{focused.Current.Name}': selection {total} chars";
        }
        catch (Exception ex)
        {
            return $"probe failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Reads the focused element's selected text directly via UIA — no clipboard, no
    /// injected keystrokes. Returns null when the focus has no text pattern or no
    /// selection, so callers can fall back to the clipboard path.
    /// </summary>
    public static string? GetFocusedSelectionText()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null)
                return null;
            if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
                return null;
            var textPattern = (TextPattern)pattern;
            var sb = new System.Text.StringBuilder();
            foreach (var range in textPattern.GetSelection())
                sb.Append(range.GetText(int.MaxValue));
            var text = sb.ToString();
            return text.Length > 0 ? text : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the focused element's full document text via UIA (for diagnostics —
    /// comparing the document before/after a paste attempt).
    /// </summary>
    public static string? GetFocusedDocumentText()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null)
                return null;
            if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
                return null;
            var textPattern = (TextPattern)pattern;
            return textPattern.DocumentRange.GetText(int.MaxValue);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// Replaces the source window's selected text via Win32 EM_REPLACESEL —
    /// no keyboard injection or clipboard use. Returns true when the target
    /// control accepted the message; EM_REPLACESEL itself has no return value.
    /// </summary>
    public static bool ReplaceSelectionText(IntPtr sourceWindow, string newText)
    {
        try
        {
            if (sourceWindow == IntPtr.Zero || !IsWindow(sourceWindow))
                return false;

            IntPtr hwnd = FindEditChild(sourceWindow);
            if (hwnd == IntPtr.Zero)
            {
                DebugLog.Write("ReplaceSelectionText: no edit control found in source window");
                return false;
            }

            string className = GetClassName(hwnd);
            DebugLog.Write($"replace target hwnd=0x{hwnd.ToInt64():X}, class={className}, source=0x{sourceWindow.ToInt64():X}");
            SetFocus(hwnd);
            Thread.Sleep(100);
            IntPtr replacement = Marshal.StringToHGlobalUni(newText);
            try
            {
                SendMessage(hwnd, 0x00C2 /* EM_REPLACESEL */, new IntPtr(1), replacement);
            }
            finally
            {
                Marshal.FreeHGlobal(replacement);
            }
            DebugLog.Write("replace EM_REPLACESEL sent");
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"ReplaceSelectionText failed: {ex.Message}");
            return false;
        }
    }

    private static IntPtr FindEditChild(IntPtr parent)
    {
        IntPtr richEdit = IntPtr.Zero;
        IntPtr fallback = IntPtr.Zero;
        EnumChildWindows(parent, (child, _) =>
        {
            string cls = GetClassName(child);
            if (cls == "RichEditD2DPT")
            {
                richEdit = child;
                return false;
            }

            if (fallback == IntPtr.Zero && (cls is "RICHEDIT50W" or "RICHEDIT20" or "EDIT"))
                fallback = child;
            else if (fallback == IntPtr.Zero && cls == "NotepadTextBox")
                fallback = child;
            return true;
        }, IntPtr.Zero);
        return richEdit != IntPtr.Zero ? richEdit : fallback;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static string GetClassName(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
