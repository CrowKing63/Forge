using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forge.Core;

namespace Forge.App;

/// <summary>
/// Settings window (S7), code-only WPF. Tabs: Model / Engine / Hardware / Prompts / Hotkeys.
///
/// Persists to <see cref="ForgeSettings"/> (JSON under %LOCALAPPDATA%\Forge). The hotkey tab
/// is the source of truth the hotkey layer (S6) reads from; the model and hardware tabs are
/// the surfaces the model catalog (S9) and GPU detection (S8) bind to later — until then the
/// values are plain config, and the model "catalog" is a simple list placeholder.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly ForgeSettings _settings;
    private readonly Dictionary<ForgeTask, TextBox> _promptBoxes = new();

    private ComboBox _catalogBox = null!;
    private TextBox _modelPathBox = null!;
    private TextBox _contextBox = null!;
    private RadioButton _cpuRadio = null!;
    private RadioButton _gpuRadio = null!;
    private RadioButton _autoRadio = null!;
    private TextBox _popupBox = null!;
    private readonly Dictionary<ForgeTask, TextBox> _immediateBoxes = new();
    private TextBlock _status = null!;

    public SettingsWindow()
    {
        _settings = ForgeSettings.Load();
        Title = "Forge Settings";
        Width = 560;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        BuildTabs();
        PopulateFromSettings();
    }

    private void BuildTabs()
    {
        var root = new Grid { Margin = new Thickness(8) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var tabs = new TabControl();
        tabs.Items.Add(MakeModelTab());
        tabs.Items.Add(MakeEngineTab());
        tabs.Items.Add(MakeHardwareTab());
        tabs.Items.Add(MakePromptsTab());
        tabs.Items.Add(MakeHotkeysTab());
        Grid.SetRow(tabs, 0);
        root.Children.Add(tabs);

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        _status = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            Foreground = Brushes.DimGray,
        };
        bottom.Children.Add(_status);

        var saveButton = new Button
        {
            Content = "Save",
            Padding = new Thickness(16, 4, 16, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        saveButton.Click += OnSave;
        bottom.Children.Add(saveButton);

        Grid.SetRow(bottom, 1);
        root.Children.Add(bottom);

        Content = root;
    }

    private static TabItem MakeTab(string header, UIElement content) => new() { Header = header, Content = content };

    private TabItem MakeModelTab()
    {
        var panel = new StackPanel { Margin = new Thickness(8) };

        _catalogBox = new ComboBox { Margin = new Thickness(0, 4, 0, 12), MinWidth = 360 };
        _catalogBox.SelectionChanged += (_, _) =>
        {
            if (_catalogBox.SelectedItem is string path)
                _modelPathBox.Text = path;
        };
        panel.Children.Add(MakeLabel("Model catalog (placeholder — the real catalog lands in S9):"));
        panel.Children.Add(_catalogBox);

        _modelPathBox = new TextBox { Margin = new Thickness(0, 4, 0, 12), MinWidth = 360 };
        panel.Children.Add(MakeLabel("Model file path:"));
        panel.Children.Add(_modelPathBox);

        return MakeTab("Model", panel);
    }

    private TabItem MakeEngineTab()
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        _contextBox = new TextBox { Margin = new Thickness(0, 4, 0, 12), Width = 120 };
        panel.Children.Add(MakeLabel("Context size (tokens):"));
        panel.Children.Add(_contextBox);
        return MakeTab("Engine", panel);
    }

    private TabItem MakeHardwareTab()
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        _cpuRadio = MakeRadio("CPU");
        _gpuRadio = MakeRadio("GPU");
        _autoRadio = MakeRadio("Auto");
        panel.Children.Add(_cpuRadio);
        panel.Children.Add(_gpuRadio);
        panel.Children.Add(_autoRadio);
        panel.Children.Add(MakeLabel("Hardware detection (S8) will populate GPU options; until then this is the settings surface only."));
        return MakeTab("Hardware", panel);
    }

    private TabItem MakePromptsTab()
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        foreach (var task in Enum.GetValues<ForgeTask>())
        {
            var box = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 64,
                Margin = new Thickness(0, 4, 0, 12),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            panel.Children.Add(MakeLabel($"{task}:"));
            panel.Children.Add(box);
            _promptBoxes[task] = box;
        }
        return MakeTab("Prompts", panel);
    }

    private TabItem MakeHotkeysTab()
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        _popupBox = new TextBox { Margin = new Thickness(0, 4, 0, 12), Width = 160 };
        panel.Children.Add(MakeLabel("Open overlay (popup):"));
        panel.Children.Add(_popupBox);

        panel.Children.Add(MakeLabel("Immediate hotkeys (one task per binding; leave empty to unbind):"));
        foreach (var task in Enum.GetValues<ForgeTask>())
        {
            var box = new TextBox { Margin = new Thickness(0, 4, 0, 12), Width = 160 };
            panel.Children.Add(MakeLabel($"{task}:"));
            panel.Children.Add(box);
            _immediateBoxes[task] = box;
        }
        panel.Children.Add(MakeLabel("These bindings are written to the hotkey config file that the hotkey layer (S6) reads."));
        return MakeTab("Hotkeys", panel);
    }

    private static TextBlock MakeLabel(string text) => new()
    {
        Text = text,
        Margin = new Thickness(0, 4, 0, 2),
    };

    private static RadioButton MakeRadio(string content) => new()
    {
        Content = content,
        Margin = new Thickness(0, 2, 0, 2),
    };

    private void PopulateFromSettings()
    {
        _catalogBox.Items.Add(_settings.ModelPath);
        if (_settings.ModelPath != ForgeSettings.DefaultModelPath)
            _catalogBox.Items.Add(ForgeSettings.DefaultModelPath);
        _catalogBox.SelectedIndex = 0;
        _modelPathBox.Text = _settings.ModelPath;
        _contextBox.Text = _settings.ContextSize.ToString();

        _cpuRadio.IsChecked = _settings.Hardware == HardwareMode.Cpu;
        _gpuRadio.IsChecked = _settings.Hardware == HardwareMode.Gpu;
        _autoRadio.IsChecked = _settings.Hardware == HardwareMode.Auto;

        foreach (var task in Enum.GetValues<ForgeTask>())
            _promptBoxes[task].Text = _settings.Prompts.TryGetValue(task.ToString(), out var prompt)
                ? prompt
                : PromptBuilder.SystemPrompt(task);

        // Hotkeys come from the config file the hotkey layer (S6) reads.
        var (popup, immediate) = HotkeyConfig.Load();
        _popupBox.Text = popup;
        foreach (var h in immediate)
            if (_immediateBoxes.TryGetValue(h.Task, out var box))
                box.Text = HotkeyText(h.Binding);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(_contextBox.Text, out int contextSize) || contextSize <= 0)
        {
            SetStatus("Context size must be a positive number.");
            return;
        }

        _settings.ModelPath = _modelPathBox.Text.Trim();
        _settings.ContextSize = contextSize;
        _settings.Hardware = _gpuRadio.IsChecked == true ? HardwareMode.Gpu
            : _autoRadio.IsChecked == true ? HardwareMode.Auto
            : HardwareMode.Cpu;
        foreach (var (task, box) in _promptBoxes)
            _settings.Prompts[task.ToString()] = box.Text;

        string popup = _popupBox.Text.Trim();
        if (!HotkeyBinding.TryParse(popup, out _))
        {
            SetStatus("Invalid overlay hotkey (expected e.g. \"Ctrl+E\").");
            return;
        }
        var immediate = new List<ImmediateHotkey>();
        foreach (var (task, box) in _immediateBoxes)
        {
            string text = box.Text.Trim();
            if (text.Length == 0) continue;
            if (!HotkeyBinding.TryParse(text, out var binding))
            {
                SetStatus($"Invalid hotkey for {task} (expected e.g. \"Ctrl+Shift+P\").");
                return;
            }
            immediate.Add(new ImmediateHotkey(task, binding));
        }

        try
        {
            _settings.Save();
            SaveHotkeys(popup, immediate);
            SetStatus("Saved.");
        }
        catch (Exception ex)
        {
            SetStatus($"Save failed: {ex.Message}");
        }
    }

    /// <summary>Writes the hotkey config file that the hotkey layer (S6) reads.</summary>
    private static void SaveHotkeys(string popup, List<ImmediateHotkey> immediate)
    {
        var payload = new
        {
            popup,
            immediate = immediate.Select(h => new { task = TaskName(h.Task), hotkey = HotkeyText(h.Binding) }),
        };
        var dir = Path.GetDirectoryName(HotkeyConfig.ConfigPath);
        if (dir is not null) Directory.CreateDirectory(dir);
        File.WriteAllText(HotkeyConfig.ConfigPath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string TaskName(ForgeTask task) => task switch
    {
        ForgeTask.Proofread => "proofread",
        ForgeTask.TranslateKrToEn => "translate",
        ForgeTask.Rewrite => "rewrite",
        _ => "summarize",
    };

    /// <summary>Formats a parsed binding back into its "Ctrl+Shift+E" display form.</summary>
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
}
