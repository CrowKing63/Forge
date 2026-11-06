using System.Runtime.InteropServices;
using System.Text;

namespace Forge.Core;

/// <summary>
/// Source-window capture helpers (plan §5): grabs the foreground window handle and
/// copies its selection to the clipboard (Ctrl+C via SendInput) so the direct
/// translate flow does not require the user to press Ctrl+C. The clipboard is read
/// before and compared after, so an unchanged clipboard means nothing was copied.
///
/// The replace itself is owned by the app's SelectionReplacer (EM_REPLACESEL /
/// UIA splice / clipboard fallback) — this class no longer sends Ctrl+V.
///
/// Win32 P/Invoke only — no UI framework dependency in Core (same approach as
/// <see cref="TextCapture"/>).
/// </summary>
public static class ReplaceAction
{
    /// <summary>What a replace attempt ended up as.</summary>
    public enum Outcome
    {
        /// <summary>The result was pasted over the source app's selection (best-effort success).</summary>
        Replaced,
        /// <summary>Replace could not be performed; the result is on the clipboard for manual paste.</summary>
        ClipboardFallback,
    }

    /// <summary>
    /// Copies the source window's current selection to the clipboard: focuses the
    /// window, sends Ctrl+C, and reads the result. Returns (text, failure) where
    /// failure is empty on success and a diagnostic reason otherwise. Used by the
    /// direct-translate flow so the user does not have to press Ctrl+C.
    /// </summary>
    public static (string? Text, string Failure) CopySelection(IntPtr sourceWindow)
    {
        if (sourceWindow == IntPtr.Zero || !IsWindow(sourceWindow))
            return (null, "source window is not a valid window");

        var (_, before) = ClipboardGuard.ReadText();

        // Only bring to front if it's NOT already the foreground window.
        // Calling SetForegroundWindow on an already-focused Chromium window
        // resets internal DOM focus (search field → page body).
        IntPtr fg = GetForegroundWindow();
        if (fg != sourceWindow)
        {
            if (!SetForegroundWindow(sourceWindow))
                return (null, "could not focus the source window");
            Thread.Sleep(300); // let the target take focus before the keystroke lands
        }
        uint sent = SendCtrlC();

        Thread.Sleep(600); // let the target finish writing the selection to the clipboard
        var (opened, after) = ClipboardGuard.ReadText();
        if (!opened)
            return (null, "clipboard was unreadable after the copy");

        // Unchanged content means Ctrl+C copied nothing (no selection in the source app).
        return before == after
            ? (null, $"the clipboard was unchanged — nothing was copied (SendInput returned {sent})")
            : (after, "");
    }

    /// <summary>A short description of a window (title + class) for diagnostics.</summary>
    public static string DescribeWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            return "invalid";
        var title = new StringBuilder(256);
        GetWindowText(hwnd, title, title.Capacity);
        var cls = new StringBuilder(256);
        GetClassName(hwnd, cls, cls.Capacity);
        return $"'{title}' [{cls}]";
    }

    /// <summary>The outcome of a replace attempt plus a user-facing message.</summary>
    public sealed record ReplaceResult(Outcome Outcome, string Message, string Diagnostics = "");

    /// <summary>
    /// Captures the foreground window handle. Call this before any Forge UI takes focus
    /// (e.g. when the task overlay is about to be shown) so the replace can return focus
    /// to the app where the selection was made.
    /// </summary>
    public static IntPtr CaptureSourceWindow() => GetForegroundWindow();

    /// <summary>Support matrix for the core apps (plan §5: best-effort, core apps first).</summary>
    public enum AppSupportLevel
    {
        /// <summary>Paste over a selection works reliably.</summary>
        Full,
        /// <summary>Works in editable areas only (address bar, text fields — not page body).</summary>
        Partial,
        /// <summary>Not specifically supported; the generic paste attempt is made and the
        /// clipboard fallback covers failure.</summary>
        Unknown,
    }

    /// <summary>One row of the per-app support matrix.</summary>
    public sealed record AppSupportEntry(string ProcessName, AppSupportLevel Level, string Notes);

    /// <summary>The app-by-app support matrix (core apps first; everything else falls back).</summary>
    public static readonly IReadOnlyList<AppSupportEntry> SupportedApps = new[]
    {
        new AppSupportEntry("notepad.exe", AppSupportLevel.Full, "Paste over a selection works."),
        new AppSupportEntry("winword.exe", AppSupportLevel.Full, "Word replaces the selection on paste."),
        new AppSupportEntry("chrome.exe", AppSupportLevel.Partial, "Works in editable areas (address bar, text fields); page body is not editable."),
        new AppSupportEntry("msedge.exe", AppSupportLevel.Partial, "Works in editable areas (address bar, text fields); page body is not editable."),
    };

    /// <summary>Looks up a process in the support matrix; <see cref="AppSupportLevel.Unknown"/> when absent.</summary>
    public static AppSupportLevel Lookup(string processName) =>
        SupportedApps.FirstOrDefault(e => string.Equals(e.ProcessName, processName, StringComparison.OrdinalIgnoreCase))?.Level
        ?? AppSupportLevel.Unknown;

    /// <summary>Sends Ctrl+C via SendInput so the focused app copies its selection.</summary>
    private static uint SendCtrlC()
    {
        var inputs = new INPUT[]
        {
            new() { type = InputKeyboard, data = new InputUnion { ki = new KeybdInput { wVk = VkControl, dwFlags = 0 } } },
            new() { type = InputKeyboard, data = new InputUnion { ki = new KeybdInput { wVk = VkC, dwFlags = 0 } } },
            new() { type = InputKeyboard, data = new InputUnion { ki = new KeybdInput { wVk = VkControl, dwFlags = KeyEventfKeyUp } } },
            new() { type = InputKeyboard, data = new InputUnion { ki = new KeybdInput { wVk = VkC, dwFlags = KeyEventfKeyUp } } },
        };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private const int InputKeyboard = 0; // INPUT_KEYBOARD
    private const uint KeyEventfKeyUp = 0x0002;
    private const ushort VkControl = 0x11;
    private const ushort VkC = 0x43;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput mi;
        [FieldOffset(0)] public KeybdInput ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int dx;
        public int dy;
        public int mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] inputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idTarget, bool attach);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
}
