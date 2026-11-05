using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forge.Core;

namespace Forge.App;

/// <summary>
/// A hotkey field that captures a key combination on click instead of requiring the user to
/// type it. Click the field, then press the keys (e.g. Ctrl+Shift+E); the binding is filled in
/// and raised via <see cref="ValueChanged"/>. Press Esc while recording to clear the binding.
///
/// Capture uses a low-level keyboard hook (WH_KEYBOARD_LL) rather than WPF key events: the hook
/// sees every keystroke in real time — physical *and* software-injected (SendInput, as used by
/// accessibility virtual keyboards such as AltKey) — independent of window focus. It also hands
/// us the raw Win32 virtual-key code directly, so no WPF Key-enum mapping is needed.
/// Derives from <see cref="Border"/> so it can host a single label and take keyboard focus.
/// </summary>
public sealed class HotkeyField : Border
{
    private readonly TextBlock _text;
    private string _value = "";
    private bool _capturing;

    // Stable reference to the hook callback (must not be GC-collected while the hook is live).
    private readonly HookProc _hookProc;
    private IntPtr _hookId = IntPtr.Zero;
    private DispatcherTimer? _captureTimeout;

    public HotkeyField()
    {
        Focusable = true;
        Cursor = System.Windows.Input.Cursors.Hand;
        CornerRadius = new CornerRadius(6);
        Background = ForgeTheme.FieldBrush;
        BorderBrush = ForgeTheme.BorderBrush;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(10, 5, 10, 5);

        _text = new TextBlock
        {
            Foreground = ForgeTheme.MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        };
        Child = _text;

        _hookProc = HookCallback;
        MouseLeftButtonUp += OnMouseClick;
        LostKeyboardFocus += (_, _) => EndCapture();
        UpdateDisplay();
    }

    /// <summary>
    /// The binding in its "Ctrl+Shift+E" display form. Empty means unbound. Setting this
    /// programmatically (e.g. initial population) does not raise <see cref="ValueChanged"/>;
    /// only a user capture does.
    /// </summary>
    public string Text
    {
        get => _value;
        set
        {
            _value = value ?? "";
            UpdateDisplay();
        }
    }

    /// <summary>Raised after the user records (or clears) a binding — the auto-save hook.</summary>
    public event EventHandler? ValueChanged;

    private void OnMouseClick(object sender, MouseButtonEventArgs e)
    {
        Focus();
        BeginCapture();
        e.Handled = true;
    }

    private void BeginCapture()
    {
        _capturing = true;
        BorderBrush = ForgeTheme.AccentBrush;
        Background = ForgeTheme.HoverFieldBrush;
        _text.Text = "Press keys…";
        _text.Foreground = ForgeTheme.AccentBrush;

        // Install the low-level hook on the UI thread (which has a message loop).
        if (_hookId == IntPtr.Zero)
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, IntPtr.Zero, 0);

        // Safety: drop the global hook if the user walks away without recording.
        _captureTimeout?.Stop();
        _captureTimeout = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(15) };
        _captureTimeout.Tick += (_, _) => EndCapture();
        _captureTimeout.Start();
    }

    private void EndCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        _captureTimeout?.Stop();
        _captureTimeout = null;
        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
        UpdateDisplay();
    }

    // Runs on the UI thread, synchronously as each keystroke is delivered.
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _capturing)
        {
            int msg = wParam.ToInt32();
            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN or WM_KEYUP or WM_SYSKEYUP)
            {
                int vk = (int)Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam).vkCode;
                bool keyUp = msg is WM_KEYUP or WM_SYSKEYUP;
                HandleCaptureKey(vk, keyUp);
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void HandleCaptureKey(int vk, bool keyUp)
    {
        if (keyUp) return; // act on down-edges only
        if (vk == VK_ESCAPE)
        {
            SetValue(""); // clear the binding
            EndCapture();
            return;
        }
        if (!IsCapturableVk(vk)) return; // ignore everything else

        // Read the modifiers physically held AT THIS instant. The hook fires in real time as the
        // main key is delivered, so the modifiers are still down — GetAsyncKeyState reads ground
        // truth independent of focus, and works for both physical keys and SendInput (AltKey).
        int mods = 0;
        if (IsDown(VK_LCONTROL) || IsDown(VK_RCONTROL)) mods |= HotkeyBinding.ModControl;
        if (IsDown(VK_LSHIFT) || IsDown(VK_RSHIFT)) mods |= HotkeyBinding.ModShift;
        if (IsDown(VK_LMENU) || IsDown(VK_RMENU)) mods |= HotkeyBinding.ModAlt;

        if (mods == 0)
        {
            // A bare key is not a valid global hotkey — hint, and keep recording.
            _text.Text = "Add Ctrl / Shift / Alt";
            _text.Foreground = ForgeTheme.AccentBrush;
            return;
        }

        SetValue(FormatBinding(mods, vk));
        EndCapture();
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private void SetValue(string text)
    {
        _value = text ?? "";
        UpdateDisplay();
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateDisplay()
    {
        if (_capturing) return; // the capture prompt is already showing
        BorderBrush = ForgeTheme.BorderBrush;
        Background = ForgeTheme.FieldBrush;
        if (_value.Length == 0)
        {
            _text.Text = "Click to record";
            _text.Foreground = ForgeTheme.MutedBrush;
        }
        else
        {
            _text.Text = _value;
            _text.Foreground = ForgeTheme.TextBrush;
        }
    }

    /// <summary>Formats a captured binding into its "Ctrl+Shift+E" display form.</summary>
    public static string FormatBinding(int modifiers, int vk)
    {
        var parts = new List<string>();
        if ((modifiers & HotkeyBinding.ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & HotkeyBinding.ModShift) != 0) parts.Add("Shift");
        if ((modifiers & HotkeyBinding.ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & HotkeyBinding.ModWin) != 0) parts.Add("Win");
        parts.Add(KeyName(vk));
        return string.Join("+", parts);
    }

    private static string KeyName(int vk)
    {
        if (vk is >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= '0' and <= '9') return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return $"F{vk - 0x6F}";
        return "?";
    }

    private static bool IsCapturableVk(int vk) => (vk is >= 0x41 and <= 0x5A) || (vk is >= '0' and <= '9') || (vk is >= 0x70 and <= 0x87);

    // ── Win32 low-level keyboard hook ─────────────────────────────────────────
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int VK_LSHIFT = 0x10, VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0x11, VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0x12, VK_RMENU = 0xA4;
    private const int VK_ESCAPE = 0x1B;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwRes);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
