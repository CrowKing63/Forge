namespace Forge.Core;

/// <summary>
/// Cleans raw model output into user-facing text (S15). Hy-MT2 echoes its chat markers
/// (&lt;|hy_User|&gt;, &lt;|hy_Assistant|&gt;) and a leading &gt; into the output; Gemma 4
/// emits its reasoning inside &lt;|channel&gt;thought ... &lt;channel|&gt; tags. The marker
/// positions are stable, so cutting between them is a reliable post-processing rule.
/// </summary>
public static class OutputPostProcessor
{
    private const string UserMarker = "<|hy_User|>";
    private const string AssistantMarker = "<|hy_Assistant|>";
    private const string ChannelOpen = "<|channel>thought";
    private const string ChannelClose = "<channel|>";

    /// <summary>Strips model-specific artifacts from <paramref name="raw"/> for <paramref name="family"/>.</summary>
    public static string Clean(string raw, ModelFamily family) => family switch
    {
        ModelFamily.HyMt2 => StripHyMarkers(raw),
        ModelFamily.Gemma4 => StripGemma4Channels(raw),
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

    private static string StripGemma4Channels(string raw)
    {
        // Remove all <|channel>thought ... <channel|> blocks.
        var sb = new System.Text.StringBuilder();
        int pos = 0;
        while (true)
        {
            int open = raw.IndexOf(ChannelOpen, pos, StringComparison.Ordinal);
            if (open < 0)
            {
                sb.Append(raw[pos..]);
                break;
            }
            int close = raw.IndexOf(ChannelClose, open + ChannelOpen.Length, StringComparison.Ordinal);
            if (close < 0)
            {
                // Unmatched opener — skip past it, keep the rest.
                pos = open + ChannelOpen.Length;
                continue;
            }
            sb.Append(raw[pos..open]);
            pos = close + ChannelClose.Length;
        }
        return sb.ToString().Trim();
    }
}
