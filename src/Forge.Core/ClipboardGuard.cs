using System.Runtime.InteropServices;

namespace Forge.Core;

/// <summary>
/// Shared Win32 clipboard primitives (S12): every replace and copy path in the app
/// goes through here so the non-destructive backup/restore contract (plan §4) is
/// implemented once. No UI framework dependency in Core (same approach as
/// <see cref="TextCapture"/>).
/// </summary>
public static class ClipboardGuard
{
    private const int CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    /// <summary>
    /// Reads the current clipboard text. Returns (false, null) when the clipboard could
    /// not be opened (locked by another app); (true, null) when it opened but held no
    /// text payload (empty, or e.g. an image).
    /// </summary>
    public static (bool Opened, string? Text) ReadText()
    {
        if (!OpenClipboardWithRetry())
            return (false, null);

        try
        {
            if (!IsClipboardFormatAvailable(CfUnicodeText))
                return (true, null);

            IntPtr hGlobal = GetClipboardData((uint)CfUnicodeText);
            if (hGlobal == IntPtr.Zero)
                return (true, null);

            IntPtr pStr = GlobalLock(hGlobal);
            string? text = pStr == IntPtr.Zero ? null : Marshal.PtrToStringUni(pStr);
            if (pStr != IntPtr.Zero) GlobalUnlock(hGlobal);
            return (true, text);
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Writes text to the clipboard (with a retry for transient locks).</summary>
    public static bool WriteText(string text)
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
            bool ok = SetClipboardData((uint)CfUnicodeText, hGlobal) != IntPtr.Zero;
            if (!ok)
                GlobalFree(hGlobal); // on failure we own the memory
            return ok;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Clears the clipboard (no-op when it cannot be opened).</summary>
    public static void Clear()
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

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewClip);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(int format);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint format);

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
}
