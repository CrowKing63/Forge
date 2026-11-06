using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Forge.Core;

namespace Forge.App;

/// <summary>
/// First-run onboarding wizard (S20), code-only WPF: four themed pages — welcome, model
/// download, translate hotkey, and auto-start — with Back/Next navigation. The model
/// download runs in the background owned by the app host, so the user can move on to the
/// next page while it is still downloading; closing the wizard never stops it.
///
/// Closing the window (X) or clicking Finish both count as completion: the host marks
/// onboarding done and persists that. The mid-flow <see cref="OnboardingWindow"/> remains
/// as the fallback for a missing model file at translation time.
/// </summary>
public sealed class OnboardingWizardWindow : Window
{
    private readonly IOnboardingHost _host;
    private int _page;

    private ContentControl _contentHost = null!;
    private UIElement[] _pages = null!;
    private TextBlock _stepLabel = null!;
    private Button _backButton = null!;
    private Button _nextButton = null!;

    // Page 2: model
    private TextBlock _modelStatus = null!;
    private ProgressBar _modelBar = null!;
    private Button _downloadButton = null!;
    private Button _cancelButton = null!;
    private Button _openSettingsButton = null!;

    // Page 3: hotkey
    private HotkeyField _hotkeyField = null!;

    // Page 4: auto-start
    private CheckBox _autoStartCheck = null!;

    public OnboardingWizardWindow(IOnboardingHost host)
    {
        _host = host;
        Title = "Forge — welcome";
        Width = 560;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = ForgeTheme.BackgroundBrush;
        Foreground = ForgeTheme.TextBrush;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        Resources = ForgeTheme.Build();

        BuildLayout();

        // Seed initial values BEFORE wiring the change handlers, so programmatic setup
        // cannot fire a save (same _loading convention as the settings window).
        _hotkeyField.Text = HotkeyConfig.Load();
        _autoStartCheck.IsChecked = host.IsAutoStartEnabled;

        _hotkeyField.ValueChanged += (_, _) => OnHotkeyCaptured();
        _autoStartCheck.Checked += (_, _) => host.SetAutoStart(true);
        _autoStartCheck.Unchecked += (_, _) => host.SetAutoStart(false);
        host.DownloadProgress += OnDownloadProgress;
        host.DownloadFinished += OnDownloadFinished;

        ShowPage(0);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Dark title bar on Windows 11 (and Win10 2000+); a harmless no-op elsewhere.
        int useDark = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
    }

    protected override void OnClosed(EventArgs e)
    {
        // Closing (X) counts as completion, same as Finish.
        _host.DownloadProgress -= OnDownloadProgress;
        _host.DownloadFinished -= OnDownloadFinished;
        _host.FinishOnboarding();
        base.OnClosed(e);
    }

