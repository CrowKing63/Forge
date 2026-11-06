using System.Windows;
using System.Windows.Automation;

namespace Forge.App;

/// <summary>
/// Owns the paste-hint lifecycle for the clipboard fallback: shows a quiet, non-activating
/// card over the source selection telling the user to press Ctrl+V. Same isolation pattern
/// as <see cref="HammerIndicator"/> — rectangle resolution, the screen-center fallback, and
/// all error handling live here so App stays simple. Never throws.
/// </summary>
public static class PasteHint
{
    /// <summary>
    /// Shows the hint over <paramref name="sourceElement"/>'s screen rectangle. Falls back
    /// to the screen center when the rectangle can't be resolved. The window closes itself
    /// after its duration — no explicit close is needed from the caller.
    /// </summary>
    public static void Show(AutomationElement? sourceElement)
    {
        Rect? rect = null;
        try
        {
            if (sourceElement is not null)
            {
                DebugLog.Write("paste hint: reading bounding rectangle");
                rect = sourceElement.Current.BoundingRectangle;
                DebugLog.Write($"paste hint: bounding rectangle {rect}");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"paste hint: bounding rectangle unavailable — {ex.Message}");
        }

        if (rect is null || rect.Value.Width <= 0 || rect.Value.Height <= 0)
        {
            // No usable selection rectangle — fall back to the screen center so the
            // notice still shows even when UIA can't resolve the element's geometry.
            rect = HammerOverlayWindow.ScreenCenter();
            DebugLog.Write("paste hint: no usable bounding rectangle — using screen center");
        }

        try
        {
            var window = new PasteHintWindow(rect.Value);
            window.Show(); // ShowActivated=false: no focus change
            DebugLog.Write($"paste hint shown over {rect.Value}");
        }
        catch (Exception ex)
        {
            // The hint is a nicety: any failure here must never break the translation.
            DebugLog.Write($"paste hint: failed to show — {ex}");
        }
    }
}
