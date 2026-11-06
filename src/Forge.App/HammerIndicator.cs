using System.Windows;
using System.Windows.Automation;
using Forge.Core;

namespace Forge.App;

/// <summary>
/// Owns the hammer working-indicator lifecycle for the direct translate path:
/// show over the source selection, mark complete on success, close on any exit.
///
/// Isolation (S16): this facade is the ONLY entry point to the indicator. It owns
/// rectangle resolution, the screen-center fallback, and all error handling, so
/// no other change in App can affect the indicator's behavior. The window itself
/// (<see cref="HammerOverlayWindow"/>) is untouched — HammerProbe verifies it directly.
/// </summary>
public sealed class HammerIndicator
{
    private readonly HammerOverlayWindow? _window;

    private HammerIndicator(HammerOverlayWindow? window) => _window = window;

    /// <summary>
    /// Shows the indicator over <paramref name="sourceElement"/>'s screen rectangle.
    /// Falls back to the screen center when the rectangle can't be resolved. Never
    /// throws: a failed show yields an inert handle so callers stay simple.
    /// </summary>
    public static HammerIndicator Show(AutomationElement? sourceElement)
    {
        Rect? rect = null;
        try
        {
            if (sourceElement is not null)
            {
                DebugLog.Write("hammer indicator: reading bounding rectangle");
                rect = sourceElement.Current.BoundingRectangle;
                DebugLog.Write($"hammer indicator: bounding rectangle {rect}");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"hammer indicator: bounding rectangle unavailable — {ex.Message}");
        }

        if (rect is null || rect.Value.Width <= 0 || rect.Value.Height <= 0)
        {
            // No usable selection rectangle — fall back to the screen center so the
            // feedback still shows even when UIA can't resolve the element's geometry.
            rect = HammerOverlayWindow.ScreenCenter();
            DebugLog.Write("hammer indicator: no usable bounding rectangle — using screen center");
        }

        try
        {
            var window = new HammerOverlayWindow(rect.Value);
            DebugLog.Write("hammer indicator: constructed, showing");
            window.Show(); // ShowActivated=false: no focus change
            DebugLog.Write($"hammer indicator shown over {rect.Value}");
            return new HammerIndicator(window);
        }
        catch (Exception ex)
        {
            // The indicator is a nicety: any failure here must never break the translation.
            DebugLog.Write($"hammer indicator: failed to show — {ex}");
            return new HammerIndicator(null);
        }
    }

    /// <summary>Plays the final strike and hides the window after a short delay (synced with the clang).</summary>
    public void Complete() => _window?.Complete();

    /// <summary>
    /// Hides and releases the window immediately. Skipped while the final strike is
    /// playing out (<see cref="HammerOverlayWindow.IsFinishing"/>) so it finishes cleanly.
    /// </summary>
    public void Close()
    {
        var window = _window;
        if (window is null || window.IsFinishing)
            return;
        window.Close();
    }
}
