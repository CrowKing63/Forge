namespace Forge.Core;

/// <summary>
/// Parses raw model output into clean result text. Flags three failure modes:
/// empty output, a refusal, and a language leak — the translation must come
/// out in English, so if the output stays in the source language it did not
/// translate. When <paramref name="sourceText"/> is supplied the check
/// compares dominant scripts (any source language); without it the legacy
/// Hangul-only check applies.
/// </summary>
public static class ResultParser
{
    public sealed record ParseResult(string Text, bool IsEmpty, bool HasError, bool LanguageMismatch);

    /// <param name="raw">The model output (after family-specific marker cleaning by <see cref="OutputPostProcessor"/>).</param>
    /// <param name="sourceText">Optional source text used to detect a language leak for any script.</param>
    public static ParseResult Parse(string? raw, string? sourceText = null)
    {
        if (string.IsNullOrEmpty(raw))
            return new ParseResult("", true, false, false);

        var text = StripCodeFences(raw).Trim();
        var isEmpty = text.Length == 0;
        var hasError = !isEmpty && DetectRefusal(text);
        var languageMismatch = false;
        if (!isEmpty)
        {
            languageMismatch = sourceText is null || string.IsNullOrWhiteSpace(sourceText)
                ? IsMostlyHangul(text)
                : SameDominantScript(text, sourceText) && HighWordOverlap(text, sourceText);
        }
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

    /// <summary>
    /// True when <paramref name="output"/> and <paramref name="source"/> share the same
    /// dominant script — i.e. the model answered in the source language instead of
    /// translating. Texts with no letters (digits/symbols only) never mismatch.
    /// </summary>
    private static bool SameDominantScript(string output, string source)
    {
        var a = DominantScript(output);
        var b = DominantScript(source);
        return a >= 0 && a == b;
    }

    /// <summary>
    /// True when the output shares most of its content words with the source —
    /// needed for same-script pairs (e.g. Spanish in, Spanish out) where script
    /// comparison alone cannot tell a leak from a translation. Shared means
    /// lowercased letters-only tokens of length >= 3; the threshold is half of
    /// the smaller side's word count.
    /// </summary>
    private static bool HighWordOverlap(string output, string source)
    {
        var outWords = ContentWords(output);
        var srcWords = ContentWords(source);
        if (outWords.Count == 0 || srcWords.Count == 0) return false;

        var shared = 0;
        foreach (var w in outWords)
            if (srcWords.Contains(w)) shared++;

        var smaller = Math.Min(outWords.Count, srcWords.Count);
        return shared * 2 >= smaller;
    }

    private static System.Collections.Generic.HashSet<string> ContentWords(string s)
    {
        var words = new System.Collections.Generic.HashSet<string>();
        var token = new System.Text.StringBuilder();
        foreach (var c in s.ToLowerInvariant())
        {
            if (char.IsLetter(c)) token.Append(c);
            else if (token.Length >= 3) words.Add(token.ToString());
            token.Clear();
        }
        if (token.Length >= 3) words.Add(token.ToString());
        return words;
    }

    /// <summary>
    /// Returns the id of the script carrying most letters in <paramref name="s"/>
    /// (Hangul=1, Latin=2, Cyrillic=3, Han=4, Arabic=5, Devanagari=6, Greek=7),
    /// or -1 when the text has no letters.
    /// </summary>
    private static int DominantScript(string s)
    {
        var counts = new int[7];
        foreach (var c in s)
        {
            if (!char.IsLetter(c)) continue;
            if (c >= '\uAC00' && c <= '\uD7A3') counts[0]++;
            else if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) counts[1]++;
            else if (c >= '\u0400' && c <= '\u04FF') counts[2]++;
            else if (c >= '\u4E00' && c <= '\u9FFF') counts[3]++;
            else if (c >= '\u0600' && c <= '\u06FF') counts[4]++;
            else if (c >= '\u0900' && c <= '\u097F') counts[5]++;
            else if (c >= '\u0370' && c <= '\u03FF') counts[6]++;
        }
        var best = -1;
        for (var i = 0; i < counts.Length; i++)
            if (counts[i] > 0 && (best < 0 || counts[i] > counts[best])) best = i;
        return best;
    }
}
