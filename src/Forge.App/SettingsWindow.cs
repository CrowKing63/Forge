using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;
using Forge.Core;

namespace Forge.App;

/// <summary>
/// Settings window (S7), code-only WPF. Left sidebar sections: Model / Engine / Hardware /
/// Hotkeys (Handy-style layout: nav column left, scrollable content right).
///
/// Persists to <see cref="ForgeSettings"/> (JSON under %LOCALAPPDATA%\Forge). The hotkey tab
/// is the source of truth the hotkey layer (S6) reads from; the model tab lists every GGUF
/// in the models directory (<see cref="ModelManager.DefaultModelsDir"/>) as clickable cards —
/// no dropdown — and offers to download catalog models that are not on disk yet. When the
/// app supplies <paramref name="applyModel"/>, selecting a model hot-swaps it — no restart.
///
/// Visuals: Forge's dark theme (<see cref="ForgeTheme"/>), applied via window resources;
/// each tab is a description line over a card of controls.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly ForgeSettings _settings;
    private readonly Func<string, Task<bool>>? _applyModel;
    private readonly string _originalModelPath;

    private StackPanel _modelList = null!;
    private StackPanel _downloadList = null!;
    private string _selectedModelPath = "";
    private CancellationTokenSource? _downloadCts;
    private readonly List<(Border Row, string Path)> _modelRows = new();
    private TextBox _contextBox = null!;
    private RadioButton _unloadImmediateRadio = null!;
    private RadioButton _unloadOneMinRadio = null!;
    private RadioButton _unloadFiveMinRadio = null!;
    private RadioButton _unloadNeverRadio = null!;
    private RadioButton _cpuRadio = null!;
    private RadioButton _gpuRadio = null!;
    private RadioButton _autoRadio = null!;
    private HotkeyField _translateBox = null!;
    private CheckBox _autoStartCheck = null!;
    private TextBlock _status = null!;
    private Border[] _navItems = null!;
    private UIElement[] _sections = null!;
    private ContentControl _contentHost = null!;
    private bool _loading;

    /// <param name="applyModel">
    /// Optional hot-swap hook: invoked with the new model path when the user saves a
    /// different model. Returns true on success. When null, saving only persists settings
    /// (takes effect on next start).
    /// </param>
    public SettingsWindow(Func<string, Task<bool>>? applyModel = null)
    {
        _settings = ForgeSettings.Load();
        _applyModel = applyModel;
        _originalModelPath = _settings.ModelPath;
        Title = "Forge Settings";
        Width = 640;
        Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = ForgeTheme.BackgroundBrush;
        Foreground = ForgeTheme.TextBrush;
        FontFamily = ForgeTheme.ThemeFont;
        FontSize = 14;
        Resources = ForgeTheme.Build();
        BuildLayout();
        _loading = true;
        try { PopulateFromSettings(); }
        finally { _loading = false; }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Dark title bar on Windows 11 (and Win10 2000+); a harmless no-op elsewhere.
        int useDark = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Stop any in-flight download (the .part file stays; it resumes later) and persist
        // any last edit (e.g. a focused field that never lost focus) before the window goes
        // away; closing does not quit the app — it lives on in the tray.
        _downloadCts?.Cancel();
        TryAutoSave();
        base.OnClosing(e);
    }

    private void BuildLayout()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left sidebar (Handy pattern): a right-bordered strip of rounded nav rows; the
        // selected section renders in a scrollable content area on the right.
        var sidebar = new Border
        {
            BorderBrush = ForgeTheme.BorderBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(10),
        };
        Grid.SetColumn(sidebar, 0);

        string[] names = { "Model", "Engine", "Hardware", "Hotkeys", "General" };
        _sections = new UIElement[5]
        {
            MakeModelSection(),
            MakeEngineSection(),
            MakeHardwareSection(),
            MakeHotkeysSection(),
            MakeGeneralSection(),
        };
        _navItems = new Border[5];

        var nav = new StackPanel();
        for (int i = 0; i < names.Length; i++)
        {
            int index = i;
            var item = MakeNavItem(names[index]);
            item.MouseLeftButtonUp += (_, _) => SelectSection(index);
            _navItems[i] = item;
            nav.Children.Add(item);
        }
        sidebar.Child = nav;
        root.Children.Add(sidebar);

        var contentArea = new Grid { Margin = new Thickness(16, 12, 16, 12) };
        contentArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        contentArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _contentHost = new ContentControl();
        scroll.Content = _contentHost;
        Grid.SetRow(scroll, 0);
        contentArea.Children.Add(scroll);

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        _status = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ForgeTheme.MutedBrush,
            TextWrapping = TextWrapping.Wrap,
        };
        bottom.Children.Add(_status);

        Grid.SetRow(bottom, 1);
        contentArea.Children.Add(bottom);

        Grid.SetColumn(contentArea, 1);
        root.Children.Add(contentArea);

        Content = root;
        SelectSection(0);
    }

    /// <summary>A rounded nav row in the sidebar: subtle fill when active, hover tint otherwise.</summary>
    private static Border MakeNavItem(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Foreground = ForgeTheme.MutedBrush,
        };
        var item = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 3, 0, 3),
            Background = Brushes.Transparent,
            Child = label,
        };
        item.MouseEnter += (_, _) =>
        {
            if (item.Background == Brushes.Transparent) item.Background = ForgeTheme.HoverFieldBrush;
        };
        item.MouseLeave += (_, _) =>
        {
            if (item.Background != ForgeTheme.FieldBrush) item.Background = Brushes.Transparent;
        };
        return item;
    }

    private void SelectSection(int index)
    {
        _contentHost.Content = _sections[index];
        for (int i = 0; i < _navItems.Length; i++)
        {
            bool active = i == index;
            _navItems[i].Background = active ? ForgeTheme.FieldBrush : Brushes.Transparent;
            ((TextBlock)_navItems[i].Child).Foreground = active ? ForgeTheme.TextBrush : ForgeTheme.MutedBrush;
        }
    }

    private UIElement MakeModelSection()
    {
        var panel = new StackPanel();

        // S18: catalog models are clickable cards, not a dropdown. Clicking one selects
        // it (auto-save + hot-swap). Models are obtained by download only — the folder
        // button opens the models directory for inspection.
        var title = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 4),
        };
        title.Children.Add(new TextBlock
        {
            Text = "Model",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        // A folder icon next to the title opens the models folder in Explorer.
        title.Children.Add(MakeFolderIconButton());
        panel.Children.Add(title);
        _modelList = new StackPanel();
        panel.Children.Add(_modelList);
        panel.Children.Add(MakeHint("Click a model to select it. Models you do not have yet are in the download section below."));

        // S18: download section — catalog models not yet on disk.
        panel.Children.Add(MakeSectionLabel("Download"));
        _downloadList = new StackPanel();
        panel.Children.Add(_downloadList);

        return MakeCard(panel);
    }

    private UIElement MakeEngineSection()
    {
        var panel = new StackPanel();
        _contextBox = new TextBox { Margin = new Thickness(0, 4, 0, 12), Width = 140 };
        _contextBox.LostFocus += (_, _) => TryAutoSave();
        panel.Children.Add(MakeLabel("Context size (tokens)"));
        panel.Children.Add(_contextBox);

        // S17: the model loads on demand at translation time; these choose when it is
        // unloaded afterwards. Fixed choices, not a free-form seconds field.
        _unloadImmediateRadio = MakeRadio("Unload immediately after each translation");
        _unloadOneMinRadio = MakeRadio("Unload after 1 minute of idle time");
        _unloadFiveMinRadio = MakeRadio("Unload after 5 minutes of idle time");
        _unloadNeverRadio = MakeRadio("Keep loaded (never unload)");
        foreach (var radio in new[] { _unloadImmediateRadio, _unloadOneMinRadio, _unloadFiveMinRadio, _unloadNeverRadio })
            radio.Checked += (_, _) => TryAutoSave();
        panel.Children.Add(MakeLabel("Model unload"));
        panel.Children.Add(MakeHint("The model loads when you translate and frees memory after this much idle time."));
        panel.Children.Add(_unloadImmediateRadio);
        panel.Children.Add(_unloadOneMinRadio);
        panel.Children.Add(_unloadFiveMinRadio);
        panel.Children.Add(_unloadNeverRadio);

        return MakeCard(panel);
    }

    private UIElement MakeHardwareSection()
    {
        var panel = new StackPanel();
        _cpuRadio = MakeRadio("CPU");
        _gpuRadio = MakeRadio("GPU");
        _autoRadio = MakeRadio("Auto");
        _cpuRadio.Checked += (_, _) => TryAutoSave();
        _gpuRadio.Checked += (_, _) => TryAutoSave();
        _autoRadio.Checked += (_, _) => TryAutoSave();
        panel.Children.Add(_cpuRadio);
        panel.Children.Add(_gpuRadio);
        panel.Children.Add(_autoRadio);

        string detectionText;
        try
        {
            var hardware = HardwareDetector.Detect();
            detectionText = hardware.Note;
        }
        catch (Exception)
        {
            detectionText = "Hardware detection unavailable.";
        }
        panel.Children.Add(MakeHint(detectionText));
        return MakeCard(panel);
    }

    private UIElement MakeHotkeysSection()
    {
        var panel = new StackPanel();
        panel.Children.Add(MakeHint("Click a field, then press the key combination (e.g. Ctrl+E). Press Esc while recording to clear it."));

        _translateBox = new HotkeyField { Margin = new Thickness(0, 4, 0, 12), Width = 180 };
        _translateBox.ValueChanged += (_, _) => TryAutoSave();
        panel.Children.Add(MakeLabel("Translate"));
        panel.Children.Add(_translateBox);

        return MakeCard(panel);
    }

    /// <summary>General (S20): auto-start at sign-in, and a way to re-run the onboarding wizard.</summary>
    private UIElement MakeGeneralSection()
    {
        var panel = new StackPanel();
        _autoStartCheck = new CheckBox
        {
            Content = "Start Forge when I sign in to Windows",
            Margin = new Thickness(0, 4, 0, 12),
        };
        _autoStartCheck.Checked += (_, _) => TryAutoSave();
        _autoStartCheck.Unchecked += (_, _) => TryAutoSave();
        panel.Children.Add(_autoStartCheck);

        var onboardingButton = new Button { Content = "Show onboarding again", MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
        onboardingButton.Click += (_, _) => ((App)Application.Current).ShowOnboardingWizard();
        panel.Children.Add(MakeSectionLabel("Onboarding"));
        panel.Children.Add(onboardingButton);

        return MakeCard(panel);
    }

    /// <summary>Rebuilds the installed-model cards and the download section from disk.</summary>
    private void RebuildModelLists()
    {
        var manager = new ModelManager(ModelManager.DefaultModelsDir);

        _modelList.Children.Clear();
        _modelRows.Clear();
        foreach (var m in manager.ListModels().Where(manager.IsDownloaded))
        {
            string sub = string.IsNullOrEmpty(m.Quantization)
                ? ""
                : $"{m.Quantization} · {m.SizeBytes / 1024 / 1024} MB";
            _modelList.Children.Add(MakeModelRow(m.Name, sub, manager.GetPath(m)));
        }

        UpdateModelSelection();

        _downloadList.Children.Clear();
        var downloadable = manager.ListModels()
            .Where(m => m.DownloadUrl.Length > 0 && !manager.IsDownloaded(m))
            .ToList();
        if (downloadable.Count == 0)
            _downloadList.Children.Add(MakeHint("All available models are installed."));
        else
            foreach (var m in downloadable)
                _downloadList.Children.Add(MakeDownloadRow(m));
    }

    /// <summary>A clickable model card: name over quantization/size; accent border when selected.</summary>
    private Border MakeModelRow(string name, string sub, string path)
    {
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold });
        if (sub.Length > 0)
            text.Children.Add(new TextBlock
            {
                Text = sub,
                Foreground = ForgeTheme.MutedBrush,
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0),
            });

        var row = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 3, 0, 3),
            Background = Brushes.Transparent,
            Child = text,
        };
        row.MouseEnter += (_, _) =>
        {
            if (row.Background == Brushes.Transparent) row.Background = ForgeTheme.HoverFieldBrush;
        };
        row.MouseLeave += (_, _) =>
        {
            if (row.Background != ForgeTheme.FieldBrush) row.Background = Brushes.Transparent;
        };
        row.MouseLeftButtonUp += (_, _) => SelectModel(path);
        _modelRows.Add((row, path));
        return row;
    }

    /// <summary>A small folder icon next to the Model title: opens the models folder in Explorer.</summary>
    private static Border MakeFolderIconButton()
    {
        var glyph = new Shapes.Path
        {
            Data = Geometry.Parse("M2 4.5 L6.5 4.5 L8.5 6.5 L14 6.5 L14 12.5 L2 12.5 Z"),
            Stroke = ForgeTheme.MutedBrush,
            StrokeThickness = 1.5,
            SnapsToDevicePixels = true,
        };
        var button = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            Child = glyph,
            ToolTip = "Open the models folder",
        };
        button.MouseEnter += (_, _) =>
        {
            if (button.Background == Brushes.Transparent) button.Background = ForgeTheme.HoverFieldBrush;
            glyph.Stroke = ForgeTheme.TextBrush;
        };
        button.MouseLeave += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            glyph.Stroke = ForgeTheme.MutedBrush;
        };
        button.MouseLeftButtonUp += (_, _) => OpenModelsFolder();
        return button;
    }

    /// <summary>Opens the models folder in Explorer (creating it if it does not exist yet).</summary>
    private static void OpenModelsFolder()
    {
        try
        {
            var dir = ModelManager.DefaultModelsDir;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Best-effort: a failed Explorer launch must not crash the settings window.
        }
    }

    /// <summary>A download card: name + size with a Download button and progress bar.</summary>
    private Border MakeDownloadRow(ModelInfo model)
    {
        var info = new StackPanel();
        info.Children.Add(new TextBlock { Text = model.Name, FontWeight = FontWeights.SemiBold });
        info.Children.Add(MakeHint($"{model.SizeBytes / 1024 / 1024} MB"));

        var download = new Button { Content = "Download", MinWidth = 90 };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, Visibility = Visibility.Collapsed, Margin = new Thickness(8, 0, 0, 0) };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        actions.Children.Add(download);
        actions.Children.Add(cancel);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(info);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);

        var bar = new ProgressBar { Height = 6, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };

        var card = new StackPanel();
        card.Children.Add(grid);
        card.Children.Add(bar);

        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = ForgeTheme.FieldBrush,
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 3, 0, 3),
            Child = card,
        };

        cancel.Click += (_, _) => _downloadCts?.Cancel();
        download.Click += async (_, _) =>
        {
            download.IsEnabled = false;
            cancel.Visibility = Visibility.Visible;
            bar.Visibility = Visibility.Visible;
            _downloadCts = new CancellationTokenSource();
            var manager = new ModelManager(ModelManager.DefaultModelsDir);
            try
            {
                await manager.DownloadAsync(
                    model,
                    progress: new Progress<DownloadProgress>(p => Dispatcher.Invoke(() => OnDownloadProgress(bar, p))),
                    _downloadCts.Token);
                SetStatus($"Download complete: {model.Name}.");
                RebuildModelLists();
            }
            catch (OperationCanceledException)
            {
                SetStatus("Download cancelled — the partial file is kept; it resumes next time.");
            }
            catch (Exception ex)
            {
                SetStatus($"Download failed: {ex.Message}");
            }
            finally
            {
                _downloadCts = null;
                download.IsEnabled = true;
                cancel.Visibility = Visibility.Collapsed;
                bar.Visibility = Visibility.Collapsed;
            }
        };

        return border;
    }

    /// <summary>Updates the accent highlight on the selected model card.</summary>
    private void UpdateModelSelection()
    {
        foreach (var (row, path) in _modelRows)
        {
            bool selected = string.Equals(path, _selectedModelPath, StringComparison.OrdinalIgnoreCase);
            row.Background = selected ? ForgeTheme.FieldBrush : Brushes.Transparent;
            row.BorderBrush = selected ? ForgeTheme.AccentBrush : null;
            row.BorderThickness = selected ? new Thickness(1) : new Thickness(0);
        }
    }

    /// <summary>Selects a model card: updates the highlight and auto-saves (hot-swap).</summary>
    private void SelectModel(string path)
    {
        if (string.Equals(path, _selectedModelPath, StringComparison.OrdinalIgnoreCase)) return;
        _selectedModelPath = path;
        UpdateModelSelection();
        TryAutoSave();
    }

    /// <summary>Maps download progress onto a card's progress bar.</summary>
    private static void OnDownloadProgress(ProgressBar bar, DownloadProgress p)
    {
        bar.IsIndeterminate = p.TotalBytes <= 0;
        bar.Value = Math.Clamp(p.Fraction * 100, 0, 100);
    }

    /// <summary>A card: the subtle surface every section's controls sit on.</summary>
    private static Border MakeCard(UIElement content) => new()
    {
        Background = ForgeTheme.CardBrush,
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(16),
        Margin = new Thickness(0, 4, 0, 8),
        Child = content,
    };

    private static TextBlock MakeLabel(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 2, 0, 4),
    };

    private static TextBlock MakeSectionLabel(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 8, 0, 4),
    };

    private TextBlock MakeHint(string text) => new()
    {
        Text = text,
        Style = (Style)Resources["HintText"],
        Margin = new Thickness(0, 6, 0, 0),
    };

    private static RadioButton MakeRadio(string content) => new()
    {
        Content = content,
        Margin = new Thickness(0, 3, 0, 3),
    };

    private void PopulateFromSettings()
    {
        // S18: build the model cards (installed models + download section) and mark the
        // currently configured one as selected.
        _selectedModelPath = _settings.ModelPath;
        RebuildModelLists();

        _contextBox.Text = _settings.ContextSize.ToString();
        _unloadImmediateRadio.IsChecked = _settings.UnloadAfter == ModelUnloadAfter.Immediate;
        _unloadOneMinRadio.IsChecked = _settings.UnloadAfter == ModelUnloadAfter.OneMinute;
        _unloadFiveMinRadio.IsChecked = _settings.UnloadAfter == ModelUnloadAfter.FiveMinutes;
        _unloadNeverRadio.IsChecked = _settings.UnloadAfter == ModelUnloadAfter.None;

        _cpuRadio.IsChecked = _settings.Hardware == HardwareMode.Cpu;
        _gpuRadio.IsChecked = _settings.Hardware == HardwareMode.Gpu;
        _autoRadio.IsChecked = _settings.Hardware == HardwareMode.Auto;

        // Hotkeys come from the config file the hotkey layer (S6) reads.
        _translateBox.Text = HotkeyConfig.Load();

        // S20: auto-start reflects the Run key's current state.
        _autoStartCheck.IsChecked = AutoStartManager.IsEnabled();
    }

    /// <summary>
    /// Validates the current field values and persists them; reports via the status line.
    /// Called on every control change (auto-save) and when the window closes.
    /// </summary>
    private void TryAutoSave()
    {
        if (_loading) return;
        if (!int.TryParse(_contextBox.Text, out int contextSize) || contextSize <= 0)
        {
            SetStatus("Context size must be a positive number.");
            return;
        }

        string newModelPath = _selectedModelPath;
        bool modelChanged = newModelPath != _originalModelPath;

        _settings.ModelPath = newModelPath;
        _settings.ContextSize = contextSize;
        _settings.UnloadAfter = _unloadImmediateRadio.IsChecked == true ? ModelUnloadAfter.Immediate
            : _unloadFiveMinRadio.IsChecked == true ? ModelUnloadAfter.FiveMinutes
            : _unloadOneMinRadio.IsChecked == true ? ModelUnloadAfter.OneMinute
            : ModelUnloadAfter.None;
        _settings.Hardware = _gpuRadio.IsChecked == true ? HardwareMode.Gpu
            : _autoRadio.IsChecked == true ? HardwareMode.Auto
            : HardwareMode.Cpu;

        string translate = _translateBox.Text.Trim();
        if (!HotkeyBinding.TryParse(translate, out _))
        {
            SetStatus("Invalid translate hotkey (expected e.g. \"Ctrl+E\").");
            return;
        }

        try
        {
            // S20: keep the auto-start setting and the Run key in sync on every save.
            _settings.AutoStart = _autoStartCheck.IsChecked == true;
            AutoStartManager.SetEnabled(_settings.AutoStart);
            _settings.Save();
            SaveHotkeys(translate);
            if (modelChanged && _applyModel is not null)
            {
                SetStatus("Switching model…");
                _ = ApplyModelAsync(newModelPath);
            }
            else
                SetStatus("Saved.");
        }
        catch (Exception ex)
        {
            SetStatus($"Save failed: {ex.Message}");
        }
    }

    /// <summary>Runs the hot-swap hook and reports the outcome in the status line.</summary>
    private async Task ApplyModelAsync(string path)
    {
        bool ok = await _applyModel!(path);
        // S17: the swap only re-points the app (loading is on demand at the next translation).
        SetStatus(ok ? "Saved — will use this model from the next translation." : "Saved — model switch failed.");
    }

    /// <summary>Writes the hotkey config file that the hotkey layer (S6) reads.</summary>
    private static void SaveHotkeys(string translate)
    {
        var payload = new { translate };
        var dir = Path.GetDirectoryName(HotkeyConfig.ConfigPath);
        if (dir is not null) Directory.CreateDirectory(dir);
        File.WriteAllText(HotkeyConfig.ConfigPath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Formats a parsed binding back into its "Ctrl+E" display form.</summary>
    private static string HotkeyText(HotkeyBinding b)
    {
        var parts = new List<string>();
        if ((b.Modifiers & HotkeyBinding.ModControl) != 0) parts.Add("Ctrl");
        if ((b.Modifiers & HotkeyBinding.ModShift) != 0) parts.Add("Shift");
        if ((b.Modifiers & HotkeyBinding.ModAlt) != 0) parts.Add("Alt");
        if ((b.Modifiers & HotkeyBinding.ModWin) != 0) parts.Add("Win");
        int vk = b.Key;
        if (vk is >= 0x41 and <= 0x5A) parts.Add(((char)vk).ToString());
        else if (vk is >= '0' and <= '9') parts.Add(((char)vk).ToString());
        else if (vk is >= 0x70 and <= 0x87) parts.Add($"F{vk - 0x6F}");
        return string.Join("+", parts);
    }

    private void SetStatus(string text) => _status.Text = text;

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
