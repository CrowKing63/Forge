using System.Runtime.InteropServices;

namespace Forge.Core;

/// <summary>
/// Reads the active window's selected text from the clipboard (primary path) and
/// enforces a maximum input-token guard before the text is handed to the pipeline.
///
/// Clipboard-first by design (plan §5/§7): UIA range reading is only an auxiliary
/// fallback and is out of scope here — the real UIA implementation lands in S4/S12. The
/// token count is a conservative *estimate*; S4 replaces it with the engine's precise
/// tokenizer.
///
/// <see cref="Capture()"/> reads the OS clipboard; <see cref="Capture(string?)"/> validates
/// already-obtained text and is the unit-testable core (no clipboard access).
/// </summary>
public sealed class TextCapture
{
    private const int CfUnicodeText = 13;

    /// <summary>Maximum input tokens accepted before rejection.</summary>
    public int MaxInputTokens { get; }

    public TextCapture(int contextSize = 8192, int safetyMargin = 4096)
    {
        if (contextSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(contextSize), contextSize, "Context size must be positive.");
        if (safetyMargin < 0)
            throw new ArgumentOutOfRangeException(nameof(safetyMargin), safetyMargin, "Safety margin cannot be negative.");
        if (safetyMargin >= contextSize)
            throw new ArgumentException("Safety margin must be smaller than the context size.", nameof(safetyMargin));

        MaxInputTokens = contextSize - safetyMargin;
    }

    /// <summary>
    /// Reads the current clipboard text and validates it. Throws
    /// <see cref="TextCaptureException"/> when there is no readable text, the input is
    /// too long, or the clipboard cannot be opened.
    /// </summary>
    public string Capture() => Capture(ReadClipboardText());

    /// <summary>
    /// Validates already-obtained clipboard text (the testable core — no OS access).
    /// <paramref name="clipboardText"/> is null when the clipboard held no text data.
    /// Returns the text exactly as captured (no trimming); whitespace-only counts as empty.
    /// </summary>
    public string Capture(string? clipboardText)
    {
        if (string.IsNullOrWhiteSpace(clipboardText))
            throw new TextCaptureException(TextCaptureError.EmptyClipboard,
                "No text was found on the clipboard. Select some text and copy it (Ctrl+C), then try again.");

        int estimatedTokens = EstimateTokens(clipboardText);
        if (estimatedTokens > MaxInputTokens)
            throw new TextCaptureException(TextCaptureError.InputTooLong,
                $"The selection is about {estimatedTokens} tokens, which exceeds the maximum of {MaxInputTokens}. Select a shorter passage and try again.");

        return clipboardText;
    }

    /// <summary>
    /// Conservative token *estimate* for a length guard: ASCII ≈ 4 chars/token, CJK/Hangul
    /// ≈ 1 char/token (an upper bound). Deliberately overestimates so the guard never
    /// under-shoots the context window. Replaced by the engine's precise tokenizer in S4.
    /// </summary>
    public static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        int ascii = 0, nonAscii = 0;
        foreach (char c in text)
        {
            if (c < 128) ascii++; else nonAscii++;
        }

        return (ascii + 3) / 4 + nonAscii;
    }

    /// <summary>Reads the OS clipboard (Win32 P/Invoke — no WinForms/WPF dependency in Core).</summary>
    internal static string? ReadClipboardText()
    {
        bool opened = false;
        for (int attempt = 0; attempt < 3 && !opened; attempt++)
        {
            opened = OpenClipboard(IntPtr.Zero);
            if (!opened) Thread.Sleep(50); // clipboard is transiently locked by another app
        }

        if (!opened)
            throw new TextCaptureException(TextCaptureError.ClipboardUnavailable,
                "The clipboard is locked by another application and could not be read. Try again.");

        try
        {
            if (!IsClipboardFormatAvailable(CfUnicodeText))
                return null; // empty, or a non-text payload (e.g., an image)

            IntPtr hGlobal = GetClipboardData(CfUnicodeText);
            if (hGlobal == IntPtr.Zero)
                return null;

            IntPtr pStr = GlobalLock(hGlobal);
            string? text = pStr == IntPtr.Zero ? null : Marshal.PtrToStringUni(pStr);
            if (pStr != IntPtr.Zero) GlobalUnlock(hGlobal);
            return text;
        }
        finally
        {
            CloseClipboard();
        }
    }

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewClip);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(int format);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);
}

/// <summary>Reason a <see cref="TextCapture"/> capture failed.</summary>
public enum TextCaptureError
{
    /// <summary>The clipboard held no readable text (empty, whitespace-only, or a non-text payload such as an image).</summary>
    EmptyClipboard,
    /// <summary>The selection exceeds the maximum input-token limit.</summary>
    InputTooLong,
    /// <summary>The OS clipboard could not be opened (locked by another application).</summary>
    ClipboardUnavailable,
}

/// <summary>Thrown when a <see cref="TextCapture"/> capture fails; carries the specific <see cref="Error"/>.</summary>
public sealed class TextCaptureException : Exception
{
    public TextCaptureError Error { get; }

    public TextCaptureException(TextCaptureError error, string message) : base(message)
    {
        Error = error;
    }
}
