using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forge.Core;
using Hardcodet.Wpf.TaskbarNotification;

namespace Forge.App;

public enum ModelState
{
    Loading,
    Ready,
    Error
}

/// <summary>
/// The Forge application (S16): a local Korean-to-English translator for non-English speakers.
/// One feature path: hotkey → capture the focused selection → translate → replace in place.
/// Feedback is three isolated modules — HammerIndicator (working indicator), ForgeSounds
/// (clang), SelectionReplacer (in-place replace). No overlay window, no result editor.
/// </summary>
public partial class App : Application, IOnboardingHost
{
    private ForgeSettings _settings = new();
    private string _modelPath = "";
    private int _gpuLayers;
    private InferenceEngine? _engine;
    private HardwareInfo? _hardware;
    private IdleUnloadManager? _idle;
    private System.Threading.Timer? _idleTimer;
    private HotkeyManager? _hotkeys;
    private TaskbarIcon? _tray;
    private ProcessingGuard _guard = new();
    private IntPtr _sourceWindow;
    private System.Windows.Automation.AutomationElement? _sourceElement;

    // S20: background model download owned by the app (started from the onboarding wizard).
    private Task? _modelDownload;
    private System.Threading.CancellationTokenSource? _downloadCts;

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
        // quits Forge. Only the tray Exit menu and the crash handler should end it.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;

        _settings = ForgeSettings.Load();
        var (modelPath, debugLog) = ParseArgs(e.Args, _settings.ModelPath);
        DebugLog.SetPath(debugLog);
        _modelPath = modelPath;

        // S8: detect hardware before any native load (LLamaSharp locks its runtime selection
        // on the first load) and resolve the GPU layer count from the settings choice.
        var hardware = HardwareDetector.Detect();
        _hardware = hardware;
        DebugLog.Write($"hardware: {hardware.Note}");
        _gpuLayers = ResolveGpuLayerCount(_settings.Hardware, hardware, modelPath);
        DebugLog.Write($"gpu layer count: {_gpuLayers} (mode: {_settings.Hardware})");

        _engine = new InferenceEngine(_settings.ContextSize, _gpuLayers);
        BuildTray();
        // S17: no startup model load — the model loads on demand at the first translation
        // and is unloaded per the settings' unload choice. Start ready; loading shows up
        // in the tray only while a translation actually runs.
        SetState(ModelState.Ready, "Forge: ready (model loads on demand)");

        SetupHotkeys();
        StartIdleTimer();

        // S20: first-run onboarding wizard (welcome, model download, hotkey, auto-start).
        if (!e.Args.Contains("--smoke-crash") && !_settings.OnboardingCompleted)
            ShowOnboardingWizard();

        if (e.Args.Contains("--smoke-crash"))
            _ = SmokeCrashAsync(); // S12 smoke hook: exercises the global handler + crash dump
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

    private void SetupHotkeys()
    {
        _hotkeys = new HotkeyManager();
        _hotkeys.Initialize();
        _hotkeys.HotkeyPressed += OnHotkeyPressed;

        // S16: one global hotkey — capture the focused selection and translate it to English.
        var binding = HotkeyConfig.Load();
        if (HotkeyConfig.TryParse(binding, out var modifiers, out var key))
        {
            if (!_hotkeys.Register(HotkeyConfig.TranslateId, modifiers, key))
                DebugLog.Write($"hotkey '{binding}' is already in use by another app — translation hotkey disabled");
            else
                DebugLog.Write($"hotkey registered: {binding} → translate selection");
        }
        else
            DebugLog.Write($"hotkey binding '{binding}' is not supported — translation hotkey disabled");
    }

    private void OnHotkeyPressed(int id)
    {
        if (id != HotkeyConfig.TranslateId)
            return;

        // S11: capture the source window now, before anything can steal focus.
        _sourceWindow = ReplaceAction.CaptureSourceWindow();
        _sourceElement = UiaProbe.GetFocusedElement();
        if (!_guard.TryBegin())
            return; // a concurrent press already owns the guard
        DebugLog.Write("translate hotkey pressed");
        _ = RunDirectAsync();
    }

