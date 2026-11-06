namespace Forge.Core;

/// <summary>
/// Parses raw model output into clean result text. Flags three failure modes:
/// empty output, a refusal, and a language leak — the translation must come out
/// in English, so if the output is (mostly) Korean it did not translate.
/// </summary>
public static class ResultParser
{
    public sealed record ParseResult(string Text, bool IsEmpty, bool HasError, bool LanguageMismatch);

    /// <param name="raw">The model output (after family-specific marker cleaning by <see cref="OutputPostProcessor"/>).</param>
    public static ParseResult Parse(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return new ParseResult("", true, false, false);

        var text = StripCodeFences(raw).Trim();
        var isEmpty = text.Length == 0;
        var hasError = !isEmpty && DetectRefusal(text);
        var languageMismatch = !isEmpty && IsMostlyHangul(text);
        return new ParseResult(text, isEmpty, hasError, languageMismatch);
    }

    /// <summary>
    /// Strips markdown code fences models sometimes wrap around their output:
    /// balanced fences (with or without a language tag), single-line fences, and
    /// an unbalanced opening fence. Plain text passes through unchanged.
    /// </summary>
    private static string StripCodeFences(string raw)
    {
        var t = raw.Trim();
        if (!t.StartsWith("```")) return t;

        // Balanced: ```...``` on one line, or an opening fence line + content + closing fence.
        if (t.EndsWith("```") && t.Length > 6)
            return t[3..^3].Trim();

        // Unbalanced opening fence: drop the fence line, keep the rest.
        var firstNewline = t.IndexOf('\n');
        if (firstNewline < 0) return t;
        return t[(firstNewline + 1)..].Trim();
    }

    /// <summary>Detects a refusal ("I'm sorry…", "죄송합니다…") in the output.</summary>
    private static bool DetectRefusal(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        return t.StartsWith("i'm sorry") || t.StartsWith("i am sorry")
            || t.Contains("cannot help") || t.Contains("can't help")
            || t.StartsWith("죄송합니다") || t.Contains("할 수 없습니다");
    }

    /// <summary>True when the letters of <paramref name="s"/> are mostly Hangul syllables.</summary>
    private static bool IsMostlyHangul(string s)
    {
        var letters = 0;
        var hangul = 0;
        foreach (var c in s)
        {
            if (char.IsLetter(c))
            {
                letters++;
                if (c >= '\uAC00' && c <= '\uD7A3') hangul++;
            }
        }
        return letters > 0 && hangul * 2 > letters;
    }
}
