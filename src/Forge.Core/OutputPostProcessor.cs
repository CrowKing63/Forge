namespace Forge.Core;

/// <summary>
/// Cleans raw model output into user-facing text (S15). Hy-MT2 echoes its chat markers
/// (&lt;|hy_User|&gt;, &lt;|hy_Assistant|&gt;) and a leading &gt; into the output; Gemma 4 does not.
/// The marker positions are stable, so cutting between them is a reliable post-processing rule.
/// </summary>
public static class OutputPostProcessor
{
    private const string UserMarker = "<|hy_User|>";
    private const string AssistantMarker = "<|hy_Assistant|>";

    /// <summary>Strips model-specific artifacts from <paramref name="raw"/> for <paramref name="family"/>.</summary>
    public static string Clean(string raw, ModelFamily family) => family switch
    {
        ModelFamily.HyMt2 => StripHyMarkers(raw),
        _ => raw,
    };

    private static string StripHyMarkers(string raw)
    {
        int userStart = raw.IndexOf(UserMarker, StringComparison.Ordinal);
        int assistantEnd = raw.LastIndexOf(AssistantMarker, StringComparison.Ordinal);
        if (userStart >= 0 && assistantEnd > userStart + UserMarker.Length)
            return raw[(userStart + UserMarker.Length)..assistantEnd].Trim();

        // Fallback: no usable marker pair — drop every <|...|> token and trim.
        var stripped = System.Text.RegularExpressions.Regex.Replace(raw, @"<\|[^|]*\|>", " ");
        return stripped.Trim();
    }
}
