using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
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
/// error). Context menu: Open Settings (stub, S7) and Exit.
/// </summary>
public partial class App : Application
{
    /// <summary>Default model path; overridable via "--model &lt;path&gt;" or a positional arg.</summary>
    public const string DefaultModelPath =
        @"C:\Users\uitaek\.cache\huggingface\hub\models--unsloth--gemma-4-E2B-it-qat-GGUF\snapshots\66a399f68ddd113b06dff02fca9523e55465d11d\gemma-4-E2B-it-qat-UD-Q4_K_XL.gguf";

    private InferenceEngine? _engine;
    private TaskbarIcon? _tray;

    [STAThread]
    public static void Main()
    {
        var app = new App();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var modelPath = ParseModelPath(e.Args);
        _engine = new InferenceEngine();
        BuildTray();
        SetState(ModelState.Loading, "Forge: loading model…");

        _ = LoadModelAsync(modelPath);
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
        try
        {
            await Task.Run(() => _engine!.LoadModel(modelPath));
            SetState(ModelState.Ready, "Forge: ready");
        }
        catch (Exception ex)
        {
            SetState(ModelState.Error, $"Forge: model failed to load — {ex.Message}");
        }
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        new SettingsWindow().Show();
    }

    private async void OnExit(object sender, RoutedEventArgs e)
    {
        await _engine!.DisposeAsync();
        Shutdown();
    }

    /// <summary>
    /// Model path from args: "--model &lt;path&gt;" takes priority, then the first
    /// positional argument, then the default.
    /// </summary>
    internal static string ParseModelPath(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == "--model")
                return args[i + 1];

        foreach (var arg in args)
            if (!arg.StartsWith("--"))
                return arg;

        return DefaultModelPath;
    }
}
