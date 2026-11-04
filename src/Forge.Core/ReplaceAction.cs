using System.Runtime.InteropServices;
using System.Text;

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

        if (!SetForegroundWindow(sourceWindow))
            return (null, "could not focus the source window");
        Thread.Sleep(300); // let the target take focus before the keystroke lands
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

        if (!ClipboardGuard.WriteText(resultText))
            return new ReplaceResult(Outcome.ClipboardFallback,
                "The clipboard could not be written. Use the Copy button and paste manually.");

        if (sourceWindow == IntPtr.Zero || !IsWindow(sourceWindow) || !SetForegroundWindow(sourceWindow))
            return new ReplaceResult(Outcome.ClipboardFallback,
                "Could not focus the source app — the result is on the clipboard. Paste it where you need it (Ctrl+V).");

        Thread.Sleep(300); // let the target process the focus before the keystroke lands

        // Re-assert focus immediately before injection — overlays (on-screen keyboards)
        // can steal the foreground back in the gap, and SendInput delivers to whatever
        // is foreground at that instant. Log where the keys actually go.
        // AttachThreadInput grants us foreground rights (Windows focus-stealing prevention).
        uint targetThread = GetWindowThreadProcessId(sourceWindow, out _);
        uint ourThread = GetCurrentThreadId();
        AttachThreadInput(ourThread, targetThread, true);
        SetForegroundWindow(sourceWindow);
        IntPtr fg = GetForegroundWindow();
        string fgInfo = DescribeWindow(fg) + (fg == sourceWindow ? " (matches source)" : " (MISMATCH — keys land here)");
        AttachThreadInput(ourThread, targetThread, false);

        // DIAGNOSTIC (temporary): UIPI analysis — our own integrity level. If we're
        // lower than the target (a normally launched app is Medium), Windows silently
        // drops injected input (SendInput still returns success).
        string integrityInfo = $"integrity self={IntegrityOfSelf()}";

        // Clipboard + Ctrl+V is the industry-standard delivery path — verified against
        // Chrome, VS Code and Notepad by the win-text-inject survey (July 2026).
        uint sent = SendCtrlV();

        // Do not type diagnostic text into the user's document. SendInput's return
        // value only means Windows accepted the events, not that the target pasted.
        // Keep the translation on the clipboard and report the result as unverified.
        return new ReplaceResult(Outcome.ClipboardFallback,
            "Paste could not be verified. The translation is still on the clipboard; paste it manually (Ctrl+V).",
            $"{integrityInfo}; foreground {fgInfo}; SendInput returned {sent}; clipboard kept");
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
        new AppSupportEntry("notepad.exe", AppSupportLevel.Full, "Paste over a selection works."),
        new AppSupportEntry("winword.exe", AppSupportLevel.Full, "Word replaces the selection on paste."),
        new AppSupportEntry("chrome.exe", AppSupportLevel.Partial, "Works in editable areas (address bar, text fields); page body is not editable."),
        new AppSupportEntry("msedge.exe", AppSupportLevel.Partial, "Works in editable areas (address bar, text fields); page body is not editable."),
    };

    /// <summary>Looks up a process in the support matrix; <see cref="AppSupportLevel.Unknown"/> when absent.</summary>
    public static AppSupportLevel Lookup(string processName) =>
        SupportedApps.FirstOrDefault(e => string.Equals(e.ProcessName, processName, StringComparison.OrdinalIgnoreCase))?.Level
        ?? AppSupportLevel.Unknown;

    /// <summary>Sends Ctrl+V via SendInput so the focused app pastes over its selection.</summary>
    private static uint SendCtrlV()
    {
        var inputs = new INPUT[]
        {
            new() { type = InputKeyboard, data = new InputUnion { ki = new KeybdInput { wVk = VkControl, dwFlags = 0 } } },
            new() { type = InputKeyboard, data = new InputUnion { ki = new KeybdInput { wVk = VkV, dwFlags = 0 } } },
            new() { type = InputKeyboard, data = new InputUnion { ki = new KeybdInput { wVk = VkControl, dwFlags = KeyEventfKeyUp } } },
            new() { type = InputKeyboard, data = new InputUnion { ki = new KeybdInput { wVk = VkV, dwFlags = KeyEventfKeyUp } } },
        };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

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

    /// <summary>
    /// DIAGNOSTIC (temporary): our own integrity level via 'whoami /groups' —
    /// the mandatory-label line reports Low/Medium/High. Used to check UIPI:
    /// injected input is silently dropped when the sender's integrity is lower
    /// than the target window's (a normally launched app is Medium).
    /// </summary>
    private static string IntegrityOfSelf()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("whoami", "/groups")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            foreach (var line in output.Split('\n'))
            {
                if (line.Contains("Mandatory Label"))
                {
                    // Format: "Mandatory Label\Medium Mandatory Level\tMandatory Group"
                    int start = line.IndexOf("Mandatory Label\\", StringComparison.Ordinal) + "Mandatory Label\\".Length;
                    string rest = line[start..];
                    int space = rest.IndexOf(' ');
                    return space > 0 ? rest[..space].Trim() : rest.Trim();
                }
            }
            return "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
