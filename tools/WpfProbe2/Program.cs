// Binary-search probe, round 3: does the hotkey-triggered flow break SendInput?
// A-J as before (all confirmed working). Then:
//   K - typed after a REAL Ctrl+E hotkey press (RegisterHotKey + self-sent via SendInput,
//       simulating GazeStick) triggers the Forge flow: UIA capture -> inference -> SetFocus -> type.
// If K is missing, the hotkey-triggered path is the culprit.
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Automation;
using System.Windows.Threading;

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
    const int WM_HOTKEY = 0x0312;
    const uint MOD_CONTROL = 0x0002;
    const uint MOD_SHIFT = 0x0004;
    static string LogPath;
    static Dispatcher? _dispatcher;

    [DllImport("user32.dll")]
    static extern uint SendInput(uint nInputs, Input[] inputs, int cbSize);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    // Clipboard P/Invoke (replicating Forge's ClipboardGuard)
    [DllImport("user32.dll")] static extern bool OpenClipboardProbe(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool CloseClipboardProbe();
    [DllImport("user32.dll")] static extern bool EmptyClipboardProbe();
    [DllImport("user32.dll")] static extern IntPtr SetClipboardDataProbe(uint format, IntPtr hMem);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalAllocProbe(uint uFlags, uint dwBytes);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLockProbe(IntPtr hMem);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlockProbe(IntPtr hMem);

    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idTarget, bool attach);

    static void Log(string text)
    {
        try { File.AppendAllText(LogPath, text); } catch { }
    }

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

    // Send Ctrl+Shift+F12 as a key chord (simulates GazeStick pressing the hotkey).
    static void SendCtrlE()
    {
        var ctrlDown = new Input { type = InputKeyboard };
        ctrlDown.u.ki.wVk = 0x11; // VK_CONTROL
        var shiftDown = new Input { type = InputKeyboard };
        shiftDown.u.ki.wVk = 0x10; // VK_SHIFT
        var f12Down = new Input { type = InputKeyboard };
        f12Down.u.ki.wVk = 0x7B; // VK_F12
        var f12Up = new Input { type = InputKeyboard };
        f12Up.u.ki.wVk = 0x7B;
        f12Up.u.ki.dwFlags = KeyUp;
        var shiftUp = new Input { type = InputKeyboard };
        shiftUp.u.ki.wVk = 0x10;
        shiftUp.u.ki.dwFlags = KeyUp;
        var ctrlUp = new Input { type = InputKeyboard };
        ctrlUp.u.ki.wVk = 0x11;
        ctrlUp.u.ki.dwFlags = KeyUp;
        SendInput(6, new[] { ctrlDown, shiftDown, f12Down, f12Up, shiftUp, ctrlUp }, Marshal.SizeOf<Input>());
    }

    static AutomationElement? ReadFocusedElement()
    {
        try
        {
            var el = AutomationElement.FocusedElement;
            _ = el.Current.Name;
            return el;
        }
        catch
        {
            return null;
        }
    }

    static IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            Log("WM_HOTKEY received - running flow\n");
            _ = Task.Run(async () =>
            {
                try
                {
                    // Forge flow: UIA capture -> inference -> SetFocus -> type
                    var el = ReadFocusedElement();
                    Log($"hotkey-flow: el={(el == null ? "null" : "ok")}\n");
                    var engine = new Forge.Core.InferenceEngine(8192, 0);
                    var modelPath = Forge.Core.ForgeSettings.Load().ModelPath;
                    engine.LoadModel(modelPath);
                    await foreach (var _t in engine.Complete("Hello", 5, CancellationToken.None)) { }
                    if (el != null) { try { el.SetFocus(); } catch { } Thread.Sleep(200); }
                    _dispatcher?.Invoke(() => Type("K "));
                    Log("K typed (hotkey-triggered flow, UI thread)\n");
                }
                catch (Exception ex)
                {
                    Log("\nHOTKEY FLOW FAILED: " + ex);
                }
            });
            handled = true;
        }
        return IntPtr.Zero;
    }

    [STAThread]
    static void Main()
    {
        LogPath = Environment.GetEnvironmentVariable("TEMP") + "\\wpfprobe2.log";
        File.WriteAllText(LogPath, "started " + DateTime.Now);

        var app = new Application();
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log("\nUNHANDLED: " + e.Exception);
            e.Handled = true;
        };

        // Hidden 1x1 HwndSource, exactly like Forge's hotkey window (shown via ShowWindow 0).
        var source = new HwndSource(new HwndSourceParameters("WpfProbe", 1, 1));
        ShowWindow(source.Handle, 0);
        Log("hidden hwndsource created\n");

        // Hotkey hook: when WM_HOTKEY arrives, run the Forge flow and type K.
        source.AddHook(HotkeyHook);

        var label = new TextBlock { Text = "Click into Notepad. I'll start as soon as it's in front.", Margin = new Thickness(8) };
        var win = new Window
        {
            Title = "WpfProbe",
            Width = 340,
            Height = 90,
            Content = label,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        win.Show();
        Log("window shown\n");
        IntPtr ourHwnd = new WindowInteropHelper(win).Handle;
        var dispatcher = win.Dispatcher;
        _dispatcher = dispatcher;
        ((HwndSource)PresentationSource.FromVisual(win)!).AddHook(HotkeyHook);

        _ = Task.Run(() =>
        {
            try
            {
                Log("polling loop started\n");
                while (true)
                {
                    IntPtr fg = GetForegroundWindow();
                    if (fg != IntPtr.Zero && fg != ourHwnd) break;
                    Thread.Sleep(200);
                }
                Log("foreground changed - settling 1s\n");
                Thread.Sleep(1000);

                dispatcher.Invoke(() =>
                {
                    try
                    {
                        IntPtr fg = GetForegroundWindow();
                        Log($"\nforeground={(fg == IntPtr.Zero ? "none" : "hwnd")}\n");

                        // A: baseline (known to work)
                        Type("A ");
                        Log("A typed\n");
                        Thread.Sleep(400);

                        // B: + SetForegroundWindow
                        SetForegroundWindow(fg);
                        Type("B ");
                        Log("B typed\n");
                        Thread.Sleep(400);

                        // C: + UIA read of the focused element
                        var el = ReadFocusedElement();
                        Type("C ");
                        Log($"C typed (el={(el == null ? "null" : "ok")})\n");
                        Thread.Sleep(400);

                        // D: + UIA SetFocus + 200ms sleep
                        if (el != null)
                        {
                            try { el.SetFocus(); } catch { }
                            Thread.Sleep(200);
                        }
                        Type("D ");
                        Log("D typed\n");
                        Thread.Sleep(400);

                        // E: full Forge sequence
                        SetForegroundWindow(fg);
                        Type("E ");
                        Log("E typed\n");
                        Thread.Sleep(400);

                        // F: baseline right before a 2s gap
                        Type("F ");
                        Log("F typed\n");
                        label.Text = "Waiting 2 seconds (simulated inference)...";
                        Thread.Sleep(2000);

                        // G: after the 2s gap
                        Type("G ");
                        Log("G typed (after 2s gap)\n");
                        label.Text = "Now loading the real model...";
                    }
                    catch (Exception ex)
                    {
                        Log("\nSTEP FAILED: " + ex);
                        label.Text = "Step failed - see " + LogPath;
                        return;
                    }
                });

                // Model stages on this background thread (like Forge's LoadModelAsync).
                var engine = new Forge.Core.InferenceEngine(8192, 0);
                var modelPath = Forge.Core.ForgeSettings.Load().ModelPath;
                Log($"loading model: {modelPath}\n");
                engine.LoadModel(modelPath);
                Log("model loaded\n");
                dispatcher.Invoke(() => Type("H "));
                Log("H typed (after model load)\n");
                dispatcher.Invoke(() => label.Text = "Running a small inference...");

                var inferTask = Task.Run(async () =>
                {
                    await foreach (var _ in engine.Complete("Hello", 5, CancellationToken.None)) { }
                });
                inferTask.Wait();
                Log("inference done\n");
                dispatcher.Invoke(() => Type("I "));
                Log("I typed (after inference)\n");
                dispatcher.Invoke(() => label.Text = "Now testing hotkey flow...");

                // J: with the hidden HwndSource present since startup
                Thread.Sleep(300);
                dispatcher.Invoke(() => Type("J "));
                Log("J typed (hidden hwndsource active)\n");

                // K: send a Ctrl+V chord (exactly like Forge's SendCtrlV).
                Thread.Sleep(500);
                // DIAGNOSTIC: use AttachThreadInput to gain foreground rights, then
                // SetForegroundWindow on the CURRENT foreground (Notepad), then Ctrl+V.
                IntPtr targetHwnd = GetForegroundWindow();
                uint targetThread = GetWindowThreadProcessId(targetHwnd, out _);
                uint ourThread = GetCurrentThreadId();
                AttachThreadInput(ourThread, targetThread, true);
                SetForegroundWindow(targetHwnd);
                Thread.Sleep(300);
                IntPtr fgCheck = GetForegroundWindow();
                Log($"foreground after attach+set: {(fgCheck == targetHwnd ? "target (Notepad)" : $"MISMATCH ({fgCheck})")}\n");
                AttachThreadInput(ourThread, targetThread, false);

                var ctrlDown = new Input { type = InputKeyboard };
                ctrlDown.u.ki.wVk = 0x11;
                var vDown = new Input { type = InputKeyboard };
                vDown.u.ki.wVk = 0x56;
                var vUp = new Input { type = InputKeyboard };
                vUp.u.ki.wVk = 0x56;
                vUp.u.ki.dwFlags = KeyUp;
                var ctrlUp = new Input { type = InputKeyboard };
                ctrlUp.u.ki.wVk = 0x11;
                ctrlUp.u.ki.dwFlags = KeyUp;
                uint sent = SendInput(4, new[] { ctrlDown, vDown, vUp, ctrlUp }, Marshal.SizeOf<Input>());
                Log($"Ctrl+V chord sent: {sent} inputs processed\n");

                // L: wait 5s, then type L.
                Thread.Sleep(5000);
                dispatcher.Invoke(() => Type("L "));
                Log("L typed\n");

                // M: create a TOPMOST overlay window (like Forge's overlay), then type M.
                // If M doesn't appear -> the topmost window is intercepting input!
                Thread.Sleep(500);
                Window? overlay = null;
                dispatcher.Invoke(() =>
                {
                    overlay = new Window
                    {
                        Width = 400,
                        Height = 200,
                        WindowStyle = WindowStyle.None,
                        AllowsTransparency = true,
                        Background = System.Windows.Media.Brushes.White,
                        Topmost = true,
                        ShowInTaskbar = false,
                        Title = "ProbeOverlay"
                    };
                    overlay.Show();
                });
                Log("topmost overlay shown\n");
                Thread.Sleep(500);
                dispatcher.Invoke(() => Type("M "));
                Log("M typed (topmost overlay active)\n");
            }
            catch (Exception ex)
            {
                Log("\nTASK FAILED: " + ex);
            }
        });

        app.Run();
        Log("\napp.Run returned - exiting\n");
    }
}
