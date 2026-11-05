using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forge.App;

// Probe: construct SettingsWindow headlessly, then inspect the themed ComboBox:
// open its dropdown with many items and read back the RENDERED foreground of item
// text plus the ScrollViewer's scrollable extent. Verifies both reported bugs
// (item text invisible; no scrolling) with real values, not guesses.
internal static class Program
{
    [STAThread]
    static void Main()
    {
        var logPath = Path.Combine(Path.GetTempPath(), "settings-probe.log");
        File.WriteAllText(logPath, "");

        void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
            Console.WriteLine(line);
            File.AppendAllText(logPath, line + Environment.NewLine);
        }

        Log("probe started");

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var tcs = new TaskCompletionSource<Exception?>();

        static T? FindVisual<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t) return t;
                var found = FindVisual<T>(child);
                if (found is not null) return found;
            }
            return null;
        }

        static IEnumerable<TextBlock> AllTextBlocks(DependencyObject root)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is TextBlock tb) yield return tb;
                foreach (var nested in AllTextBlocks(child)) yield return nested;
            }
        }

        static string BrushName(Brush? b) =>
            b is SolidColorBrush sb ? $"#{sb.Color.R:X2}{sb.Color.G:X2}{sb.Color.B:X2}" : b?.ToString() ?? "null";

        // Awaits on the dispatcher thread pump WPF frames, so layout/render run between steps.
        app.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                Log("constructing SettingsWindow");
                var settings = new SettingsWindow();
                settings.Show();
                await Task.Delay(800);

                // --- Sidebar layout check (Handy pattern) ---
                var sidebar = (settings.Content as Grid)?.Children[0] as Border;
                if (sidebar is null) throw new Exception("sidebar not found");
                var navTexts = AllTextBlocks(sidebar).Where(t => t.Text is "Model" or "Engine" or "Hardware" or "Prompts" or "Hotkeys").ToList();
                Log($"nav items found: {navTexts.Count}");
                if (navTexts.Count != 5) throw new Exception("expected 5 sidebar nav items");

                // The window's ScrollViewer is a ContentControl subclass, so this finds it;
                // its .Content is our section host.
                var winScroll = FindVisual<ScrollViewer>(settings);
                if (winScroll is null) throw new Exception("window ScrollViewer not found");
                if ((winScroll.Content as ContentControl)?.Content is null)
                    throw new Exception("content host should show the Model section on load");

                // --- Window-level scroll check: shrink the window until content overflows ---
                settings.Height = 180;
                await Task.Delay(500);
                Log($"window scroll: viewport={winScroll.ViewportHeight:F1} extent={winScroll.ExtentHeight:F1} scrollable={winScroll.ScrollableHeight:F1}");
                if (winScroll.ScrollableHeight <= 0) throw new Exception("content should overflow at height 180");
                var offsetBefore = winScroll.VerticalOffset;
                winScroll.ScrollToVerticalOffset(winScroll.ExtentHeight);
                await Task.Delay(300);
                Log($"window scroll test: offset before={offsetBefore:F1} after={winScroll.VerticalOffset:F1}");
                if (winScroll.VerticalOffset <= 0) throw new Exception("scroll offset did not move");

                var combo = FindVisual<ThemedComboBox>(settings);
                if (combo is null) throw new Exception("ThemedComboBox not found in visual tree");
                Log($"found ThemedComboBox, items: {combo.Items.Count}");

                // Closed-state diagnostics: which template is applied and what does the field render?
                Log($"combo.Template = {combo.Template?.GetType()}");
                Log($"combo.Background = {BrushName(combo.Background)} combo.BorderBrush = {BrushName(combo.BorderBrush)}");
                var closedBorder = FindVisual<Border>(combo);
                // Walk every border in the closed field to find the one that paints its background.
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(combo); i++)
                    LogBorders(VisualTreeHelper.GetChild(combo, i), 0);
                void LogBorders(DependencyObject node, int depth)
                {
                    if (node is Border b)
                        Log(new string(' ', depth * 2) + $"border: bg={BrushName(b.Background)} w={b.ActualWidth:F0} h={b.ActualHeight:F0}");
                    for (var j = 0; j < VisualTreeHelper.GetChildrenCount(node); j++)
                        LogBorders(VisualTreeHelper.GetChild(node, j), depth + 1);
                }

                // (Pixel sampling removed: RenderTargetBitmap is unreliable in
                // headless probe; border background logs above are sufficient.)

                for (var i = 1; i <= 25; i++)
                    combo.Items.Add($"model-{i:00}-Q8_0.gguf (very long filename to test wrapping)");
                await Task.Delay(300);

                Log("opening dropdown");
                // Open via the toggle (default template: Click/IsChecked drives
                // IsDropDownOpen through the template trigger).
                var toggle = FindVisual<System.Windows.Controls.Primitives.ToggleButton>(combo);
                if (toggle is null) throw new Exception("PART_Toggle not found");
                toggle.IsChecked = true;
                await Task.Delay(1200);

                var popup = FindVisual<Popup>(combo);
                if (popup is null) throw new Exception("PART_Popup not found");
                Log($"IsDropDownOpen={combo.IsDropDownOpen} popup.IsOpen={popup.IsOpen} child={(popup.Child != null)}");
                if (popup.Child is null) throw new Exception("popup content not attached");
                Log($"popup open, child type: {popup.Child.GetType().Name}");

                // Keep the dropdown open long enough for an external screenshot.
                await Task.Delay(300);

                DumpTree(popup.Child, 0);

                // Render the open dropdown to a PNG so it can be inspected visually.
                var childFe = (FrameworkElement)popup.Child;
                // Popup content lives in its own HwndSource; render from that source's root
                // so the capture is valid (rendering across the boundary yields a blank image).
                var hws = PresentationSource.FromVisual(childFe) as HwndSource;
                var renderTarget = hws?.RootVisual ?? childFe;
                var rtb = new RenderTargetBitmap(
                    (int)childFe.ActualWidth, (int)childFe.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(renderTarget);
                var pngPath = Path.Combine(Path.GetTempPath(), "forge-dropdown.png");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = File.Create(pngPath))
                    encoder.Save(fs);
                Log($"saved dropdown render: {pngPath}");

                void DumpTree(DependencyObject node, int depth)
                {
                    var fe = node as FrameworkElement;
                    Log(new string(' ', depth * 2) + $"{node.GetType().Name} w={fe?.ActualWidth:F0} h={fe?.ActualHeight:F0}");
                    for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                        DumpTree(VisualTreeHelper.GetChild(node, i), depth + 1);
                }

                var scroll = FindVisual<ScrollViewer>(popup.Child);
                if (scroll is null) throw new Exception("ScrollViewer not found in popup");
                Log($"scrollviewer: actualHeight={scroll.ActualHeight:F1} scrollableExtent={scroll.ScrollableHeight:F1} canContentScroll={scroll.CanContentScroll} verticalOffset={scroll.VerticalOffset}");

                // Functional scroll test: request a large offset and verify the viewport moved.
                var before = scroll.VerticalOffset;
                scroll.ScrollToVerticalOffset(scroll.ExtentHeight);
                await Task.Delay(300);
                Log($"scroll test: offset before={before:F1} after={scroll.VerticalOffset:F1} extent={scroll.ExtentHeight:F1} viewport={scroll.ViewportHeight:F1}");

                var texts = AllTextBlocks(popup.Child).ToList();
                Log($"textblocks in popup: {texts.Count}");
                foreach (var t in texts.Take(3))
                    Log($"  item text '{t.Text[..Math.Min(24, t.Text.Length)]}' foreground={BrushName(TextElement.GetForeground(t))}");

                // Closed-state selected value text (ContentSite) — the popup's child is in a
                // separate visual tree, so walking the combo only reaches closed-state text.
                foreach (var t in AllTextBlocks(combo).Take(3))
                    Log($"  closed-field text '{t.Text[..Math.Min(24, t.Text.Length)]}' foreground={BrushName(TextElement.GetForeground(t))}");

                // --- Auto-save on change: raise LostFocus on a field and verify the file updates ---
                string cfg = Forge.Core.ForgeSettings.DefaultConfigPath;
                var ctxField = typeof(SettingsWindow).GetField("_contextBox",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var ctxBox = (TextBox)ctxField!.GetValue(settings)!;
                string originalContext = ctxBox.Text;
                ctxBox.Text = "4096";
                ctxBox.RaiseEvent(new RoutedEventArgs(TextBox.LostFocusEvent));
                await Task.Delay(300);
                var cfgChanged = File.ReadAllText(cfg);
                Log($"change-save: context updated={cfgChanged.Contains("\"ContextSize\": 4096")}");
                if (!cfgChanged.Contains("\"ContextSize\": 4096")) throw new Exception("LostFocus should auto-save the context size");

                // Restore the original value so the probe leaves the user's settings untouched.
                ctxBox.Text = originalContext;
                ctxBox.RaiseEvent(new RoutedEventArgs(TextBox.LostFocusEvent));
                await Task.Delay(300);

                // --- Auto-save on close: closing must persist the settings file ---
                var cfgBefore = File.Exists(cfg) ? File.ReadAllText(cfg) : "";
                settings.Close();
                await Task.Delay(300);
                var cfgAfter = File.Exists(cfg) ? File.ReadAllText(cfg) : "";
                Log($"close-save: persisted={cfgAfter.Length > 0} values-unchanged={cfgBefore == cfgAfter}");
                if (cfgAfter.Length == 0) throw new Exception("closing the window should persist settings");

                Log("probe succeeded");
                tcs.SetResult(null);
            }
            catch (Exception ex)
            {
                Log($"PROBE FAILED: {ex}");
                tcs.SetResult(ex);
            }
            finally
            {
                app.Shutdown();
            }
        });

        app.Run();
        var failure = tcs.Task.GetAwaiter().GetResult();
        Environment.Exit(failure is null ? 0 : 1);
    }
}
