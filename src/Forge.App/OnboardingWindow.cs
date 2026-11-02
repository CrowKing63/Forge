using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forge.Core;

namespace Forge.App;

/// <summary>
/// First-run onboarding (S10), code-only WPF: when the configured model file is
/// missing, this window downloads it via <see cref="ModelManager"/> (S9) with a
/// progress bar. Downloads resume automatically from any existing <c>.part</c>
/// file, so closing the app mid-download and restarting continues where it left off.
///
/// When the path has no catalog source (a custom GGUF that was never downloaded),
/// the window offers to open Settings instead of downloading.
/// </summary>
public sealed class OnboardingWindow : Window
{
    /// <summary>True when the model file exists after this window's work.</summary>
    public bool Succeeded { get; private set; }

    private readonly string _modelPath;
    private readonly ModelManager _manager = new(ModelManager.DefaultModelsDir);
    private ProgressBar _bar = null!;
    private TextBlock _status = null!;
    private Button _actionButton = null!;
    private CancellationTokenSource? _cts;

    public OnboardingWindow(string modelPath)
    {
        _modelPath = modelPath;
        Title = "Forge — first run";
        Width = 460;
        Height = 230;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        BuildLayout();
    }

    /// <summary>
    /// Runs the download (if needed) and updates the UI. Returns when the file exists
    /// (<see cref="Succeeded"/>), the user cancels, or the download fails. Call on the
    /// UI thread; progress callbacks are marshalled back to it.
    /// </summary>
    public async Task RunDownloadAsync()
    {
        var model = _manager.ListModels().FirstOrDefault(m =>
            string.Equals(Path.GetFullPath(_manager.GetPath(m)), Path.GetFullPath(_modelPath), StringComparison.OrdinalIgnoreCase));

        if (model is null || string.IsNullOrEmpty(model.DownloadUrl))
        {
            // No catalog source for this path — a custom GGUF that was never downloaded.
            SetStatus("No model file found, and this path has no download source.");
            _actionButton.Content = "Open Settings";
            _actionButton.Click += (_, _) => new SettingsWindow().Show();
            return;
        }

        if (_manager.IsDownloaded(model))
        {
            Succeeded = true;
            SetStatus("Model file found — loading…");
            return;
        }

        _cts = new CancellationTokenSource();
        SetStatus($"Downloading {model.Name} ({model.SizeBytes / 1024 / 1024} MB)…");
        try
        {
            await _manager.DownloadAsync(model, progress: new Progress<DownloadProgress>(p => Dispatcher.Invoke(() => OnProgress(p))), _cts.Token);
            Succeeded = true;
            SetStatus("Download complete — loading model…");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Cancelled — you can retry from Settings.");
        }
        catch (Exception ex)
        {
            SetStatus($"Download failed: {ex.Message}");
        }
    }

    private void OnProgress(DownloadProgress p)
    {
        _bar.IsIndeterminate = p.TotalBytes <= 0;
        _bar.Value = Math.Clamp(p.Fraction * 100, 0, 100);
        SetStatus($"Downloading… {p.BytesReceived / 1024 / 1024} MB of {p.TotalBytes / 1024 / 1024} MB");
    }

    private void BuildLayout()
    {
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // heading
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // spacer
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // bar
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // status
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // button

        var heading = new TextBlock
        {
            Text = "Welcome to Forge",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
        };
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        _bar = new ProgressBar
        {
            Height = 6,
            Margin = new Thickness(0, 12, 0, 8),
        };
        Grid.SetRow(_bar, 2);
        root.Children.Add(_bar);

        _status = new TextBlock
        {
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(_status, 3);
        root.Children.Add(_status);

        _actionButton = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(12, 4, 12, 4),
            MinWidth = 90,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _actionButton.Click += (_, _) =>
        {
            _cts?.Cancel();
            Close();
        };
        Grid.SetRow(_actionButton, 4);
        root.Children.Add(_actionButton);

        Content = root;
    }

    private void SetStatus(string text) => _status.Text = text;
}
