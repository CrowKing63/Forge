namespace Forge.Core;

/// <summary>
/// The cleaned model output plus detection flags, produced by <see cref="ResultParser"/>.
/// </summary>
public sealed class ParsedResult
{
    /// <summary>The output after trimming and code-fence removal.</summary>
    public string Text { get; }

    /// <summary>True when the cleaned text is empty or whitespace-only.</summary>
    public bool IsEmpty { get; }

    /// <summary>True when the output looks like a refusal/error message.</summary>
    public bool HasError { get; }

    /// <summary>
    /// True when a task (and, for same-language tasks, source text) was supplied and the
    /// output's language differs from the expected one. Always false when no task is given.
    /// </summary>
    public bool LanguageMismatch { get; }

    internal ParsedResult(string text, bool isEmpty, bool hasError, bool languageMismatch)
    {
        Text = text;
        IsEmpty = isEmpty;
        HasError = hasError;
        LanguageMismatch = languageMismatch;
    }
}

/// <summary>
/// Cleans and validates raw model output. Pure and deterministic: no I/O, same input → same output.
///
/// <see cref="Parse"/> trims whitespace, strips surrounding code fences, and flags three
/// conditions: empty output, refusal/error output, and language leakage (output in the wrong
/// language for the task). The language check needs a <paramref name="task"/>, and for
/// same-language tasks also the source text to know which language is expected.
/// Diff computation is the UI's job (S7); prompt-quality tuning is S13.
/// </summary>
public static class ResultParser
{
    private static readonly string[] ErrorMarkers =
    {
        "i'm sorry", "i am sorry", "i cannot", "i can't", "as an ai",
        "죄송합니다", "할 수 없습니다",
    };

    /// <summary>
    /// Cleans <paramref name="raw"/> and flags empty, error, and language-leak conditions.
    /// </summary>
    /// <param name="raw">Raw model output.</param>
    /// <param name="sourceText">Original selected text (needed for same-language tasks' check).</param>
    /// <param name="task">The task the output was produced for; enables the language check.</param>
    public static ParsedResult Parse(string raw, string? sourceText = null, ForgeTask? task = null)
    {
        string text = StripCodeFences(raw);
        bool isEmpty = string.IsNullOrWhiteSpace(text);
        bool hasError = !isEmpty && DetectError(text);
        bool languageMismatch = DetermineLanguageMismatch(text, sourceText, task);

        return new ParsedResult(text, isEmpty, hasError, languageMismatch);
    }

    internal static string StripCodeFences(string raw)
    {
        string s = (raw ?? string.Empty).Trim();
        if (!s.StartsWith("```")) return s;

        int contentStart = s.IndexOf('\n');
        if (contentStart < 0)
        {
            // Single-line fence: ```content```
            if (s.EndsWith("```") && s.Length > 6)
                return s.Substring(3, s.Length - 6).Trim();
            return s;
        }

        contentStart += 1; // skip past the newline that ends the opening fence line
        int closeStart = s.LastIndexOf("```", StringComparison.Ordinal);
        if (closeStart > contentStart)
            return s.Substring(contentStart, closeStart - contentStart).Trim();

        // Unbalanced: no closing fence — drop only the opening fence line.
        return s.Substring(contentStart).Trim();
    }

    internal static bool DetectError(string text)
    {
        string lower = text.ToLowerInvariant();
        foreach (var marker in ErrorMarkers)
            if (lower.Contains(marker, StringComparison.Ordinal))
                return true;
        return false;
    }

    internal static bool DetermineLanguageMismatch(string text, string? sourceText, ForgeTask? task)
    {
        if (task is null || string.IsNullOrWhiteSpace(text)) return false;

        bool outKorean = IsPredominantlyKorean(text);

        switch (task.Value)
        {
            case ForgeTask.TranslateKrToEn:
                // Expected output is English → flag when it came out Korean.
                return outKorean;
            case ForgeTask.Proofread:
            case ForgeTask.Rewrite:
            case ForgeTask.Summarize:
                // Expected output matches the source language; can't judge without it.
                if (string.IsNullOrWhiteSpace(sourceText)) return false;
                return outKorean != IsPredominantlyKorean(sourceText);
            default:
                return false;
        }
    }

    internal static bool IsPredominantlyKorean(string text)
    {
        int korean = 0, latin = 0;
        foreach (char c in text)
        {
            if (c >= 0xAC00 && c <= 0xD7A3) korean++;              // Hangul syllables
            else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin++;
        }
        return korean > latin;
    }
}
