namespace Forge.Core;

/// <summary>
/// The support tier for a loaded model. Determines how much post-processing and
/// prompt engineering the app applies.
/// </summary>
public enum ModelTier
{
    /// <summary>Certified catalog model — family, template, sampling params all known.</summary>
    Certified,

    /// <summary>Unauthenticated GGUF — best-effort detection, generic fallbacks.</summary>
    Unauthenticated,
}

/// <summary>
/// The certified model catalog (S23). A small, fixed list of models that Forge has
/// benchmarked and whose conventions (chat template, output markers, sampling) are
/// known. Any GGUF not in this list loads as <see cref="ModelTier.Unauthenticated"/>.
///
/// The catalog is intentionally tiny: each entry must be manually verified against
/// real inference output before it earns a slot.
/// </summary>
public static class ModelCatalog
{
    /// <summary>A single certified model entry.</summary>
    public sealed record CertifiedModel(
        string NamePattern,
        ModelFamily Family,
        float Temperature,
        int MaxOutputTokens)
    {
        /// <summary>All certified models.</summary>
        public static readonly CertifiedModel[] All =
        {
            new("gemma-4", ModelFamily.Gemma4, 0.7f, 512),
            new("Hy-MT2", ModelFamily.HyMt2, 0.3f, 256),
        };

        /// <summary>
        /// Matches a model file path against the catalog. Returns the entry when the
        /// file name contains the pattern (case-insensitive), null otherwise.
        /// </summary>
        public static CertifiedModel? Match(string modelPath)
        {
            string name = Path.GetFileName(modelPath);
            foreach (var m in All)
                if (name.Contains(m.NamePattern, StringComparison.OrdinalIgnoreCase))
                    return m;
            return null;
        }

        /// <summary>True when the file is a certified catalog model.</summary>
        public static bool IsCertified(string modelPath) => Match(modelPath) is not null;
    }
}
