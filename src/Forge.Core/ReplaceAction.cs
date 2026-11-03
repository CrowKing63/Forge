using System.Runtime.InteropServices;

namespace Forge.Core;

/// <summary>
/// Best-effort replace (plan §5): focuses the source window captured at task start and
/// pastes the result over its selection (Ctrl+V via SendInput). No success guarantee —
/// when focus or the clipboard fails, the result stays on the clipboard with guidance
/// (clipboard fallback). The clipboard is backed up before and restored after a
/// successful replace (plan §4: non-destructive).
///
/// Win32 P/Invoke only — no UI framework dependency in Core (same approach as
/// <see cref="TextCapture"/>).
/// </summary>
public static class ReplaceAction
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    /// <summary>What a replace attempt ended up as.</summary>
    public enum Outcome
    {
        /// <summary>The result was pasted over the source app's selection (best-effort success).</summary>
        Replaced,
        /// <summary>Replace could not be performed; the result is on the clipboard for manual paste.</summary>
        ClipboardFallback,
    }

    /// <summary>The outcome of a replace attempt plus a user-facing message.</summary>
    public sealed record ReplaceResult(Outcome Outcome, string Message);

    /// <summary>
    /// Captures the foreground window handle. Call this before any Forge UI takes focus
    /// (e.g. when the task overlay is about to be shown) so the replace can return focus
    /// to the app where the selection was made.
    /// </summary>
    public static IntPtr CaptureSourceWindow() => GetForegroundWindow();

    /// <summary>
    /// Best-effort replace: puts <paramref name="resultText"/> on the clipboard, focuses
    /// <paramref name="sourceWindow"/>, and sends Ctrl+V so the app pastes over its
    /// selection. The previous clipboard content is restored afterwards (or the
    /// clipboard is cleared when it held no text).
    ///
    /// When the source window is gone or cannot be focused, the result is left on the
    /// clipboard and <see cref="Outcome.ClipboardFallback"/> is returned with guidance.
    /// </summary>
    public static ReplaceResult TryReplace(string resultText, IntPtr sourceWindow)
    {
        if (string.IsNullOrWhiteSpace(resultText))
            return new ReplaceResult(Outcome.ClipboardFallback, "Nothing to replace — the result is empty.");

        string? backup = TextCapture.ReadClipboardText();

        if (!SetClipboardText(resultText))
            return new ReplaceResult(Outcome.ClipboardFallback,
                "The clipboard could not be written. Use the Copy button and paste manually.");

        if (sourceWindow == IntPtr.Zero || !IsWindow(sourceWindow) || !SetForegroundWindow(sourceWindow))
            return new ReplaceResult(Outcome.ClipboardFallback,
                "Could not focus the source app — the result is on the clipboard. Paste it where you need it (Ctrl+V).");

        Thread.Sleep(300); // let the target process the focus before the keystroke lands
        SendCtrlV();

        // The target reads the clipboard during its paste; give it a beat before we restore
        // the backup, or it would paste the backup instead of the result. 600ms covers
        // Notepad (Win11), whose paste is asynchronous and slower than the classic edit
        // control — 250ms was empirically not enough (the document ended up with the
        // backup text).
        Thread.Sleep(600);
        if (backup is not null)
            SetClipboardText(backup);
        else
            ClearClipboard();

        return new ReplaceResult(Outcome.Replaced, "Replaced in the source app.");
    }

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
        new AppSupportEntry("notepad.exe", AppSupportLevel.Full, "Standard edit control — paste over a selection works."),
        new AppSupportEntry("winword.exe", AppSupportLevel.Full, "Word replaces the selection on paste."),
        new AppSupportEntry("chrome.exe", AppSupportLevel.Partial, "Works in editable areas (address bar, text fields); page body is not editable."),
        new AppSupportEntry("msedge.exe", AppSupportLevel.Partial, "Works in editable areas (address bar, text fields); page body is not editable."),
    };

    /// <summary>Looks up a process in the support matrix; <see cref="AppSupportLevel.Unknown"/> when absent.</summary>
    public static AppSupportLevel Lookup(string processName) =>
        SupportedApps.FirstOrDefault(e => string.Equals(e.ProcessName, processName, StringComparison.OrdinalIgnoreCase))?.Level
        ?? AppSupportLevel.Unknown;

    /// <summary>Writes text to the clipboard (Win32, with a retry for transient locks).</summary>
    internal static bool SetClipboardText(string text)
    {
        if (!OpenClipboardWithRetry())
            return false;

        try
        {
            EmptyClipboard();
            IntPtr hGlobal = GlobalAlloc(GmemMoveable, (uint)((text.Length + 1) * sizeof(char)));
            if (hGlobal == IntPtr.Zero)
                return false;

            IntPtr pStr = GlobalLock(hGlobal);
            if (pStr == IntPtr.Zero)
            {
                GlobalFree(hGlobal);
                return false;
            }

            Marshal.Copy(text.ToCharArray(), 0, pStr, text.Length); // UTF-16; the null terminator is already zeroed by GlobalAlloc
            GlobalUnlock(hGlobal);
            bool ok = SetClipboardData(CfUnicodeText, hGlobal) != IntPtr.Zero;
            if (!ok)
                GlobalFree(hGlobal); // on failure we own the memory
            return ok;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Clears the clipboard (used to restore an empty state after a successful replace).</summary>
    internal static void ClearClipboard()
    {
        if (!OpenClipboardWithRetry())
            return;
        try
        {
            EmptyClipboard();
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool OpenClipboardWithRetry()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
                return true;
            Thread.Sleep(50); // the clipboard is transiently locked by another app
        }
        return false;
    }

    private static void SendCtrlV()
    {
        var inputs = new INPUT[]
        {
            new() { type = InputKeyboard, data = new InputUnion { ki = new KeybdInput { wVk = VkControl, dwFlags = 0 } } },
            new() { type = InputKeyboard, data = new InputUnion { ki = new KeybdInput { wVk = VkV, dwFlags = 0 } } },
            new() { type = InputKeyboard, data = new InputUnion { ki = new KeybdInput { wVk = VkControl, dwFlags = KeyEventfKeyUp } } },
            new() { type = InputKeyboard, data = new InputUnion { ki = new KeybdInput { wVk = VkV, dwFlags = KeyEventfKeyUp } } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private const int InputKeyboard = 0; // INPUT_KEYBOARD
    private const uint KeyEventfKeyUp = 0x0002;
    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;

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
    private static extern bool OpenClipboard(IntPtr hWndNewClip);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint format, IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, uint dwBytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] inputs, int cbSize);
}
