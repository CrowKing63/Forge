namespace Forge.Core;

/// <summary>
/// The model family a loaded GGUF belongs to. Determines the prompt format
/// (<see cref="PromptBuilder"/>) and output post-processing
/// (<see cref="OutputPostProcessor"/>).
/// </summary>
public enum ModelFamily
{
    /// <summary>Gemma 4 — the default; chat template with &lt;|turn&gt; role markers.</summary>
    Gemma4,

    /// <summary>Tencent Hy-MT2 — translation-specialized; &lt;|hy_...&gt; marker format (S15).</summary>
    HyMt2,
}

/// <summary>
/// Detects which prompt/output conventions a model file follows. The catalog is small,
/// so the file name is a reliable discriminator — no settings field needed.
/// </summary>
public static class ModelProfile
{
    /// <summary>Maps a model file path to its <see cref="ModelFamily"/> by file name.</summary>
    public static ModelFamily Detect(string modelPath) =>
        Path.GetFileName(modelPath).Contains("Hy-MT2", StringComparison.OrdinalIgnoreCase)
            ? ModelFamily.HyMt2
            : ModelFamily.Gemma4;

    /// <summary>
    /// Caps the output budget for a family (S15: Hy-MT2 can run away when its stop token
    /// is not emitted; translations are short, so a hard cap is safe). Other families
    /// keep the full context-derived budget.
    /// </summary>
    public static int MaxOutputTokens(ModelFamily family, int contextBudget) => family switch
    {
        ModelFamily.HyMt2 => Math.Min(contextBudget, 512),
        _ => contextBudget,
    };
}
