namespace Forge.Core;

/// <summary>
/// Builds the prompt for the app's single task: translating a selected Korean
/// text into English. The instruction per family is kept verbatim — both were
/// benchmark-verified on real models (S13 tuning for Gemma 4, S15 README
/// instruction for Hy-MT2) and MT models are sensitive to wording changes.
/// </summary>
public static class PromptBuilder
{
    /// <summary>Gemma 4 translation instruction (S13-tuned).</summary>
    private const string GemmaInstruction =
        "Translate the following Korean text to natural English. Preserve meaning and tone. Output only the translation.";

    /// <summary>Hy-MT2 README instruction, verbatim (S15-verified).</summary>
    private const string HyMt2Instruction =
        "Translate the following segment into English, without additional explanation:";

    /// <summary>
    /// Builds the full prompt for translating <paramref name="selectedText"/> with
    /// the conventions of <paramref name="family"/>.
    /// </summary>
    public static string Build(string selectedText, ModelFamily family)
    {
        if (selectedText is null || selectedText.Trim().Length == 0)
            throw new ArgumentException("Selected text must not be empty.", nameof(selectedText));

        return family switch
        {
            ModelFamily.HyMt2 =>
                "<|hy_begin_of_sentence|><|hy_User|>" + HyMt2Instruction + " " + selectedText + "<|hy_Assistant|>",
            _ =>
                "<|turn>system\n" + GemmaInstruction + "\n<turn|>\n" +
                "<|turn>user\n" + selectedText + "\n<turn|>\n" +
                "<|turn>model\n",
        };
    }
}
