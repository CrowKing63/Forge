using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Forge.Core;

namespace Forge.App;

/// <summary>
/// Task-selection overlay (S6): a small borderless popup listing the four tasks,
/// keyboard-navigable (Up/Down to move, Enter to select, Esc to close). Built in
/// code only — no XAML (S5 convention).
/// </summary>
public sealed class TaskOverlayWindow : Window
{
    /// <summary>The four v1 tasks, in overlay order.</summary>
    public static readonly ForgeTask[] Tasks =
    {
        ForgeTask.TranslateKrToEn,
        ForgeTask.Rewrite,
        ForgeTask.Proofread,
        ForgeTask.Summarize,
    };

    private static readonly string[] TaskLabels =
    {
        "Translate KR → EN",
        "Rewrite",
        "Proofread",
        "Summarize",
    };

    private readonly Border[] _rows;
    private readonly TextBlock _status;
    private int _selectedIndex;
    private bool _processing;

    /// <summary>Raised on the UI thread when Enter selects a task.</summary>
    public event Action<ForgeTask>? TaskSelected;

    public TaskOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 280;
        SizeToContent = SizeToContent.Height;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x24));

        var root = new StackPanel { Margin = new Thickness(12) };
        _rows = new Border[Tasks.Length];
        for (var i = 0; i < Tasks.Length; i++)
            root.Children.Add(BuildRow(i));
        _status = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA5)),
            FontSize = 12,
            Text = "↑↓ select · Enter run · Esc close",
        };
        root.Children.Add(_status);
        Content = root;

        UpdateHighlight();
    }

    private Border BuildRow(int index)
    {
        var row = new Border
        {
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 2, 0, 2),
            CornerRadius = new CornerRadius(4),
            Child = new TextBlock
            {
                Text = TaskLabels[index],
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xEC)),
            },
        };
        _rows[index] = row;
        return row;
    }

    private void UpdateHighlight()
    {
        for (var i = 0; i < _rows.Length; i++)
            _rows[i].Background = i == _selectedIndex && !_processing
                ? new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6))
                : Brushes.Transparent;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (_processing)
            return;

        switch (e.Key)
        {
            case Key.Up:
                _selectedIndex = (_selectedIndex + Tasks.Length - 1) % Tasks.Length;
                UpdateHighlight();
                e.Handled = true;
                break;
            case Key.Down:
                _selectedIndex = (_selectedIndex + 1) % Tasks.Length;
                UpdateHighlight();
                e.Handled = true;
                break;
            case Key.Enter:
                TaskSelected?.Invoke(Tasks[_selectedIndex]);
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Disables navigation and shows a status line while a task runs.</summary>
    public void SetProcessing(string status)
    {
        _processing = true;
        _status.Text = status;
        UpdateHighlight();
    }

    /// <summary>Re-enables navigation after processing finishes or fails.</summary>
    public void SetIdle(string status)
    {
        _processing = false;
        _status.Text = status;
        UpdateHighlight();
    }
}