    private void BuildLayout()
    {
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // step label
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // page card
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // nav

        _stepLabel = new TextBlock
        {
            Foreground = ForgeTheme.MutedBrush,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(_stepLabel, 0);
        root.Children.Add(_stepLabel);

        _contentHost = new ContentControl();
        Grid.SetRow(_contentHost, 1);
        root.Children.Add(_contentHost);

        var nav = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        _backButton = new Button
        {
            Content = "Back",
            MinWidth = 90,
            Visibility = Visibility.Collapsed,
        };
        _backButton.Click += (_, _) => ShowPage(_page - 1);
        _nextButton = new Button
        {
            Content = "Next",
            MinWidth = 90,
            Style = (Style)Resources["AccentButton"],
        };
        _nextButton.Click += OnNextClicked;
        nav.Children.Add(_backButton);
        nav.Children.Add(_nextButton);
        Grid.SetRow(nav, 2);
        root.Children.Add(nav);

        Content = root;

        _pages = new UIElement[4]
        {
            MakeWelcomePage(),
            MakeModelPage(),
            MakeHotkeyPage(),
            MakeAutoStartPage(),
        };
    }

    private void ShowPage(int page)
    {
        _page = Math.Clamp(page, 0, _pages.Length - 1);
        _contentHost.Content = _pages[_page];
        _stepLabel.Text = $"{_page + 1} of {_pages.Length}";
        _backButton.Visibility = _page == 0 ? Visibility.Collapsed : Visibility.Visible;
        _nextButton.Content = _page == _pages.Length - 1 ? "Finish" : "Next";
        if (_page == 1)
            RefreshModelPage(); // the download state may have changed while away
    }

    private void OnNextClicked(object sender, RoutedEventArgs e)
    {
        if (_page < _pages.Length - 1)
            ShowPage(_page + 1);
        else
            Close(); // Finish
    }

    private void OnHotkeyCaptured()
    {
        // Auto-save on capture (the settings window's convention): persist and re-register.
        // An empty or invalid value keeps the previous binding — the hotkey can never be lost.
        string binding = _hotkeyField.Text.Trim();
        if (binding.Length == 0 || !_host.ApplyHotkey(binding))
            _hotkeyField.Text = HotkeyConfig.Load();
    }

    // ---- Page 2: model download (background, owned by the host) ----

    private void RefreshModelPage()
    {
        var manager = new ModelManager(ModelManager.DefaultModelsDir);
        if (File.Exists(_host.ModelPath))
        {
            _modelStatus.Text = "Model ready.";
            _modelBar.Visibility = Visibility.Collapsed;
            _modelBar.Value = 0;
            HideDownloadButtons();
            return;
        }

        var model = manager.ListModels().FirstOrDefault(m =>
            string.Equals(Path.GetFullPath(manager.GetPath(m)), Path.GetFullPath(_host.ModelPath), StringComparison.OrdinalIgnoreCase));

        if (model is null || string.IsNullOrEmpty(model.DownloadUrl))
        {
            // No catalog source for this path — a custom GGUF that was never downloaded.
            _modelStatus.Text = "No model file found, and this path has no download source.";
            _modelBar.Visibility = Visibility.Collapsed;
            HideDownloadButtons();
            _openSettingsButton.Visibility = Visibility.Visible;
            return;
        }

        if (_host.IsModelDownloadRunning)
        {
            _modelStatus.Text = "Downloading… (you can continue to the next page)";
            _modelBar.Visibility = Visibility.Visible;
            _downloadButton.Visibility = Visibility.Collapsed;
            _cancelButton.Visibility = Visibility.Visible;
            return;
        }

        // Ask before downloading: a multi-GB download should not start uninvited.
        _modelStatus.Text = $"Do you want to download {model.Name} ({model.SizeBytes / 1024 / 1024} MB)?";
        _modelBar.Visibility = Visibility.Collapsed;
        _downloadButton.Visibility = Visibility.Visible;
        _cancelButton.Visibility = Visibility.Collapsed;
        _openSettingsButton.Visibility = Visibility.Collapsed;
    }

    private void HideDownloadButtons()
    {
        _downloadButton.Visibility = Visibility.Collapsed;
        _cancelButton.Visibility = Visibility.Collapsed;
        _openSettingsButton.Visibility = Visibility.Collapsed;
    }

    private void OnDownloadProgress(DownloadProgress p)
    {
        _modelBar.IsIndeterminate = p.TotalBytes <= 0;
        _modelBar.Value = Math.Clamp(p.Fraction * 100, 0, 100);
        _modelStatus.Text = $"Downloading… {p.BytesReceived / 1024 / 1024} MB of {p.TotalBytes / 1024 / 1024} MB";
    }

    private void OnDownloadFinished(ModelDownloadOutcome outcome)
    {
        if (outcome == ModelDownloadOutcome.Completed)
        {
            RefreshModelPage(); // shows "Model ready."
            return;
        }
        _modelStatus.Text = outcome == ModelDownloadOutcome.Cancelled
            ? "Download cancelled — the partial file is kept; it resumes next time."
            : "Download failed.";
        HideDownloadButtons();
    }

    private void OnDownloadClicked(object sender, RoutedEventArgs e)
    {
        _host.StartModelDownload();
        RefreshModelPage();
    }

    // ---- Page builders ----

    private UIElement MakeWelcomePage()
    {
        var panel = new StackPanel();
        panel.Children.Add(MakeHeading("Welcome to Forge"));
        panel.Children.Add(MakeHint(
            "Forge translates the text you select, right where it sits. Select some text in any app, press Ctrl+E, and the selection is replaced with its English translation — running entirely on your own machine."));
        return MakeCard(panel);
    }

    private UIElement MakeModelPage()
    {
        var panel = new StackPanel();
        panel.Children.Add(MakeHeading("Model"));
        _modelStatus = MakeHint("");
        panel.Children.Add(_modelStatus);
        _modelBar = new ProgressBar
        {
            Height = 6,
            Margin = new Thickness(0, 8, 0, 8),
            Visibility = Visibility.Collapsed,
        };
        panel.Children.Add(_modelBar);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _downloadButton = new Button
        {
            Content = "Download",
            MinWidth = 90,
            Style = (Style)Resources["AccentButton"],
        };
        _downloadButton.Click += OnDownloadClicked;
        _cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 80,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(8, 0, 0, 0),
        };
        _cancelButton.Click += (_, _) => _host.CancelModelDownload();
        actions.Children.Add(_downloadButton);
        actions.Children.Add(_cancelButton);
        panel.Children.Add(actions);

        _openSettingsButton = new Button
        {
            Content = "Open Settings",
            MinWidth = 120,
            Visibility = Visibility.Collapsed,
        };
        _openSettingsButton.Click += (_, _) => _host.OpenSettingsWindow();
        panel.Children.Add(_openSettingsButton);

        return MakeCard(panel);
    }

    private UIElement MakeHotkeyPage()
    {
        var panel = new StackPanel();
        panel.Children.Add(MakeHeading("Translate hotkey"));
        panel.Children.Add(MakeHint(
            "Click the field, then press the key combination (e.g. Ctrl+E). Press Esc while recording to clear it."));
        panel.Children.Add(MakeLabel("Translate"));
        _hotkeyField = new HotkeyField { Margin = new Thickness(0, 4, 0, 8), Width = 180 };
        panel.Children.Add(_hotkeyField);
        panel.Children.Add(MakeHint("Leave it as is to keep the default."));
        return MakeCard(panel);
    }

    private UIElement MakeAutoStartPage()
    {
        var panel = new StackPanel();
        panel.Children.Add(MakeHeading("Start with Windows"));
        _autoStartCheck = new CheckBox
        {
            Content = "Start Forge when I sign in to Windows",
            Margin = new Thickness(0, 4, 0, 8),
        };
        panel.Children.Add(_autoStartCheck);
        panel.Children.Add(MakeHint("Forge launches in the background (tray) automatically after you sign in."));
        return MakeCard(panel);
    }

    // ---- Themed building blocks (same surfaces as the settings window) ----

    private static Border MakeCard(UIElement content) => new()
    {
        Background = ForgeTheme.CardBrush,
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(16),
        Child = content,
    };

    private static TextBlock MakeHeading(string text) => new()
    {
        Text = text,
        FontSize = 16,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 8),
    };

    private static TextBlock MakeLabel(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 2, 0, 4),
    };

    private TextBlock MakeHint(string text) => new()
    {
        Text = text,
        Style = (Style)Resources["HintText"],
        Margin = new Thickness(0, 6, 0, 0),
    };

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
