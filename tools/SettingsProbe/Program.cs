using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forge.App;

// Probe: construct SettingsWindow headlessly, then inspect the model tab: the dropdown
// is gone (S18), models are clickable cards, and the download section lists catalog
// models not yet on disk. Verifies card presence, selection highlight, window scroll,
// hotkey capture logic, and auto-save with real values, not guesses.
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

        static IEnumerable<Button> AllButtons(DependencyObject root)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is Button b2) yield return b2;
                foreach (var nested in AllButtons(child)) yield return nested;
            }
        }

        static IEnumerable<System.Windows.Shapes.Path> AllPaths(DependencyObject root)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is System.Windows.Shapes.Path p) yield return p;
                foreach (var nested in AllPaths(child)) yield return nested;
            }
        }

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
                var navTexts = AllTextBlocks(sidebar).Where(t => t.Text is "Model" or "Engine" or "Hardware" or "Hotkeys" or "General").ToList();
                Log($"nav items found: {navTexts.Count}");
                if (navTexts.Count != 5) throw new Exception("expected 5 sidebar nav items");

                // --- General section (S20): auto-start checkbox + onboarding re-run button ---
                typeof(SettingsWindow).GetMethod("SelectSection",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(settings, new object[] { 4 });
                await Task.Delay(300);
                var generalCheck = FindVisual<CheckBox>(settings);
                if (generalCheck is null) throw new Exception("auto-start checkbox not found in General");
                var onboardingButton = AllButtons(settings).FirstOrDefault(b => (b.Content as string) == "Show onboarding again");
                if (onboardingButton is null) throw new Exception("'Show onboarding again' button not found in General");
                Log("general section OK");
                typeof(SettingsWindow).GetMethod("SelectSection",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(settings, new object[] { 0 });

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

                // --- Model cards (S18): the dropdown is gone; models are clickable cards ---
                if (FindVisual<ComboBox>(settings) is not null)
                    throw new Exception("no combobox should remain in settings — models are cards now");

                var modelList = typeof(SettingsWindow).GetField("_modelList",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(settings) as StackPanel;
                if (modelList is null) throw new Exception("_modelList not found");
                var downloadList = typeof(SettingsWindow).GetField("_downloadList",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(settings) as StackPanel;
                if (downloadList is null) throw new Exception("_downloadList not found");

                int installedRows = modelList.Children.Count;
                int downloadButtons = 0;
                foreach (var child in downloadList.Children)
                    if (child is Border b)
                        downloadButtons += AllButtons(b).Where(x => x.Content as string == "Download").Count();
                Log($"model cards: installed={installedRows} download buttons={downloadButtons}");
                if (installedRows + downloadButtons < 1)
                    throw new Exception("at least one model must be visible (installed card or download button)");

                // The selected path must match a row, and that row must carry the accent border.
                string selectedPath = (string)typeof(SettingsWindow).GetField("_selectedModelPath",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(settings)!;
                var rows = (System.Collections.IList)typeof(SettingsWindow).GetField("_modelRows",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(settings)!;
                Border? selectedRow = null;
                foreach (var item in rows)
                {
                    var (row, path) = (System.ValueTuple<Border, string>)item;
                    if (string.Equals(path, selectedPath, StringComparison.OrdinalIgnoreCase)) selectedRow = row;
                }
                if (selectedRow is null) throw new Exception("the selected model path has no card");
                Log($"selected model: {selectedPath}");
                if (!ReferenceEquals(selectedRow.BorderBrush, ForgeTheme.AccentBrush))
                    throw new Exception("the selected card should carry the accent border");
                Log("selected card highlight OK");

                // --- Model title folder icon: a clickable folder glyph next to the "Model" label ---
                var icon = AllPaths(settings).FirstOrDefault(p =>
                    (VisualTreeHelper.GetParent(p) as Border)?.ToolTip as string == "Open the models folder");
                if (icon is null) throw new Exception("folder icon button not found in the Model section title");
                Log("model folder icon OK");

                // --- Download card construction (S18): build one for the catalog default and
                // assert its shape (Download + Cancel buttons, name text) without touching disk.
                var makeDownloadRow = typeof(SettingsWindow).GetMethod("MakeDownloadRow",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var downloadCard = (Border)makeDownloadRow.Invoke(settings, new object[] { Forge.Core.ModelManager.DefaultModel })!;
                int dButtons = AllButtons(downloadCard).Count();
                if (dButtons != 2) throw new Exception("a download card should have Download + Cancel buttons");
                var nameText = ((StackPanel)((Grid)((StackPanel)downloadCard.Child).Children[0]).Children[0]).Children[0] as TextBlock;
                Log($"download card: buttons={dButtons} name='{nameText?.Text}'");

                // --- Hotkey capture field checks ---
                var popupField = (HotkeyField)typeof(SettingsWindow).GetField("_translateBox",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(settings)!;
                string originalPopup = popupField.Text;
                Log($"hotkey field: popup.Text='{popupField.Text}'");

                // FormatBinding must round-trip through the parser.
                var formatted = HotkeyField.FormatBinding(Forge.Core.HotkeyBinding.ModControl | Forge.Core.HotkeyBinding.ModShift, 0x45);
                Log($"format: {formatted}");
                if (formatted != "Ctrl+Shift+E") throw new Exception("FormatBinding should yield Ctrl+Shift+E");
                if (!Forge.Core.HotkeyBinding.TryParse(formatted, out var parsed)) throw new Exception("formatted binding should parse");
                if (parsed.Modifiers != (Forge.Core.HotkeyBinding.ModControl | Forge.Core.HotkeyBinding.ModShift) || parsed.Key != 0x45)
                    throw new Exception("parsed binding should match the captured mods+key");

                // The popup field's loaded text (if any) must be a valid binding.
                if (popupField.Text.Length > 0 && !Forge.Core.HotkeyBinding.TryParse(popupField.Text, out _))
                    throw new Exception("loaded popup hotkey should be a valid binding");

                // --- End-to-end capture: start capture, inject an AltKey-style SendInput combo,
                // and verify the low-level hook records it. This is exactly the path that failed
                // before (WPF PreviewKeyDown + GetAsyncKeyState saw no modifiers on a fast burst).
                // Drive the capture logic directly. The modifier-reading path (GetAsyncKeyState) can't
                // be exercised in this headless agent context — no physical keys are held — so we
                // verify the paths that don't depend on live modifier state: a bare key keeps
                // recording without changing the value, and Esc clears + ends capture.
                var begin = typeof(HotkeyField).GetMethod("BeginCapture",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var handle = typeof(HotkeyField).GetMethod("HandleCaptureKey",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var capturingField = typeof(HotkeyField).GetField("_capturing",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

                begin.Invoke(popupField, null);
                if (!(bool)capturingField.GetValue(popupField)!) throw new Exception("BeginCapture should set _capturing");

                // A bare key (no modifiers held here) must NOT capture — it stays recording.
                handle.Invoke(popupField, new object[] { 0x45, false });
                if (!(bool)capturingField.GetValue(popupField)!) throw new Exception("a bare key should keep recording");
                if (popupField.Text != originalPopup) throw new Exception("a bare key should not change the value");

                // Esc clears the binding and ends capture.
                handle.Invoke(popupField, new object[] { 0x1B, false });
                if ((bool)capturingField.GetValue(popupField)!) throw new Exception("Esc should end capture");
                if (popupField.Text != "") throw new Exception("Esc should clear the value");
                Log("capture logic: bare-key + esc paths OK");

                // Restore the field so the probe leaves the user's settings untouched. The setter
                // does not raise ValueChanged, so no save fires here; the change-save section below
                // persists the restored value along with everything else.
                popupField.Text = originalPopup;

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
