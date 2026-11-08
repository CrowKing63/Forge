using System.Text;

namespace Forge.Core;

/// <summary>
/// Two-tier prompt builder (S21): renders the chat prompt using the model's embedded
/// Jinja template when available (tier 2 — any GGUF), falling back to the certified
/// catalog format (<see cref="PromptBuilder"/>) when no template is present or rendering
/// fails (tier 1).
///
/// Tier 1 (certified): Gemma4, HyMt2 — hardcoded marker formats benchmark-verified in S13/S15.
/// Tier 2 (any GGUF): models with an embedded Jinja chat template — rendered by
/// <see cref="JinjaRenderer"/> using the standard llama.cpp context
/// (<c>{"messages": [...], "tools": []}</c>).
/// </summary>
public static class ChatPromptBuilder
{
    /// <summary>The system instruction for translation (shared across tiers).</summary>
    private const string SystemInstruction =
        "Translate the following text to natural English. Preserve meaning and tone. Output only the translation.";

    /// <summary>
    /// Builds the full chat prompt for translating <paramref name="selectedText"/>.
    /// Uses the embedded Jinja template when <paramref name="chatTemplate"/> is a
    /// non-empty Jinja string; otherwise falls back to <see cref="PromptBuilder"/>.
    /// </summary>
    public static string Build(string selectedText, ModelFamily family, string? chatTemplate, string modelPath)
    {
        if (selectedText is null || selectedText.Trim().Length == 0)
            throw new ArgumentException("Selected text must not be empty.", nameof(selectedText));

        // Tier 1 (certified): skip Jinja entirely — use the known-good hardcoded markers.
        // The Gemma 4 template is 18K chars of complex Jinja that our renderer can't
        // parse (stack overflow). Certified models always use tier 1.
        if (ModelCatalog.CertifiedModel.Match(modelPath) is not null)
            return PromptBuilder.Build(selectedText, family);

        // Tier 2 (unauthenticated): try the embedded Jinja template.
        if (!string.IsNullOrWhiteSpace(chatTemplate))
        {
            try
            {
                var messages = new List<object?>
                {
                    new Dictionary<string, object?> { ["role"] = "system", ["content"] = SystemInstruction },
                    new Dictionary<string, object?> { ["role"] = "user", ["content"] = selectedText },
                };
                var context = new Dictionary<string, object?>
                {
                    ["messages"] = messages,
                    ["tools"] = new List<object?>(),
                };
                return JinjaRenderer.Render(chatTemplate, context);
            }
            catch (Exception ex)
            {
                // Template uses unsupported constructs — fall through to tier 1.
                Console.Error.WriteLine($"[ChatPromptBuilder] Jinja render failed: {ex}");
            }
        }

        // Fallback: generic role markers.
        return PromptBuilder.Build(selectedText, family);
    }
}
