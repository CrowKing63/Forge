using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Forge.Core;

namespace Forge.App;

/// <summary>
/// Result editor window (S7), code-only WPF: split view of original vs. result with diff
/// highlighting, an editable result pane, and a [Replace][Copy][Regenerate][Cancel] bar.
///
/// Streaming → completion: raw tokens stream into the result pane while generating; on
/// completion both panes are re-rendered as a diff (DiffRenderer) — inserted text is
/// highlighted in the result pane, removed text in the original pane.
///
/// Replace and Regenerate are exposed as events: the replace action itself is S11's domain
/// (S11 subscribes to <see cref="ReplaceRequested"/>), and the pipeline re-run is wired by
/// the app shell (<see cref="RegenerateRequested"/>). Cancel simply closes the window —
/// the original text was never mutated, so it is kept as-is.
/// </summary>
public sealed class ResultEditorWindow : Window
{
    /// <summary>Raised when the user picks Replace; carries the final result text (S11 subscribes).</summary>
    public event Action<string>? ReplaceRequested;

    /// <summary>Raised when the user picks Regenerate; the app re-runs the S4 pipeline.</summary>
    public event Action? RegenerateRequested;

    private static readonly FontFamily MonoFont = new("Consolas");
    private static readonly Brush InsertBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xE6, 0xC9)); // light green
    private static readonly Brush RemoveBrush = new SolidColorBrush(Color.FromRgb(0xF8, 0xD7, 0xDA)); // light red

    private readonly string _originalText;
    private RichTextBox _originalBox = null!;
    private RichTextBox _resultBox = null!;
    private TextBlock _status = null!;
    private Button _replaceButton = null!;
    private Button _copyButton = null!;
    private Button _regenerateButton = null!;
    private bool _streaming;

    public ResultEditorWindow(string originalText, ForgeTask task)
    {
        _originalText = originalText;
        Title = $"Forge — {task}";
        Width = 900;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        BuildLayout();
        _originalBox.Document = NewDocument(originalText);
        BeginStreaming();
    }

    /// <summary>Starts a fresh streaming run: clears the result pane and disables the action buttons.</summary>
    public void BeginStreaming()
    {
        _streaming = true;
        _resultBox.Document = NewDocument(string.Empty);
        SetStatus("Generating…");
        SetActionButtonsEnabled(false);
    }

    /// <summary>Appends one streamed token to the raw result pane (call on the UI thread).</summary>
    public void AppendToken(string token)
    {
        if (!_streaming || string.IsNullOrEmpty(token)) return;
        var paragraph = (Paragraph)_resultBox.Document.Blocks.First();
        var lastRun = paragraph.Inlines.OfType<Run>().Last();
        lastRun.Text += token;
        _resultBox.ScrollToEnd();
    }

    /// <summary>Completes the run: renders the diff in both panes and enables the button bar.</summary>
    public void Complete(ParsedResult result)
    {
        _streaming = false;
        var segments = DiffRenderer.Compute(_originalText, result.Text);
        _resultBox.Document = BuildDiffDocument(segments, resultPane: true);
        _originalBox.Document = BuildDiffDocument(segments, resultPane: false);
        SetStatus(result.HasError || result.LanguageMismatch
            ? "Done — review the result (warning flags set)"
            : "Done");
        SetActionButtonsEnabled(true);
    }

    /// <summary>The current result text (what Copy and Replace act on).</summary>
    public string ResultText
    {
        get
        {
            var range = new TextRange(_resultBox.Document.ContentStart, _resultBox.Document.ContentEnd);
            return range.Text;
        }
    }

    private void BuildLayout()
    {
        var root = new Grid { Margin = new Thickness(8) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // status
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // panes
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // buttons

        _status = new TextBlock { Margin = new Thickness(0, 0, 0, 8), Foreground = Brushes.DimGray };
        Grid.SetRow(_status, 0);
        root.Children.Add(_status);

        var panes = new Grid();
        panes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panes.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _originalBox = MakeTextBox(readOnly: true);
        var originalPane = MakePane("Original", _originalBox);
        Grid.SetColumn(originalPane, 0);
        panes.Children.Add(originalPane);

        var splitter = new GridSplitter { Width = 4 };
        Grid.SetColumn(splitter, 1);
        panes.Children.Add(splitter);

        _resultBox = MakeTextBox(readOnly: false);
        var resultPane = MakePane("Result", _resultBox);
        Grid.SetColumn(resultPane, 2);
        panes.Children.Add(resultPane);

        Grid.SetRow(panes, 1);
        root.Children.Add(panes);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        _replaceButton = AddButton(buttons, "Replace", (_, _) => ReplaceRequested?.Invoke(ResultText));
        _copyButton = AddButton(buttons, "Copy", OnCopy);
        _regenerateButton = AddButton(buttons, "Regenerate", (_, _) => RegenerateRequested?.Invoke());
        AddButton(buttons, "Cancel", (_, _) => Close());
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
    }

    private static DockPanel MakePane(string header, RichTextBox box)
    {
        var pane = new DockPanel();
        var label = new TextBlock
        {
            Text = header,
            Margin = new Thickness(2, 0, 2, 4),
            Foreground = Brushes.DimGray,
        };
        DockPanel.SetDock(label, Dock.Top);
        pane.Children.Add(label);
        pane.Children.Add(box);
        return pane;
    }

    private static RichTextBox MakeTextBox(bool readOnly) => new()
    {
        IsReadOnly = readOnly,
        AcceptsReturn = true,
        FontFamily = MonoFont,
        FontSize = 14,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    private static Button AddButton(StackPanel parent, string text, RoutedEventHandler onClick)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(12, 4, 12, 4),
            MinWidth = 90,
        };
        button.Click += onClick;
        parent.Children.Add(button);
        return button;
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        // Copy goes to the clipboard immediately (S7).
        Clipboard.SetText(ResultText);
        SetStatus("Copied to clipboard");
    }

    private void SetActionButtonsEnabled(bool enabled)
    {
        _replaceButton.IsEnabled = enabled;
        _copyButton.IsEnabled = enabled;
        _regenerateButton.IsEnabled = enabled;
    }

    private void SetStatus(string text) => _status.Text = text;

    /// <summary>Plain (unhighlighted) document with the given text.</summary>
    private static FlowDocument NewDocument(string text)
    {
        var doc = new FlowDocument { FontFamily = MonoFont, FontSize = 14 };
        var paragraph = new Paragraph();
        AppendText(paragraph, text, null, null);
        doc.Blocks.Add(paragraph);
        return doc;
    }

    /// <summary>
    /// Diff-highlighted document: the result pane highlights inserted text, the original
    /// pane highlights removed text (strikethrough).
    /// </summary>
    private static FlowDocument BuildDiffDocument(IReadOnlyList<DiffSegment> segments, bool resultPane)
    {
        var doc = new FlowDocument { FontFamily = MonoFont, FontSize = 14 };

        var paragraph = new Paragraph();
        foreach (var segment in segments)
        {
            string text = resultPane ? segment.ResultText : segment.OriginalText;
            if (string.IsNullOrEmpty(text)) continue;

            Brush? background = null;
            TextDecorationCollection? decorations = null;
            if (resultPane && segment.Kind == DiffSegmentKind.Inserted)
                background = InsertBrush;
            else if (!resultPane && segment.Kind == DiffSegmentKind.Removed)
            {
                background = RemoveBrush;
                decorations = new TextDecorationCollection { TextDecorations.Strikethrough };
            }

            AppendText(paragraph, text, background, decorations);
        }
        doc.Blocks.Add(paragraph);
        return doc;
    }

    /// <summary>Appends <paramref name="text"/> to the paragraph, one run per line.</summary>
    private static void AppendText(Paragraph paragraph, string text, Brush? background, TextDecorationCollection? decorations)
    {
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) paragraph.Inlines.Add(new LineBreak());
            var run = new Run(lines[i]);
            if (background is not null) run.Background = background;
            if (decorations is not null) run.TextDecorations = decorations;
            paragraph.Inlines.Add(run);
        }
    }
}
