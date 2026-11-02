namespace Forge.Core;

/// <summary>
/// The four text-processing tasks Forge offers (plan §5). Each maps to a fixed system
/// prompt in <see cref="PromptBuilder"/>.
/// </summary>
public enum ForgeTask
{
    /// <summary>교정 — fix grammar, spelling, punctuation; preserve meaning, tone, language.</summary>
    Proofread,
    /// <summary>변역 KR→EN — translate Korean to natural English.</summary>
    TranslateKrToEn,
    /// <summary>재작성 — rewrite for clarity and conciseness; same language and tone.</summary>
    Rewrite,
    /// <summary>요약 — summarize in 2-3 sentences; same language.</summary>
    Summarize,
}

/// <summary>
/// Assembles the complete Gemma 4 prompt (role markers included) for a task.
///
/// Pure and deterministic: no I/O, same input → same output. LLamaSharp cannot run Gemma 4's
/// chat template (S0), so the full prompt — not just a (system, user) pair — is built here
/// with the S0-verified Gemma 4 role-marker format:
/// <c>&lt;|turn&gt;system\n{system}\n&lt;turn|&gt;\n&lt;|turn&gt;user\n{user}\n&lt;turn|&gt;\n&lt;|turn&gt;model\n</c>
///
/// Temperature/sampling are owned by the engine (S1); prompt-quality tuning is S13.
/// </summary>
public static class PromptBuilder
{
    private static readonly Dictionary<ForgeTask, string> SystemPrompts = new()
    {
        [ForgeTask.Proofread] =
            "Fix grammar, spelling, and punctuation. Do not change meaning, tone, or language. Output only the corrected text.",
        [ForgeTask.TranslateKrToEn] =
            "Translate the following Korean text to natural English. Preserve meaning and tone. Output only the translation.",
        [ForgeTask.Rewrite] =
            "Rewrite the following text for clarity and conciseness. Keep the same language and tone. Output only the rewritten text.",
        [ForgeTask.Summarize] =
            "Summarize the following text in 2-3 sentences. Same language. Output only the summary.",
    };

    /// <summary>The system-prompt text for a task (exposed for tests and S13 tuning).</summary>
    public static string SystemPrompt(ForgeTask task) => SystemPrompts[task];

    /// <summary>
    /// Builds the complete prompt for <paramref name="task"/> with
    /// <paramref name="selectedText"/> as the user turn. Throws
    /// <see cref="ArgumentException"/> when the selected text is null or whitespace-only.
    /// </summary>
    public static string Build(ForgeTask task, string selectedText)
    {
        if (string.IsNullOrWhiteSpace(selectedText))
            throw new ArgumentException("Selected text must not be empty.", nameof(selectedText));

        return "<|turn>system\n" + SystemPrompts[task] + "\n<turn|>\n"
             + "<|turn>user\n" + selectedText + "\n<turn|>\n"
             + "<|turn>model\n";
    }
}