    /// <summary>
    /// Direct translate (S16 — the app's only feature path): reads the source app's selection
    /// via UIA, runs it through the S4 pipeline, and replaces the selection in place.
    /// Feedback comes from three isolated modules: HammerIndicator (working indicator),
    /// SelectionReplacer (replace), ForgeSounds (clang). Success is silent apart from the
    /// tray tooltip; failures surface with a MessageBox carrying the specific recovery message.
    /// The caller must have acquired the re-entry guard; it is released when the run finishes.
    /// </summary>
    private async Task RunDirectAsync()
    {
        var engine = _engine!;
        DebugLog.Write("direct translate started");
        SetState(ModelState.Loading, "Forge: translating…");

        HammerIndicator? indicator = null;
        try
        {
            // S17 on-demand load: the model is not loaded at startup — it loads here, on the
            // first translation (and again after an idle unload). LoadModelAsync also runs the
            // first-run onboarding download when the file is missing.
            if (!engine.IsLoaded)
            {
                DebugLog.Write("direct translate: model load started");
                if (!await LoadModelAsync(_modelPath))
                {
                    // A missing file means the user cancelled onboarding — no error dialog.
                    if (File.Exists(_modelPath))
                        MessageBox.Show("Model failed to load — open Settings to check the model.",
                            "Forge", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                DebugLog.Write("direct translate: model loaded");
            }

            // Working indicator over the selection (isolated module). Shown only once the
            // model is loaded and about to process — never during onboarding or load failure.
            indicator = HammerIndicator.Show(_sourceElement);
            // 1. Capture: read the selection directly via UIA (no keystrokes needed).
            //    Falls back to Ctrl+C if UIA can't read it.
            DebugLog.Write("direct translate: capture started");
            string? selected = UiaProbe.GetFocusedSelectionText();
            if (selected is null || selected.Length == 0)
            {
                var (clipText, failure) = Application.Current.Dispatcher.Invoke(() =>
                    ReplaceAction.CopySelection(_sourceWindow));
                selected = clipText;
            }
            if (selected is null)
                throw new TextCaptureException(TextCaptureError.EmptyClipboard,
                    "No text was selected. Select some text and try again.");
            DebugLog.Write($"direct translate: capture {selected.Length} chars");

            var capture = new TextCapture(
                contextSize: engine.ContextSize,
                safetyMargin: 4096,
                tokenCounter: engine.CountTokens);
            selected = capture.Capture(selected); // validates the token limit

            // 2. Inference (S16: single prompt — translate to English).
            var family = ModelProfile.Detect(_modelPath);
            var prompt = PromptBuilder.Build(selected, family);
            var promptTokens = engine.CountTokens(prompt);
            var maxTokens = ModelProfile.MaxOutputTokens(family, Math.Max(16, engine.ContextSize - promptTokens - 256));
            DebugLog.Write($"direct translate: inference started ({promptTokens} prompt tokens)");
            var sb = new System.Text.StringBuilder();
            await foreach (var token in engine.Complete(prompt, maxTokens: maxTokens, CancellationToken.None))
                sb.Append(token);
            DebugLog.Write($"direct translate: inference done ({sb.Length} chars)");

            var result = ResultParser.Parse(OutputPostProcessor.Clean(sb.ToString(), family));
            UnloadAfterInference(); // S17: apply the unload choice (immediate release or idle timer)

            if (result.IsEmpty)
            {
                DebugLog.Write("direct translate finished: no usable output");
                indicator?.Close();
                MessageBox.Show("No usable output — the original text is untouched.",
                    "Forge", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 3. Replace in place (isolated module). On success: final strike + clang.
            DebugLog.Write("direct translate: replace started");
            var replaceResult = SelectionReplacer.Replace(_sourceWindow, _sourceElement, selected, result.Text);
            DebugLog.Write($"direct translate finished: {replaceResult.Outcome} — {replaceResult.Message}");

            if (replaceResult.Outcome == ReplaceAction.Outcome.Replaced)
            {
                indicator?.Complete(); // final strike, synced with the clang
                ForgeSounds.PlayClang();
            }
            else
            {
                indicator?.Close();
                MessageBox.Show(replaceResult.Message, "Forge", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (TextCaptureException ex)
        {
            DebugLog.Write($"direct translate capture failed: {ex.Message}");
            indicator?.Close();
            MessageBox.Show(ex.Message, "Forge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"direct translate failed: {ex}");
            indicator?.Close();
            MessageBox.Show($"Failed: {ex.Message} — the original text is untouched.",
                "Forge", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            indicator?.Close(); // safety net for any path that didn't finish the strike
            SetState(ModelState.Ready, "Forge: ready");
            _guard.End();
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

    private async Task<bool> LoadModelAsync(string modelPath)
    {
        // S10 first-run onboarding: when the configured model file is missing, download
        // it (with progress + resume via S9) before attempting to load.
        if (!File.Exists(modelPath))
            await ShowOnboardingAsync(modelPath);

        // The user cancelled or skipped onboarding — no file to load, no error.
        if (!File.Exists(modelPath))
            return false;

        try
        {
            await Task.Run(() => _engine!.LoadModel(modelPath));
            ArmIdleUnload(); // S17: arm the idle timer from (re)load, per current settings
            if (_engine!.FellBackToCpu)
                SetState(ModelState.Ready, "Forge: ready (GPU unavailable — using CPU)");
            else
                SetState(ModelState.Ready, "Forge: ready");
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"model failed to load: {ex}");
            SetState(ModelState.Error, $"Forge: model failed to load — {ex.Message}. Open Settings to check the model path.");
            return false;
        }
    }

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
        new SettingsWindow(SwapModelAsync).Show();
    }

    // ---- S20: onboarding wizard host (IOnboardingHost) ----

    public void ShowOnboardingWizard() => new OnboardingWizardWindow(this).Show();

    public string ModelPath => _modelPath;

    public bool IsModelDownloadRunning => _modelDownload is not null;

    /// <summary>Raised on the UI thread as the background download progresses.</summary>
    public event Action<DownloadProgress>? DownloadProgress;

    /// <summary>Raised on the UI thread when the background download ends.</summary>
    public event Action<ModelDownloadOutcome>? DownloadFinished;

    public void StartModelDownload()
    {
        if (_modelDownload is not null)
            return;

        var manager = new ModelManager(ModelManager.DefaultModelsDir);
        var model = manager.ListModels().FirstOrDefault(m =>
            string.Equals(Path.GetFullPath(manager.GetPath(m)), Path.GetFullPath(_modelPath), StringComparison.OrdinalIgnoreCase));
        if (model is null || string.IsNullOrEmpty(model.DownloadUrl) || manager.IsDownloaded(model))
            return;

        _downloadCts = new System.Threading.CancellationTokenSource();
        DebugLog.Write($"model download started: {model.Name}");
        _modelDownload = RunModelDownloadAsync(manager, model);
    }

    public void CancelModelDownload() => _downloadCts?.Cancel();

    private async Task RunModelDownloadAsync(ModelManager manager, ModelInfo model)
    {
        try
        {
            await manager.DownloadAsync(model,
                progress: new Progress<DownloadProgress>(p => DownloadProgress?.Invoke(p)),
                _downloadCts!.Token);
            DebugLog.Write($"model download complete: {model.Name}");
            DownloadFinished?.Invoke(ModelDownloadOutcome.Completed);
        }
        catch (OperationCanceledException)
        {
            DebugLog.Write("model download cancelled — partial file kept for resume");
            DownloadFinished?.Invoke(ModelDownloadOutcome.Cancelled);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"model download failed: {ex}");
            DownloadFinished?.Invoke(ModelDownloadOutcome.Failed);
        }
        finally
        {
            _modelDownload = null;
            _downloadCts = null;
        }
    }

    /// <summary>Saves the binding and re-registers the global hotkey. Returns false when unparseable or in use.</summary>
    public bool ApplyHotkey(string binding)
    {
        if (!HotkeyConfig.TryParse(binding, out var modifiers, out var key))
            return false;
        HotkeyConfig.Save(binding);
        _hotkeys!.UnregisterAll();
        bool ok = _hotkeys.Register(HotkeyConfig.TranslateId, modifiers, key);
        DebugLog.Write(ok ? $"hotkey re-registered: {binding}" : $"hotkey '{binding}' is already in use by another app");
        return ok;
    }

    public bool IsAutoStartEnabled => AutoStartManager.IsEnabled();

    public void SetAutoStart(bool enabled)
    {
        AutoStartManager.SetEnabled(enabled);
        var settings = ForgeSettings.Load(); // fresh — _settings is the startup snapshot
        settings.AutoStart = enabled;
        settings.Save();
        DebugLog.Write($"auto-start {(enabled ? "enabled" : "disabled")}");
    }

    public void FinishOnboarding()
    {
        var settings = ForgeSettings.Load();
        settings.OnboardingCompleted = true;
        settings.Save();
        DebugLog.Write("onboarding completed");
    }

    public void OpenSettingsWindow() => new SettingsWindow(SwapModelAsync).Show();

    /// <summary>
    /// Hot-swaps the configured model without a restart (S9): releases the currently loaded
    /// model so its memory is freed, and points the app at the new file. S17: it does NOT
    /// load the new model — loading is on demand at the next translation (which also runs
    /// the first-run onboarding download if the file is missing). Refused while a task is
    /// running. Returns true on success so the settings window can report the outcome.
    /// </summary>
    private async Task<bool> SwapModelAsync(string newPath)
    {
        if (_guard.IsProcessing)
        {
            SetState(ModelState.Error, "Cannot switch model while a task is running.");
            return false;
        }

        _modelPath = newPath;
        _gpuLayers = ResolveGpuLayerCount(_settings.Hardware, _hardware!, newPath);
        DebugLog.Write($"model swap: {newPath} (gpu layers: {_gpuLayers})");

        await Task.Run(() => _engine!.Unload());
        _idle = null; // the old model is gone; ArmIdleUnload re-arms after the next load
        return true;
    }

    private void StartIdleTimer()
    {
        // S17: the manager is created per-arm in ArmIdleUnload (the settings may change
        // while the app runs); this timer just ticks whatever is armed.
        _idleTimer = new System.Threading.Timer(
            _ => _idle?.Tick(),
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Arms (or clears) the idle-unload timer from the CURRENT settings. Called after every
    /// model load, so a settings change takes effect from the next run without a restart.
    /// "Never" and "Immediate" leave nothing armed — Immediate is handled at inference end.
    /// </summary>
    private void ArmIdleUnload()
    {
        var unloadAfter = ForgeSettings.Load().UnloadAfter;
        if (unloadAfter == ModelUnloadAfter.None || unloadAfter == ModelUnloadAfter.Immediate)
        {
            _idle = null;
            return;
        }

        int timeoutSeconds = unloadAfter == ModelUnloadAfter.OneMinute ? 60 : 300;
        _idle = new IdleUnloadManager(
            timeoutSeconds: timeoutSeconds,
            isLoaded: () => _engine?.IsLoaded ?? false,
            unload: () =>
            {
                DebugLog.Write("idle timer fired — unloading model");
                Task.Run(() => _engine?.Unload());
                SetState(ModelState.Ready, "Forge: idle (model unloaded)");
            });
        _idle.NotifyInferenceCompleted();
    }

    /// <summary>
    /// After a finished translation: applies the current settings' unload choice.
    /// "Immediate" releases the model right away; the timed options arm the idle timer.
    /// </summary>
    private void UnloadAfterInference()
    {
        if (ForgeSettings.Load().UnloadAfter == ModelUnloadAfter.Immediate)
        {
            DebugLog.Write("immediate unload — releasing model after translation");
            _idle = null;
            Task.Run(() => _engine?.Unload());
            SetState(ModelState.Ready, "Forge: idle (model unloaded)");
            return;
        }
        ArmIdleUnload();
    }

    // ---- S3 smoke test: crash the app on purpose to exercise the crash-dump path ----

    private async Task SmokeCrashAsync()
    {
        await Task.Delay(1500); // let startup finish so tray, hotkeys, and the engine exist
        _settings.CrashDumpEnabled = true;
        _ = Dispatcher.BeginInvoke(() => throw new InvalidOperationException("smoke crash (S12 verification)")); // fire-and-forget: the dispatcher op is intentionally not awaited
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
    /// Splits startup args into (model path, debug log path). "--debug-log &lt;path&gt;"
    /// is a smoke-test hook; model-path resolution keeps the S5 rules on the remaining args.
    /// </summary>
    internal static (string ModelPath, string? DebugLog) ParseArgs(string[] args, string fallbackModelPath)
    {
        string? debugLog = null;

        var modelArgs = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--debug-log")
            {
                if (i + 1 < args.Length)
                    debugLog = args[++i];
                continue;
            }
            modelArgs.Add(args[i]);
        }

        return (ParseModelPath(modelArgs.ToArray(), fallbackModelPath), debugLog);
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
}
