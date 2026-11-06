using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Forge.Core;

namespace Forge.App;

/// <summary>
/// Settings window (S7), code-only WPF. Left sidebar sections: Model / Engine / Hardware /
/// Hotkeys (Handy-style layout: nav column left, scrollable content right).
///
/// Persists to <see cref="ForgeSettings"/> (JSON under %LOCALAPPDATA%\Forge). The hotkey tab
/// is the source of truth the hotkey layer (S6) reads from; the model tab lists every GGUF
/// in the models directory (<see cref="ModelManager.DefaultModelsDir"/>) and, when the app
/// supplies <paramref name="applyModel"/>, hot-swaps the loaded model on save — no restart.
///
/// Visuals: Forge's dark theme (<see cref="ForgeTheme"/>), applied via window resources;
/// each tab is a description line over a card of controls.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly ForgeSettings _settings;
    private readonly Func<string, Task<bool>>? _applyModel;
    private readonly string _originalModelPath;

    private ComboBox _modelBox = null!;
    private TextBox _contextBox = null!;
    private RadioButton _unloadImmediateRadio = null!;
    private RadioButton _unloadOneMinRadio = null!;
    private RadioButton _unloadFiveMinRadio = null!;
    private RadioButton _unloadNeverRadio = null!;
    private RadioButton _cpuRadio = null!;
    private RadioButton _gpuRadio = null!;
    private RadioButton _autoRadio = null!;
    private HotkeyField _translateBox = null!;
    private TextBlock _status = null!;
    private Border[] _navItems = null!;
    private UIElement[] _sections = null!;
    private ContentControl _contentHost = null!;
    private bool _loading;

    /// <summary>One dropdown entry: a catalog model plus its file path.</summary>
    private sealed record ModelChoice(ModelInfo Info, string Path)
    {
        public override string ToString() =>
            string.IsNullOrEmpty(Info.Quantization) ? Info.Name : $"{Info.Name} ({Info.Quantization})";
    }

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
        Width = 720;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = ForgeTheme.BackgroundBrush;
        Foreground = ForgeTheme.TextBrush;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
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
        // Persist any last edit (e.g. a focused field that never lost focus) before the
        // window goes away; closing does not quit the app — it lives on in the tray.
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

        string[] names = { "Model", "Engine", "Hardware", "Hotkeys" };
        _sections = new UIElement[4]
        {
            MakeModelSection(),
            MakeEngineSection(),
            MakeHardwareSection(),
            MakeHotkeysSection(),
        };
        _navItems = new Border[4];

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

        // S9: the models dropdown lists every GGUF in the models directory. No manual
        // path entry — dropping a file into the folder makes it appear here.
        _modelBox = new ThemedComboBox { Margin = new Thickness(0, 4, 0, 0) };
        _modelBox.SelectionChanged += (_, _) => TryAutoSave();
        panel.Children.Add(MakeLabel("Model"));
        panel.Children.Add(_modelBox);
        panel.Children.Add(MakeHint("Drop .gguf files into the models folder to add them; pick one here."));

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
        // S9: list every GGUF in the models directory; select the currently configured one.
        var manager = new ModelManager(ModelManager.DefaultModelsDir);
        foreach (var m in manager.ListModels())
            _modelBox.Items.Add(new ModelChoice(m, manager.GetPath(m)));

        // If the configured path is outside the directory (e.g. a custom location), keep it
        // selectable so saving doesn't silently switch models.
        string current = _settings.ModelPath;
        if (_modelBox.Items.Cast<ModelChoice>().All(c => c.Path != current))
        {
            var fileName = Path.GetFileNameWithoutExtension(current);
            _modelBox.Items.Add(new ModelChoice(
                new ModelInfo(fileName, fileName, "", 0, "", ""),
                current));
        }

        var selected = _modelBox.Items.Cast<ModelChoice>().FirstOrDefault(c => c.Path == current);
        _modelBox.SelectedItem = selected ?? _modelBox.Items.Cast<ModelChoice>().First();

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

        var choice = _modelBox.SelectedItem as ModelChoice;
        string newModelPath = choice?.Path ?? "";
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
