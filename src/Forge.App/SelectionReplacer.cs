using System.Windows.Automation;
using Forge.Core;

namespace Forge.App;

/// <summary>
/// Owns the entire replace cascade for the direct translate path (S16 isolation):
/// EM_REPLACESEL → UIA splice → clipboard fallback, in that order. Best-effort by
/// design — it never throws and always returns a usable outcome with diagnostics.
///
/// The primitives (<see cref="UiaProbe"/>, <see cref="ClipboardGuard"/>) are untouched;
/// this is the ONLY place the cascade order lives, so no other change in App can
/// alter replace behavior.
/// </summary>
public static class SelectionReplacer
{
    /// <summary>The outcome of a replace attempt plus a user-facing message.</summary>
    public sealed record ReplaceResult(ReplaceAction.Outcome Outcome, string Message, string Diagnostics = "");

    /// <summary>
    /// Replaces <paramref name="selectedText"/> with <paramref name="replacement"/> in the
    /// source app. Tries the source window's native edit control first (focus-preserving),
    /// then a UIA splice (works for Chromium even after the visual selection is gone),
    /// then stages the result on the clipboard for a manual paste.
    /// </summary>
    public static ReplaceResult Replace(IntPtr sourceWindow, AutomationElement? sourceElement, string selectedText, string replacement)
    {
        // 1) EM_REPLACESEL: in-place, focus-preserving.
        bool em = UiaProbe.ReplaceSelectionText(sourceWindow, replacement);
        if (em)
            return new ReplaceResult(ReplaceAction.Outcome.Replaced, "Replaced via EM_REPLACESEL.");

        // 2) UIA splice: replace only the selection within the full text. Works even when
        //    the visual selection is gone (inference takes seconds).
        bool uia = UiaProbe.ReplaceSelectionViaUia(sourceElement, selectedText, replacement);
        if (uia)
            return new ReplaceResult(ReplaceAction.Outcome.Replaced, "Replaced via UIA splice.");

        // 3) Clipboard fallback: the result is staged for a manual paste.
        bool clip = ClipboardGuard.WriteText(replacement);
        return clip
            ? new ReplaceResult(ReplaceAction.Outcome.ClipboardFallback,
                "The result is on the clipboard. Press Ctrl+V to paste it manually.")
            : new ReplaceResult(ReplaceAction.Outcome.ClipboardFallback,
                "The result could not be applied and the clipboard write failed.");
    }
}
