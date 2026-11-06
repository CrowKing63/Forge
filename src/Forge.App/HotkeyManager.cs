using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Forge.App;

/// <summary>
/// Global hotkeys via Win32 RegisterHotKey (S6). A message-only HwndSource (parent HWND_MESSAGE)
/// owns a real HWND; WM_HOTKEY arrives on the UI thread through an AddHook. Message-only windows
/// are never shown on screen — no title bar, taskbar entry, or Alt+Tab presence — so startup
/// shows no flash at all. No window class is registered (no RegisterClassEx), which sidesteps the
/// ERROR_INVALID_PARAMETER failure the native CreateWindowEx path hit. Registration can fail when
/// the combination is already in use by another app — callers must handle that gracefully and
/// continue without the binding.
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
        if (_source != null)
            return;
        var parameters = new HwndSourceParameters("ForgeHotkeys")
        {
            // HWND_MESSAGE (-3): the window becomes message-only — invisible by design,
            // never painted, no taskbar entry, no Alt+Tab presence.
            ParentWindow = new IntPtr(-3),
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProcHook);
    }

    /// <summary>Registers a hotkey. Returns false when the combination is already in use.</summary>
    public bool Register(int id, int modifiers, int key)
    {
        if (_source == null)
            throw new InvalidOperationException("Initialize() must be called first.");
        // MOD_NOREPEAT (0x4000): without it, holding the key floods WM_HOTKEY dozens of times.
        const int MOD_NOREPEAT = 0x4000;
        if (!NativeMethods.RegisterHotKey(_source.Handle, id, (uint)(modifiers | MOD_NOREPEAT), (uint)key))
            return false;
        _registeredIds.Add(id);
        return true;
    }

    /// <summary>Unregisters everything registered so far. Safe to call repeatedly.</summary>
    public void UnregisterAll()
    {
        if (_source == null)
            return;
        foreach (var id in _registeredIds)
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        _registeredIds.Clear();
    }

    private const int WM_HOTKEY = 0x0312;

    /// <summary>
    /// HwndSource hook. Runs on the UI thread: WPF's dispatcher pumps the standard Win32 message
    /// loop, which dispatches WM_HOTKEY (posted by RegisterHotKey) to this hook.
    /// </summary>
    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            HotkeyPressed?.Invoke(wParam.ToInt32());
            handled = true;
            return IntPtr.Zero;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source?.Dispose();
        _source = null;
    }

    internal static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
