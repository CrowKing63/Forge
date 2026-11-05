using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forge.Core;
using Hardcodet.Wpf.TaskbarNotification;

namespace Forge.App;

/// <summary>
/// Model lifecycle state shown by the tray icon (S5).
/// </summary>
public enum ModelState
{
    Loading,
    Ready,
    Error,
}

/// <summary>
/// Tray-only WPF shell: no main window. On startup it loads the model in the
/// background and reflects the engine state on the tray icon (loading / ready /
/// error). Context menu: Open Settings (S7) and Exit.
///
/// S6: global hotkeys. The popup-type binding (Ctrl+E by default) runs a direct
/// translate — capture → PromptBuilder → streaming inference → ResultParser → replace
/// in the source app — with no overlay and no result window. Immediate-type bindings
/// from config still run their task through the S7 result editor. The re-entry guard
/// (plan §7) ignores hotkey presses while a task is processing.
///
/// S7: the result editor window streams tokens in during inference and renders
/// the original-vs-result diff on completion; its buttons act on the result
/// (Copy, Regenerate, Cancel) and expose Replace to S11 as an event.
/// </summary>
public partial class App : Application
{
    /// <summary>Hotkey id reserved for the popup-type binding (overlay toggle).</summary>
    private const int PopupHotkeyId = 1;

    private ForgeSettings _settings = new();
    private InferenceEngine? _engine;
    private IdleUnloadManager? _idle;
    private System.Threading.Timer? _idleTimer;
    private string _modelPath = "";
    private int _gpuLayers;
    private TaskbarIcon? _tray;
    private HotkeyManager? _hotkeys;
    private ProcessingGuard _guard = new();
    private TaskOverlayWindow? _overlay;
    private IntPtr _sourceWindow = IntPtr.Zero; // S11: the app where the selection was made
    private AutomationElement? _sourceElement; // the focused element at hotkey time (for SetFocus before paste)
    private Dictionary<int, ForgeTask> _immediateHotkeys = new();

    [STAThread]
    public static void Main()
    {
        var app = new App();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Tray-only app: no main window. WPF's default ShutdownMode (OnLastWindowClose)
        // ends the process when the last open window closes — e.g. closing Settings
        // quits Forge. Only the tray Exit menu and the crash handler should end it.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // S12: global exception handling — a fatal .NET exception ends the app (plan §7)
        // with an opt-in crash dump (no text content) and user guidance.
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;

        _settings = ForgeSettings.Load();
        var (modelPath, showOverlay, debugLogPath) = ParseArgs(e.Args, _settings.ModelPath);
        DebugLog.SetPath(debugLogPath);
        _modelPath = modelPath;

        // S8: detect hardware before any native load (LLamaSharp locks its runtime selection
        // on the first load) and resolve the GPU layer count from the settings choice.
        var hardware = HardwareDetector.Detect();
        DebugLog.Write($"hardware: {hardware.Note}");
        _gpuLayers = ResolveGpuLayerCount(_settings.Hardware, hardware, modelPath);
        DebugLog.Write($"gpu layer count: {_gpuLayers} (mode: {_settings.Hardware})");

        _engine = new InferenceEngine(_settings.ContextSize, _gpuLayers);
        BuildTray();
        SetState(ModelState.Loading, "Forge: loading model…");

        SetupHotkeys(showOverlay);
        StartIdleTimer();

        if (e.Args.Contains("--demo"))
            _ = RunDemoAsync(); // S7 debug hook: canned result, no model needed
        if (e.Args.Contains("--smoke-crash"))
            _ = SmokeCrashAsync(); // S12 smoke hook: exercises the global handler + crash dump

        _ = LoadModelAsync(modelPath);
    }

    /// <summary>
    /// S12 smoke-test hook: after startup settles, raises an unhandled dispatcher
    /// exception to exercise the global handler end-to-end (crash dump + guidance +
    /// shutdown). Forces the opt-in on so the dump path runs regardless of settings.
    /// </summary>
    private async Task SmokeCrashAsync()
    {
        await Task.Delay(1500); // let startup finish so tray, hotkeys, and the engine exist
        _settings.CrashDumpEnabled = true;
        Dispatcher.BeginInvoke(() => throw new InvalidOperationException("smoke crash (S12 verification)"));
    }

