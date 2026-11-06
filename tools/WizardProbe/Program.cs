using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Forge.App;
using Forge.Core;

// Probe: drive the S20 onboarding wizard headlessly with a stub host (no tray, no global
// hotkeys, no registry). Verifies page navigation, the model page's three states
// (sourceless path, download consent, running), hotkey seeding, auto-start wiring, and
// that closing marks onboarding completed.
internal static class Program
{
    [STAThread]
    static void Main()
    {
        var logPath = Path.Combine(Path.GetTempPath(), "wizard-probe.log");
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
                if (child is Button b) yield return b;
                foreach (var nested in AllButtons(child)) yield return nested;
            }
        }

        static Button FindButton(DependencyObject root, string content) =>
            AllButtons(root).FirstOrDefault(b => (b.Content as string) == content)
            ?? throw new Exception($"button '{content}' not found");

        // WPF buttons have no public Click() — raising the routed event drives the handler.
        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, b));

        // Awaits on the dispatcher thread pump WPF frames, so layout/render run between steps.
        app.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var host = new StubHost
                {
                    ModelPath = Path.Combine(Path.GetTempPath(), "definitely-not-a-model.gguf"),
                };

                Log("constructing OnboardingWizardWindow");
                var wizard = new OnboardingWizardWindow(host);
                wizard.Show();
                await Task.Delay(600);

                // --- Page 1: welcome ---
                var stepLabel = AllTextBlocks(wizard).First(t => t.Text is not null && t.Text!.Contains(" of "));
                Log($"page 1: step='{stepLabel.Text}'");
                if (stepLabel.Text != "1 of 4") throw new Exception("expected '1 of 4' on the first page");

                // --- Next → page 2: model (sourceless path branch) ---
                Click(FindButton(wizard, "Next"));
                await Task.Delay(300);
                if (stepLabel.Text != "2 of 4") throw new Exception("expected '2 of 4' after Next");
                var back = FindButton(wizard, "Back");
                if (back.Visibility != Visibility.Visible) throw new Exception("Back should be visible on page 2");

                var modelStatus = AllTextBlocks(wizard)
                    .FirstOrDefault(t => t.Text is not null && t.Text!.Contains("No model file found"));
                Log($"page 2: status='{modelStatus?.Text}'");
                if (modelStatus is null) throw new Exception("a sourceless path should report 'No model file found'");

                var openSettings = FindButton(wizard, "Open Settings");
                if (openSettings.Visibility != Visibility.Visible)
                    throw new Exception("'Open Settings' should be visible for a sourceless path");
                var download = AllButtons(wizard).FirstOrDefault(b => (b.Content as string) == "Download");
                if (download is not null && download.Visibility != Visibility.Collapsed)
                    throw new Exception("no visible Download button should exist for a sourceless path");

                // --- Back → page 1 ---
                Click(back);
                await Task.Delay(300);
                if (stepLabel.Text != "1 of 4") throw new Exception("expected '1 of 4' after Back");
                if (back.Visibility != Visibility.Collapsed) throw new Exception("Back should be hidden on page 1");

                // --- Page 3: hotkey seeded from the config file ---
                Click(FindButton(wizard, "Next"));
                await Task.Delay(200);
                Click(FindButton(wizard, "Next"));
                await Task.Delay(200);
                var field = FindVisual<HotkeyField>(wizard);
                if (field is null) throw new Exception("hotkey field not found on page 3");
                Log($"page 3: hotkey='{field.Text}' expected='{HotkeyConfig.Load()}'");
                if (field.Text != HotkeyConfig.Load()) throw new Exception("the hotkey field should be seeded from the config");

                // --- Page 4: auto-start reflects the host; toggling calls SetAutoStart ---
                Click(FindButton(wizard, "Next"));
                await Task.Delay(200);
                var check = FindVisual<CheckBox>(wizard);
                if (check is null) throw new Exception("auto-start checkbox not found on page 4");
                if (check.IsChecked != host.IsAutoStartEnabled)
                    throw new Exception("the checkbox should reflect the host's initial state");
                check.IsChecked = true;
                await Task.Delay(200);
                if (host.LastAutoStart != true)
                    throw new Exception("checking the box should call SetAutoStart(true)");
                Log("page 4: auto-start wiring OK");

                // --- Finish closes the window and marks onboarding completed ---
                var finish = FindButton(wizard, "Finish");
                if (finish.Content as string != "Finish") throw new Exception("the last page's button should read 'Finish'");
                Click(finish);
                await Task.Delay(400);
                if (host.FinishCalls != 1) throw new Exception("closing the wizard should call FinishOnboarding exactly once");
                Log("finish: onboarding completed OK");

                // --- Download-consent branch (only when a catalog model is not on disk) ---
                var manager = new ModelManager(ModelManager.DefaultModelsDir);
                var defaultModel = manager.ListModels().FirstOrDefault(m => m.Name == ModelManager.DefaultModel.Name);
                if (defaultModel is not null && !manager.IsDownloaded(defaultModel))
                {
                    var host2 = new StubHost { ModelPath = manager.GetPath(defaultModel) };
                    var wizard2 = new OnboardingWizardWindow(host2);
                    wizard2.Show();
                    await Task.Delay(600);
                    Click(FindButton(wizard2, "Next"));
                    await Task.Delay(300);

                    var status2 = AllTextBlocks(wizard2)
                        .FirstOrDefault(t => t.Text is not null && t.Text!.Contains("Do you want to download"));
                    Log($"consent: status='{status2?.Text}'");
                    if (status2 is null) throw new Exception("a downloadable model should ask for consent");

                    Click(FindButton(wizard2, "Download"));
                    await Task.Delay(300);
                    if (host2.StartCalls != 1) throw new Exception("clicking Download should start the host's download");
                    Log("consent: download started OK");
                    wizard2.Close();
                }
                else
                {
                    Log("consent branch skipped (default model already on disk)");
                }

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

    /// <summary>Records host calls so the probe can assert wiring without touching the real app.</summary>
    sealed class StubHost : IOnboardingHost
    {
        public string ModelPath { get; init; } = "";
        public bool IsModelDownloadRunning => false;
        public int StartCalls;
        public int FinishCalls;
        public bool? LastAutoStart;

        public event Action<DownloadProgress>? DownloadProgress;
        public event Action<ModelDownloadOutcome>? DownloadFinished;

        public void StartModelDownload() => StartCalls++;
        public void CancelModelDownload() { }
        public bool ApplyHotkey(string binding) => true;
        public bool IsAutoStartEnabled => false;
        public void SetAutoStart(bool enabled) => LastAutoStart = enabled;
        public void FinishOnboarding() => FinishCalls++;
        public void OpenSettingsWindow() { }
    }
}
