// Standalone SendInput probe: types ' FORGE-OK ' into whatever window is focused.
// Used to answer: does ANY plain console process's synthetic input reach the
// desktop, or only trusted/signed ones (like the on-screen keyboard)?
using System.Runtime.InteropServices;

static class Program
{
    const int InputKeyboard = 1;
    const uint KeyUp = 0x0002;

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

    [DllImport("user32.dll")]
    static extern uint SendInput(uint nInputs, Input[] inputs, int cbSize);

    static void Key(char c)
    {
        var down = new Input { type = InputKeyboard };
        down.u.ki.wVk = c;
        var up = new Input { type = InputKeyboard };
        up.u.ki.wVk = c;
        up.u.ki.dwFlags = KeyUp;
        uint sent = SendInput(2, new[] { down, up }, Marshal.SizeOf<Input>());
        if (sent != 2)
            Console.WriteLine($"SendInput returned {sent} for '{c}'");
    }

    static void Main()
    {
        Console.WriteLine("In 3 seconds this will type ' FORGE-OK ' into the focused window.");
        Console.WriteLine("Focus Notepad (or any text field) and wait.");
        Thread.Sleep(3000);
        foreach (char c in " FORGE-OK ")
        {
            Key(c);
            Thread.Sleep(50);
        }
        Console.WriteLine("Done. Check the window for ' FORGE-OK '.");
    }
}