    /// <summary>Maps the settings' hardware choice to a GPU layer count via detection.</summary>
    private static int ResolveGpuLayerCount(HardwareMode mode, HardwareInfo hardware, string modelPath)
    {
        if (mode == HardwareMode.Cpu)
            return 0;

        long modelSize = 0;
        try { if (File.Exists(modelPath)) modelSize = new FileInfo(modelPath).Length; }
        catch (Exception) { /* unreadable — treat as unknown size */ }

        return HardwareDetector.RecommendGpuLayerCount(hardware, modelSize);
    }

    /// <summary>
    /// S8 idle unload: a 1-second tick advances the state machine; when the timeout elapses
    /// with a loaded model, it is unloaded and the tray reflects the idle state.
    /// </summary>
    private void StartIdleTimer()
    {
        _idle = new IdleUnloadManager(_settings.IdleUnloadSeconds, () => _engine!.IsLoaded, () => _engine!.Unload());
        _idleTimer = new System.Threading.Timer(_ => OnIdleTick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void OnIdleTick()
    {
        if (_idle?.Tick() == true)
        {
            DebugLog.Write($"model idle-unloaded after {_settings.IdleUnloadSeconds}s");
            Dispatcher.Invoke(() => SetState(ModelState.Ready, "Forge: idle — model unloaded"));
        }
    }

    private void SetupHotkeys(bool showOverlayOnStart)
    {
        var (popupBinding, immediateBindings) = HotkeyConfig.Load();

        _hotkeys = new HotkeyManager();
        _hotkeys.Initialize();
        _hotkeys.HotkeyPressed += OnHotkeyPressed;

        if (!HotkeyBinding.TryParse(popupBinding, out var popup))
            popup = default(HotkeyBinding); // unreachable: the default always parses
        if (_hotkeys.Register(PopupHotkeyId, popup.Modifiers, popup.Key))
            DebugLog.Write($"registered popup hotkey '{popupBinding}'");
        else
            DebugLog.Write($"popup hotkey '{popupBinding}' already in use — overlay unavailable via hotkey");

        foreach (var entry in immediateBindings)
        {
            var id = PopupHotkeyId + _immediateHotkeys.Count + 1;
            if (_hotkeys.Register(id, entry.Binding.Modifiers, entry.Binding.Key))
            {
                _immediateHotkeys[id] = entry.Task;
                DebugLog.Write($"registered immediate hotkey for {entry.Task}");
            }
            else
                DebugLog.Write($"immediate hotkey for {entry.Task} already in use — skipped");
        }

        if (showOverlayOnStart)
            ShowOverlay();
    }

    private void OnHotkeyPressed(int id)
    {
        // Re-entry guard (plan §7): ignore hotkeys while a task is processing.
        if (_guard.IsProcessing)
        {
            DebugLog.Write("hotkey ignored (task in progress)");
            return;
        }

        if (id == PopupHotkeyId)
        {
            if (!_guard.TryBegin())
                return; // a concurrent press already owns the guard
            // Direct translate: no overlay, no result window. Capture the source window
            // now — once any Forge UI takes focus it would be gone.
            _sourceWindow = ReplaceAction.CaptureSourceWindow();
            _sourceElement = UiaProbe.GetFocusedElement();
            DebugLog.Write($"hotkey pressed → direct translate (source {ReplaceAction.DescribeWindow(_sourceWindow)})");
            _ = RunDirectAsync(ForgeTask.TranslateKrToEn);
            return;
        }

        if (_immediateHotkeys.TryGetValue(id, out var task))
        {
            if (!_guard.TryBegin())
                return; // a concurrent press already owns the guard
            DebugLog.Write($"immediate hotkey pressed → task '{task}'");
            _ = RunTaskAsync(task, null);
        }
    }

    private void ShowOverlay()
    {
        if (_overlay is not null && _overlay.IsVisible)
        {
            _overlay.Hide();
            DebugLog.Write("overlay hidden (toggle)");
            return;
        }

        // S11: capture the source window now — once the overlay is shown, Forge owns the focus.
        _sourceWindow = ReplaceAction.CaptureSourceWindow();

        _overlay ??= new TaskOverlayWindow();
        _overlay.TaskSelected += OnTaskSelected;
        _overlay.Show();
        _overlay.Activate();
        DebugLog.Write("overlay shown");
    }

    private void OnTaskSelected(ForgeTask task)
    {
        if (!_guard.TryBegin())
            return; // a concurrent press already owns the guard
        _ = RunTaskAsync(task, _overlay);
    }

    /// <summary>
    /// Runs one task through the S4 pipeline (same wiring as Forge.Cli): capture →
    /// PromptBuilder → streaming inference → ResultParser. The caller must have
    /// acquired the re-entry guard; it is released when the run finishes.
    ///
    /// S7: opens the result editor after capture, streams tokens into it, and renders
    /// the original-vs-result diff on completion. Closing the window (Cancel) cancels
    /// the inference and keeps the original text.
    /// </summary>
    private async Task RunTaskAsync(ForgeTask task, TaskOverlayWindow? overlay)
    {
        var engine = _engine!;
        DebugLog.Write($"task '{task}' started");
        SetState(ModelState.Loading, $"Forge: working on {task}…");
        overlay?.SetProcessing($"Working on {task}…");

        // S8 on-demand reload: the model may have been idle-unloaded since the last task.
        if (!engine.IsLoaded)
        {
            overlay?.SetProcessing("Loading model…");
            try
            {
                await Task.Run(() => engine.LoadModel(_modelPath));
            }
            catch (Exception ex)
            {
                DebugLog.Write($"task '{task}' failed to load model: {ex.Message}");
                SetState(ModelState.Error, $"Forge: model failed to load — {ex.Message}");
                overlay?.SetIdle("Failed to load model — open Settings to check the model path.");
                _guard.End();
                return;
            }
        }

        string status;
        ResultEditorWindow? window = null;
        var cts = new CancellationTokenSource();
        try
        {
            var capture = new TextCapture(
                contextSize: engine.ContextSize,
                safetyMargin: 4096,
                tokenCounter: engine.CountTokens);
            var selected = capture.Capture();

            window = new ResultEditorWindow(selected, task);
            window.Closed += (_, _) => cts.Cancel(); // Cancel / closing discards the result
            window.RegenerateRequested += () =>
            {
                if (!_guard.TryBegin())
                    return; // a concurrent run already owns the guard
                window.Close(); // replace this run with a fresh one
                _ = RunTaskAsync(task, null);
            };
            window.ReplaceRequested += text =>
            {
                var replaceResult = ReplaceAction.TryReplace(text, _sourceWindow);
                DebugLog.Write($"replace: {replaceResult.Outcome} — {replaceResult.Message}");
                if (replaceResult.Outcome == ReplaceAction.Outcome.Replaced)
                    PlayClang(); // the forge's hammer strike
                window.ReportReplace(replaceResult);
            };
            window.Show();

            var prompt = PromptBuilder.Build(task, selected);
            var promptTokens = engine.CountTokens(prompt);
            var maxTokens = Math.Max(16, engine.ContextSize - promptTokens - 256);

            var sb = new System.Text.StringBuilder();
            await foreach (var token in engine.Complete(prompt, maxTokens: maxTokens, cts.Token))
            {
                sb.Append(token);
                window.AppendToken(token);
            }

            var result = ResultParser.Parse(sb.ToString(), selected, task);
            window.Complete(result);
            _idle?.NotifyInferenceCompleted(); // S8: reset the idle-unload timer
            status = result.IsEmpty ? "No usable output" : "Done — result ready";
        }
        catch (TextCaptureException ex)
        {
            // S12: capture failures carry their own user-facing message with a recovery path.
            status = ex.Message;
            window?.Close();
        }
        catch (OperationCanceledException)
        {
            status = "Cancelled — original kept";
            window?.Close();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"task '{task}' failed: {ex}");
            status = $"Failed: {ex.Message} — the original text is untouched.";
            window?.Close();
        }

        DebugLog.Write($"task '{task}' finished: {status}");
        SetState(ModelState.Ready, "Forge: ready");
        overlay?.SetIdle(status);
        _guard.End();
    }

    /// <summary>
    /// Direct translate (no overlay, no result window): reads the source app's selection
    /// via UIA, runs the translation through the S4 pipeline, and replaces the selection
    /// in the source app. Success is silent apart from the tray tooltip; failures surface
    /// with a MessageBox carrying the specific recovery message.
    /// The caller must have acquired the re-entry guard; it is released when the run finishes.
    /// </summary>
    private async Task RunDirectAsync(ForgeTask task)
    {
        var engine = _engine!;
        DebugLog.Write($"direct '{task}' started");
        SetState(ModelState.Loading, $"Forge: working on {task}…");

        // S8 on-demand reload: the model may have been idle-unloaded since the last task.
        if (!engine.IsLoaded)
        {
            DebugLog.Write($"direct '{task}' model load started");
            try
            {
                await Task.Run(() => engine.LoadModel(_modelPath));
            }
            catch (Exception ex)
            {
                DebugLog.Write($"direct '{task}' failed to load model: {ex.Message}");
                SetState(ModelState.Error, $"Forge: model failed to load — {ex.Message}");
                MessageBox.Show($"Model failed to load — {ex.Message}. Open Settings to check the model path.",
                    "Forge", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            DebugLog.Write($"direct '{task}' model loaded");
        }

        try
        {
            // 1. Capture: read the selection directly via UIA (no keystrokes needed).
            //    Falls back to Ctrl+C if UIA can't read it.
            DebugLog.Write($"direct '{task}' capture started");
            string? selected = UiaProbe.GetFocusedSelectionText();
            if (selected is null || selected.Length == 0)
            {
                // Fallback: Ctrl+C on the UI thread
                var (clipText, failure) = Application.Current.Dispatcher.Invoke(() =>
                    ReplaceAction.CopySelection(_sourceWindow));
                selected = clipText;
            }
            if (selected is null)
                throw new TextCaptureException(TextCaptureError.EmptyClipboard,
                    "No text was selected. Select some text and try again.");
            DebugLog.Write($"direct '{task}' capture: {selected.Length} chars");

            var capture = new TextCapture(
                contextSize: engine.ContextSize,
                safetyMargin: 4096,
                tokenCounter: engine.CountTokens);
            selected = capture.Capture(selected); // validates the token limit

            // 2. Inference.
            var prompt = PromptBuilder.Build(task, selected);
            var promptTokens = engine.CountTokens(prompt);
            var maxTokens = Math.Max(16, engine.ContextSize - promptTokens - 256);
            DebugLog.Write($"direct '{task}' inference started ({promptTokens} prompt tokens)");
            var sb = new System.Text.StringBuilder();
            await foreach (var token in engine.Complete(prompt, maxTokens: maxTokens, CancellationToken.None))
                sb.Append(token);
            DebugLog.Write($"direct '{task}' inference done ({sb.Length} chars)");

            var result = ResultParser.Parse(sb.ToString(), selected, task);
            _idle?.NotifyInferenceCompleted(); // S8: reset the idle-unload timer

            if (result.IsEmpty)
            {
                DebugLog.Write($"direct '{task}' finished: no usable output");
                MessageBox.Show("No usable output — the original text is untouched.",
                    "Forge", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 3. Replace: use the source app's native edit control directly. Falls back to
            //    character-by-character typing (Handy Direct) for Chromium, then UIA splice.
            //    If all fail, the result stays on the clipboard with a guidance message.
            DebugLog.Write($"direct '{task}' replace started");
            ReplaceAction.ReplaceResult replaceResult;
            bool replaced = UiaProbe.ReplaceSelectionText(_sourceWindow, result.Text);
            if (replaced)
            {
                replaceResult = new(ReplaceAction.Outcome.Replaced, "Replaced via EM_REPLACESEL.");
            }
            else
            {
                // Fallback for Chromium: UIA splice — replace only the selection within the full text.
                // Works even when the visual selection is gone (inference takes seconds).
                bool uiaSet = UiaProbe.ReplaceSelectionViaUia(_sourceElement, selected, result.Text);
                if (uiaSet)
                    replaceResult = new(ReplaceAction.Outcome.Replaced, "Replaced via UIA splice.");
                else
                {
                    ClipboardGuard.WriteText(result.Text);
                    replaceResult = new(ReplaceAction.Outcome.ClipboardFallback,
                        "The result is on the clipboard. Press Ctrl+V to paste it manually.");
                }
            }
            DebugLog.Write($"direct '{task}' finished: {replaceResult.Outcome} — {replaceResult.Message} ({replaceResult.Diagnostics})");

            if (replaceResult.Outcome == ReplaceAction.Outcome.Replaced)
                PlayClang(); // the forge's hammer strike
            else
                MessageBox.Show(replaceResult.Message, "Forge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (TextCaptureException ex)
        {
            DebugLog.Write($"direct '{task}' capture failed: {ex.Message}");
            MessageBox.Show(ex.Message, "Forge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"direct '{task}' failed: {ex}");
            MessageBox.Show($"Failed: {ex.Message} — the original text is untouched.",
                "Forge", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetState(ModelState.Ready, "Forge: ready");
            _guard.End();
        }
    }

    /// <summary>
    /// Plays the "clang" strike on a successful replace — the forge's hammer hitting the anvil.
    /// Best-effort: a missing file or audio failure never interrupts a translation.
    /// </summary>
    private static void PlayClang()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "clang.wav");
            if (!File.Exists(path))
                return;
            // SoundPlayer.Play() is async (returns immediately); the object stays alive for the
            // duration of the short clip and is collected by GC afterwards.
            new System.Media.SoundPlayer(path).Play();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"clang: failed to play — {ex.Message}");
        }
    }

    private void BuildTray()
    {
        var menu = new ContextMenu();
        var settingsItem = new MenuItem { Header = "Open Settings" };
        settingsItem.Click += OnOpenSettings;
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Separator());
        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += OnExit;
        menu.Items.Add(exitItem);

        _tray = new TaskbarIcon { ContextMenu = menu };
        // Left-click opens the same menu as right-click (easier than right-click for many input methods).
        _tray.TrayLeftMouseUp += (_, _) => OpenTrayMenu();
    }

    private void OpenTrayMenu()
    {
        var menu = _tray?.ContextMenu;
        if (menu is null)
            return;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void SetState(ModelState state, string tooltip)
    {
        var iconFile = state switch
        {
            ModelState.Loading => "tray_loading.ico",
            ModelState.Ready => "tray_ready.ico",
            _ => "tray_error.ico",
        };
        _tray?.IconSource = LoadIcon(iconFile);
        _tray?.ToolTipText = tooltip;
    }

    private static BitmapImage LoadIcon(string fileName)
    {
        var uri = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", fileName), UriKind.Absolute);
        var image = new BitmapImage(uri);
        image.Freeze();
        return image;
    }

    private async Task LoadModelAsync(string modelPath)
    {
        // S10 first-run onboarding: when the configured model file is missing, download
        // it (with progress + resume via S9) before attempting to load.
        if (!File.Exists(modelPath))
            await ShowOnboardingAsync(modelPath);

        try
        {
            await Task.Run(() => _engine!.LoadModel(modelPath));
            _idle?.NotifyInferenceCompleted(); // S8: arm the idle timer from (re)load
            if (_engine!.FellBackToCpu)
                SetState(ModelState.Ready, "Forge: ready (GPU unavailable — using CPU)");
            else
                SetState(ModelState.Ready, "Forge: ready");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"model failed to load at startup: {ex}");
            SetState(ModelState.Error, $"Forge: model failed to load — {ex.Message}. Open Settings to check the model path.");
        }
    }

    /// <summary>
    /// S10: shows the onboarding window for a missing model file and waits for the
    /// download (or cancellation). The window is closed before this returns; a failed
    /// or cancelled download falls through to the normal load-failure path.
    /// </summary>
    private async Task ShowOnboardingAsync(string modelPath)
    {
        var onboarding = new OnboardingWindow(modelPath);
        onboarding.Show();
        DebugLog.Write("onboarding shown (model file missing)");
        await onboarding.RunDownloadAsync();
        if (!onboarding.Succeeded)
            DebugLog.Write("onboarding ended without a model file");
        onboarding.Close();
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        new SettingsWindow().Show();
    }

    // ---- S12: global exception handling (plan §7 — a fatal exception ends the app) ----

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true; // WPF would otherwise tear down with its own crash dialog
        string? dumpPath = WriteCrashDump(e.Exception);
        DebugLog.Write($"fatal exception: {e.Exception}");
        MessageBox.Show(FatalMessage(dumpPath), "Forge", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown();
    }

    private void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is not Exception ex)
            return;
        string? dumpPath = WriteCrashDump(ex);
        DebugLog.Write($"fatal exception (background thread): {ex}");
        try
        {
            // Best effort: the process is ending, so the box may never be pumped —
            // the dump file and debug log are the durable record.
            Dispatcher.BeginInvoke(() => MessageBox.Show(FatalMessage(dumpPath), "Forge", MessageBoxButton.OK, MessageBoxImage.Error));
        }
        catch
        {
            // The dispatcher is already gone; nothing left to show.
        }
    }

    private string? WriteCrashDump(Exception ex) =>
        _settings.CrashDumpEnabled ? CrashDump.Write(ex, _modelPath, _gpuLayers) : null;

    private static string FatalMessage(string? dumpPath) =>
        "Forge hit an unexpected error and will close." +
        (dumpPath is not null ? $"\n\nA crash dump was saved to:\n{dumpPath}" : "") +
        "\n\nYour text and clipboard are untouched. Restart Forge to continue.";

    private void OnExit(object sender, RoutedEventArgs e) => Shutdown();

    protected override void OnExit(ExitEventArgs e)
    {
        // Single cleanup point: release the idle timer, global hotkeys, and native model.
        _idleTimer?.Dispose();
        _hotkeys?.Dispose();
        _engine?.Unload();
        base.OnExit(e);
    }

    /// <summary>
    /// Splits startup args into (model path, show overlay at startup, debug log
    /// path). "--debug-overlay" and "--debug-log &lt;path&gt;" are smoke-test hooks;
    /// model-path resolution keeps the S5 rules on the remaining args.
    /// </summary>
    internal static (string ModelPath, bool ShowOverlay, string? DebugLog) ParseArgs(string[] args, string fallbackModelPath)
    {
        var showOverlay = false;
        string? debugLog = null;

        var modelArgs = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--debug-overlay")
            {
                showOverlay = true;
                continue;
            }
            if (args[i] == "--debug-log")
            {
                if (i + 1 < args.Length)
                    debugLog = args[++i];
                continue;
            }
            modelArgs.Add(args[i]);
        }

        return (ParseModelPath(modelArgs.ToArray(), fallbackModelPath), showOverlay, debugLog);
    }

    /// <summary>
    /// Model path from args: "--model &lt;path&gt;" takes priority, then the first
    /// positional argument, then the saved settings value (which defaults to the
    /// S0-verified model).
    /// </summary>
    internal static string ParseModelPath(string[] args, string fallback)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == "--model")
                return args[i + 1];

        foreach (var arg in args)
            if (!arg.StartsWith("--"))
                return arg;

        return fallback;
    }

    // ---- demo path (S7 debug): simulates a pipeline run without loading the model, so
    // the result editor's streaming → diff transition can be exercised on any machine. ----

    private async Task RunDemoAsync()
    {
        const string original = "오늘 날기자가 너무 좋아서 밖에 나가고 싶다.";
        const string result = "The weather is nice today, so I want to go outside.";

        var window = new ResultEditorWindow(original, ForgeTask.TranslateKrToEn);
        window.Show();
        DebugLog.Write("demo run started (canned result, no model)");

        await Task.Run(async () =>
        {
            for (int i = 0; i < result.Length; i += 2)
            {
                string chunk = result.Substring(i, Math.Min(2, result.Length - i));
                window.Dispatcher.Invoke(() => window.AppendToken(chunk));
                await Task.Delay(40);
            }
        });

        window.Complete(ResultParser.Parse(result, original, ForgeTask.TranslateKrToEn));
        DebugLog.Write("demo run completed");
    }
}
