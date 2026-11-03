namespace Forge.Core;

/// <summary>
/// Reads the active window's selected text from the clipboard (primary path) and
/// enforces a maximum input-token guard before the text is handed to the pipeline.
///
/// Clipboard-first by design (plan §5/§7): UIA range reading is only an auxiliary
/// fallback and is out of scope here — the real UIA implementation lands in S4/S12.
/// The token count uses an injected precise counter when one is supplied (S4 wires
/// in <see cref="InferenceEngine.CountTokens"/>); otherwise it falls back to the
/// conservative <see cref="EstimateTokens"/> estimate.
///
/// <see cref="Capture()"/> reads the OS clipboard; <see cref="Capture(string?)"/> validates
/// already-obtained text and is the unit-testable core (no clipboard access).
/// </summary>
public sealed class TextCapture
{
    private readonly Func<string, int>? _tokenCounter;

    /// <summary>Maximum input tokens accepted before rejection.</summary>
    public int MaxInputTokens { get; }

    /// <param name="contextSize">Context window size in tokens.</param>
    /// <param name="safetyMargin">Reserved tokens for the prompt overhead and output.</param>
    /// <param name="tokenCounter">
    /// Precise token counter (e.g. the engine's tokenizer). When null, falls back to
    /// the conservative <see cref="EstimateTokens"/> estimate.
    /// </param>
    public TextCapture(int contextSize = 8192, int safetyMargin = 4096, Func<string, int>? tokenCounter = null)
    {
        if (contextSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(contextSize), contextSize, "Context size must be positive.");
        if (safetyMargin < 0)
            throw new ArgumentOutOfRangeException(nameof(safetyMargin), safetyMargin, "Safety margin cannot be negative.");
        if (safetyMargin >= contextSize)
            throw new ArgumentException("Safety margin must be smaller than the context size.", nameof(safetyMargin));

        MaxInputTokens = contextSize - safetyMargin;
        _tokenCounter = tokenCounter;
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

        int tokens = _tokenCounter is not null ? _tokenCounter(clipboardText) : EstimateTokens(clipboardText);
        if (tokens > MaxInputTokens)
            throw new TextCaptureException(TextCaptureError.InputTooLong,
                $"The selection is {tokens} tokens, which exceeds the maximum of {MaxInputTokens}. Select a shorter passage and try again.");

        return clipboardText;
    }

    /// <summary>
    /// Conservative token *estimate* used as the fallback guard when no precise counter
    /// is injected: ASCII ≈ 4 chars/token, CJK/Hangul ≈ 1 char/token (an upper bound).
    /// Deliberately overestimates so the guard never under-shoots the context window.
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

    /// <summary>Reads the OS clipboard (shared Win32 primitives — <see cref="ClipboardGuard"/>).</summary>
    internal static string? ReadClipboardText()
    {
        var (opened, text) = ClipboardGuard.ReadText();
        if (!opened)
            throw new TextCaptureException(TextCaptureError.ClipboardUnavailable,
                "The clipboard is locked by another application and could not be read. Try again.");
        return text;
    }
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
