using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Forge.App;

/// <summary>
/// Global hotkeys via Win32 RegisterHotKey (S6). A hidden 1x1 HwndSource owns a
/// real HWND; WM_HOTKEY arrives on the UI thread through its hook. Registration
/// can fail when the combination is already in use by another app — callers
/// must handle that gracefully and continue without the binding.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private HwndSource? _source;
    private readonly List<int> _registeredIds = new();

    /// <summary>Raised on the UI thread with the hotkey id.</summary>
    public event Action<int>? HotkeyPressed;

    /// <summary>Creates the hidden message window. Must be called on the UI thread.</summary>
    public void Initialize()
    {
        if (_source is not null)
            return;
        _source = new HwndSource(new HwndSourceParameters("ForgeHotkeys", 1, 1));
        ShowWindow(_source.Handle, SW_HIDE); // hidden message window — never shown on screen or in the taskbar
        _source.AddHook(OnWmHotKey);
    }

    /// <summary>Registers a hotkey. Returns false when the combination is already in use.</summary>
    public bool Register(int id, int modifiers, int key)
    {
        if (_source is null)
            throw new InvalidOperationException("Initialize() must be called first.");
        if (!NativeMethods.RegisterHotKey(_source.Handle, id, (uint)modifiers, (uint)key))
            return false;
        _registeredIds.Add(id);
        return true;
    }

    /// <summary>Unregisters everything registered so far. Safe to call repeatedly.</summary>
    public void UnregisterAll()
    {
        if (_source is null)
            return;
        foreach (var id in _registeredIds)
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        _registeredIds.Clear();
    }

    private IntPtr OnWmHotKey(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // On 64-bit processes lParam is pointer-sized; the hotkey id lives in the low word.
        HotkeyPressed?.Invoke((int)(lParam.ToInt64() & 0xFFFF));
        handled = true;
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source?.Dispose();
        _source = null;
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_HIDE = 0;

    internal static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
