// Minimal WPF SendInput probe — isolates WHY Forge's injected keys don't land.
// Mimics Forge's characteristics: a hidden 1x1 HwndSource (like the hotkey
// window) + a visible WPF window, and tests injection from two thread
// contexts in one run:
//   'WPF-UI' typed from the UI (Dispatcher) thread
//   'WPF-BG' typed from a background thread
// Focus Notepad first, then run. Whichever marker appears tells us which
// context is blocked.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

[StructLayout(LayoutKind.Sequential)]
struct KeybdInput
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
struct MouseInput
{
    public int dx;
    public int dy;
    public uint mouseData;
    public uint flags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
struct HardwareInput
{
    public uint msg;
    public ushort hi;
    public ushort lo;
}

[StructLayout(LayoutKind.Explicit)]
struct Union
{
    [FieldOffset(0)] public KeybdInput ki;
    [FieldOffset(0)] public MouseInput mi;
    [FieldOffset(0)] public HardwareInput hi;
}

[StructLayout(LayoutKind.Sequential)]
struct Input
{
    public int type;
    public Union u;
}

static class Probe
{
    const int InputKeyboard = 1;
    const uint KeyUp = 0x0002;

    [DllImport("user32.dll")]
    static extern uint SendInput(uint nInputs, Input[] inputs, int cbSize);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    static void Type(string text)
    {
        foreach (char c in text)
        {
            var down = new Input { type = InputKeyboard };
            down.u.ki.wVk = c;
            var up = new Input { type = InputKeyboard };
            up.u.ki.wVk = c;
            up.u.ki.dwFlags = KeyUp;
            SendInput(2, new[] { down, up }, Marshal.SizeOf<Input>());
            Thread.Sleep(50);
        }
    }

    [STAThread]
    static void Main()
    {
        var app = new Application();

        // The Forge-like hidden message window (1x1 HwndSource, never shown).
        var source = new HwndSource(new HwndSourceParameters("WpfProbe", 1, 1));
        Probe.ShowWindow(source.Handle, 0);

        var label = new TextBlock
        {
            Text = "Focus Notepad. Typing 'WPF-UI' in 3s, then 'WPF-BG'.",
            Margin = new Thickness(8),
        };
        var win = new Window
        {
            Title = "WpfProbe",
            Width = 320,
            Height = 90,
            Content = label,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        win.Show();

        var dispatcher = win.Dispatcher;

        // UI-thread injection (like Forge's hotkey handler).
        _ = Task.Delay(3000).ContinueWith(_ =>
            dispatcher.Invoke(() =>
            {
                Probe.Type("WPF-UI ");
                label.Text = "Typed 'WPF-UI' from the UI thread.";
            }));

        // Background-thread injection.
        _ = Task.Delay(4500).ContinueWith(_ =>
        {
            Probe.Type("WPF-BG ");
            dispatcher.Invoke(() => label.Text = "Typed 'WPF-BG' from a background thread.");
        });

        app.Run();
    }
}
