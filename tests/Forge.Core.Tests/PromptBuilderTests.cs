using Forge.Core;
using Xunit;

namespace Forge.Core.Tests;

public class PromptBuilderTests
{
    // ---- Build: task → system-prompt mapping (plan §5; S13-tuned) ----

    [Theory]
    [InlineData(ForgeTask.Proofread,
        "Fix grammar, spelling, and punctuation. When a word is misspelled, correct it to the intended word — do not replace it with a different word that changes the sentence's meaning. Do not change meaning, tone, or language. Output only the corrected text.")]
    [InlineData(ForgeTask.TranslateKrToEn,
        "Translate the following Korean text to natural English. Preserve meaning and tone. Output only the translation.")]
    [InlineData(ForgeTask.Rewrite,
        "Rewrite the following text for clarity and conciseness. Keep the same language and tone. Output only the rewritten text.")]
    [InlineData(ForgeTask.Summarize,
        "Summarize the following text in 2-3 sentences. Same language and same person as the source text. Output only the summary.")]
    public void SystemPrompt_MapsEachTaskToItsPlanText(ForgeTask task, string expected)
    {
        Assert.Equal(expected, PromptBuilder.SystemPrompt(task));
    }

    // ---- Build: full prompt with Gemma 4 role markers (S0 format) ----

    [Fact]
    public void Build_AssemblesGemma4RoleMarkers()
    {
        const string text = "오늘 날씨가 좋다.";
        string prompt = PromptBuilder.Build(ForgeTask.Proofread, text);

        Assert.Equal(
            "<|turn>system\n" +
            "Fix grammar, spelling, and punctuation. When a word is misspelled, correct it to the intended word — do not replace it with a different word that changes the sentence's meaning. Do not change meaning, tone, or language. Output only the corrected text.\n" +
            "<turn|>\n" +
            "<|turn>user\n" +
            "오늘 날씨가 좋다." +
            "\n<turn|>\n" +
            "<|turn>model\n",
            prompt);
    }

    [Fact]
    public void Build_UserTurnContainsSelectedTextVerbatim()
    {
        const string text = "  hello world  ";
        string prompt = PromptBuilder.Build(ForgeTask.Summarize, text);
        Assert.Contains("<|turn>user\n" + text + "\n<turn|>", prompt);
    }

    [Fact]
    public void Build_IsDeterministic()
    {
        Assert.Equal(PromptBuilder.Build(ForgeTask.Rewrite, "abc"), PromptBuilder.Build(ForgeTask.Rewrite, "abc"));
    }

    // ---- Build: input guards ----

    [Fact]
    public void Build_EmptyText_Throws()
    {
        Assert.Throws<ArgumentException>(() => PromptBuilder.Build(ForgeTask.Proofread, ""));
        Assert.Throws<ArgumentException>(() => PromptBuilder.Build(ForgeTask.Proofread, "   "));
        Assert.Throws<ArgumentException>(() => PromptBuilder.Build(ForgeTask.Proofread, null!));
    }

    // ---- Build: Hy-MT2 family (S15) ----

    [Fact]
    public void Build_HyMt2_Translate_UsesReadmeInstructionAndMarkers()
    {
        const string text = "나중에 또 연락할게~";
        string prompt = PromptBuilder.Build(ForgeTask.TranslateKrToEn, text, ModelFamily.HyMt2);

        Assert.Equal(
            "<|hy_begin_of_sentence|><|hy_User|>" +
            "Translate the following segment into English, without additional explanation:" +
            text +
            "<|hy_Assistant|>",
            prompt);
    }

    [Fact]
    public void Build_HyMt2_OtherTasks_UsesSystemPrompt()
    {
        const string text = "오늘 날씨가 좋다.";
        string prompt = PromptBuilder.Build(ForgeTask.Proofread, text, ModelFamily.HyMt2);

        Assert.Equal(
            "<|hy_begin_of_sentence|><|hy_User|>" +
            PromptBuilder.SystemPrompt(ForgeTask.Proofread) + " " +
            text +
            "<|hy_Assistant|>",
            prompt);
    }

    [Fact]
    public void Build_Gemma4Family_IsUnchanged()
    {
        Assert.Equal(PromptBuilder.Build(ForgeTask.Rewrite, "abc"),
                     PromptBuilder.Build(ForgeTask.Rewrite, "abc", ModelFamily.Gemma4));
    }
}
