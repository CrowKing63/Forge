using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forge.Core;

namespace Forge.App;

/// <summary>
/// S19 Settings window — a single scrollable page with all settings.
/// Model / Engine / Hotkeys / General, no sidebar.
/// </summary>
public partial class SettingsWindow : Window
{
    private static readonly Brush BgBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x34));
    private static readonly Brush CardBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x20, 0x26));
    private static readonly Brush BorderClr = new SolidColorBrush(Color.FromRgb(0x3A, 0x3D, 0x44));
    private static readonly Brush TextClr = new SolidColorBrush(Color.FromRgb(0xE6, 0xE8, 0xEC));
    private static readonly Brush MutedClr = new SolidColorBrush(Color.FromRgb(0x9A, 0x9E, 0xA6));

    private ForgeSettings _settings;
    private bool _busy;

    private TextBlock _modelStatusText = null!;
    private TextBox _modelPathBox = null!;
    private ComboBox _contextCombo = null!;
    private ComboBox _unloadCombo = null!;
    private CheckBox _autoStartCheck = null!;

    public SettingsWindow()
    {
        _settings = ForgeSettings.Load();
        BuildUi();
    }

    private void BuildUi()
    {
        Title = "Forge — Settings";
        Width = 480;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = BgBrush;

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = "Settings",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextClr,
            Margin = new Thickness(0, 0, 0, 16),
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        scroll.Content = stack;

        // ---- Model ----
        stack.Children.Add(SectionHeader("Model"));
        var modelCard = Card(out var modelSp);
        _modelStatusText = StatusRow(modelSp, "No model configured.");
        _modelPathBox = PathField(modelSp);
        _modelPathBox.Text = _settings.ModelPath;
        if (!string.IsNullOrWhiteSpace(_settings.ModelPath))
            UpdateModelStatus(_settings.ModelPath);
        modelSp.Children.Add(Btn("Browse…", BrowseForModel));
        stack.Children.Add(modelCard);

        // ---- Engine ----
        stack.Children.Add(SectionHeader("Engine"));
        var engineCard = Card(out var engineSp);
        LabelRow(engineSp, "Context size");
        _contextCombo = Combo(engineSp, new[] { "2048", "4096", "8192", "16384" });
        _contextCombo.SelectedItem = _settings.ContextSize.ToString();
        LabelRow(engineSp, "Unload after");
        _unloadCombo = Combo(engineSp, new[]
        {
            "After each translation",
            "1 minute",
            "5 minutes",
            "Never",
        });
        _unloadCombo.SelectedItem = UnloadLabel(_settings.UnloadAfter);
        stack.Children.Add(engineCard);

        // ---- Hotkeys ----
        stack.Children.Add(SectionHeader("Hotkeys"));
        var hotkeyCard = Card(out var hkSp);
        LabelRow(hkSp, "Translate");
        var hkText = new TextBlock
        {
            Text = HotkeyConfig.Load(),
            FontSize = 14,
            Foreground = TextClr,
            Margin = new Thickness(0, 4, 0, 8),
        };
        hkSp.Children.Add(hkText);
        stack.Children.Add(hotkeyCard);

        // ---- General ----
        stack.Children.Add(SectionHeader("General"));
        var generalCard = Card(out var genSp);
        _autoStartCheck = new CheckBox
        {
            Content = "Start Forge when I sign in to Windows",
            Foreground = TextClr,
            FontSize = 14,
            IsChecked = AutoStartManager.IsEnabled(),
            Margin = new Thickness(0, 0, 0, 8),
        };
        genSp.Children.Add(_autoStartCheck);
        stack.Children.Add(generalCard);

        var saveBtn = Btn("Save", SaveAndClose);
        saveBtn.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(saveBtn, 2);
        root.Children.Add(saveBtn);

        Content = root;
    }

    private void BrowseForModel()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select a GGUF model",
            Filter = "GGUF models (*.gguf)|*.gguf",
        };
        if (dlg.ShowDialog(this) == true)
        {
            _modelPathBox.Text = dlg.FileName;
            UpdateModelStatus(dlg.FileName);
        }
    }

    private void SaveAndClose()
    {
        if (_busy) return;
        _busy = true;

        _settings.ModelPath = string.IsNullOrWhiteSpace(_modelPathBox.Text) ? "" : _modelPathBox.Text.Trim();
        if (int.TryParse(_contextCombo.SelectedItem?.ToString(), out int ctx))
            _settings.ContextSize = ctx;
        _settings.UnloadAfter = UnloadValue(_unloadCombo.SelectedItem?.ToString());

        try
        {
            AutoStartManager.SetEnabled(_autoStartCheck.IsChecked == true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not update auto-start: {ex.Message}", "Forge",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _settings.Save();
        DialogResult = true;
        Close();
    }

    private void UpdateModelStatus(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _modelStatusText.Text = "No model configured.";
            _modelStatusText.Foreground = MutedClr;
            return;
        }
        if (!File.Exists(path))
        {
            _modelStatusText.Text = "Model file not found.";
            _modelStatusText.Foreground = Brushes.Red;
            return;
        }

        var certified = ModelCatalog.CertifiedModel.Match(path);
        if (certified is not null)
        {
            _modelStatusText.Text = $"✓ Certified — {certified.NamePattern}";
            _modelStatusText.Foreground = Brushes.Green;
        }
        else
        {
            _modelStatusText.Text = "Unauthenticated model — use at your own risk.";
            _modelStatusText.Foreground = Brushes.Orange;
        }
    }

    // ---- UI helpers ----

    private static Border Card(out StackPanel sp)
    {
        var border = new Border
        {
            Background = CardBrush,
            BorderBrush = BorderClr,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12),
        };
        sp = new StackPanel();
        border.Child = sp;
        return border;
    }

    private static TextBlock SectionHeader(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = MutedClr,
        Margin = new Thickness(0, 8, 0, 6),
    };

    private static void LabelRow(StackPanel sp, string label)
    {
        var tb = new TextBlock
        {
            Text = label,
            FontSize = 14,
            Foreground = TextClr,
            Margin = new Thickness(0, 8, 0, 2),
        };
        sp.Children.Add(tb);
    }

    private static ComboBox Combo(StackPanel sp, string[] items)
    {
        var combo = new ComboBox
        {
            Width = 160,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 2, 0, 8),
        };
        foreach (var item in items)
            combo.Items.Add(item);
        sp.Children.Add(combo);
        return combo;
    }

    private static TextBlock StatusRow(StackPanel sp, string text)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 13,
            Foreground = MutedClr,
            Margin = new Thickness(0, 0, 0, 8),
        };
        sp.Children.Add(tb);
        return tb;
    }

    private static TextBox PathField(StackPanel sp)
    {
        var box = new TextBox
        {
            FontSize = 13,
            Foreground = TextClr,
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x29, 0x30)),
            BorderBrush = BorderClr,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 8),
        };
        sp.Children.Add(box);
        return box;
    }

    private static Button Btn(string text, Action onClick)
    {
        var btn = new Button
        {
            Content = text,
            FontSize = 13,
            Foreground = TextClr,
            Background = new SolidColorBrush(Color.FromRgb(0x32, 0x35, 0x3D)),
            BorderBrush = BorderClr,
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 4, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private static string UnloadLabel(ModelUnloadAfter v) => v switch
    {
        ModelUnloadAfter.Immediate => "After each translation",
        ModelUnloadAfter.OneMinute => "1 minute",
        ModelUnloadAfter.FiveMinutes => "5 minutes",
        ModelUnloadAfter.None => "Never",
        _ => "1 minute",
    };

    private static ModelUnloadAfter UnloadValue(string? label) => label switch
    {
        "After each translation" => ModelUnloadAfter.Immediate,
        "1 minute" => ModelUnloadAfter.OneMinute,
        "5 minutes" => ModelUnloadAfter.FiveMinutes,
        "Never" => ModelUnloadAfter.None,
        _ => ModelUnloadAfter.OneMinute,
    };
}
