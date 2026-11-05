using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forge.Core;

namespace Forge.App;

/// <summary>
/// First-run onboarding (S10), code-only WPF: when the configured model file is
/// missing, this window asks whether to download it, then downloads via
/// <see cref="ModelManager"/> (S9) with a progress bar. Downloads resume automatically
/// from any existing <c>.part</c> file, so closing the app mid-download and restarting
/// continues where it left off.
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
    private Button _downloadButton = null!;
    private Button _cancelButton = null!;
    private CancellationTokenSource? _cts;
    private TaskCompletionSource<bool>? _choice; // non-null while awaiting the user's choice
    private bool _openSettingsOnDownload;

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
    /// Asks for consent, then runs the download (if needed) and updates the UI.
    /// Returns when the file exists (<see cref="Succeeded"/>), the user cancels, or the
    /// download fails. Call on the UI thread; progress callbacks are marshalled back to it.
    /// </summary>
    public async Task RunDownloadAsync()
    {
        var model = _manager.ListModels().FirstOrDefault(m =>
            string.Equals(Path.GetFullPath(_manager.GetPath(m)), Path.GetFullPath(_modelPath), StringComparison.OrdinalIgnoreCase));

        if (model is null || string.IsNullOrEmpty(model.DownloadUrl))
        {
            // No catalog source for this path — a custom GGUF that was never downloaded.
            SetStatus("No model file found, and this path has no download source.");
            _downloadButton.Content = "Open Settings";
            _cancelButton.Visibility = Visibility.Collapsed;
            _openSettingsOnDownload = true;
            _cts = new CancellationTokenSource();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, _cts.Token); // stay open until the user acts
            }
            catch (OperationCanceledException)
            {
                // Window closed.
            }
            return;
        }

        if (_manager.IsDownloaded(model))
        {
            Succeeded = true;
            SetStatus("Model file found — loading…");
            HideButtons();
            return;
        }

        // Ask before downloading: a multi-GB download should not start uninvited.
        _choice = new TaskCompletionSource<bool>();
        SetStatus($"Do you want to download {model.Name} ({model.SizeBytes / 1024 / 1024} MB)?");
        _downloadButton.Content = "Download";
        var confirmed = await _choice.Task;
        _choice = null;
        if (!confirmed)
        {
            SetStatus("Cancelled.");
            HideButtons();
            return;
        }

        _cts = new CancellationTokenSource();
        SetStatus($"Downloading {model.Name} ({model.SizeBytes / 1024 / 1024} MB)…");
        _downloadButton.IsEnabled = false;
        try
        {
            await _manager.DownloadAsync(model, progress: new Progress<DownloadProgress>(p => Dispatcher.Invoke(() => OnProgress(p))), _cts.Token);
            Succeeded = true;
            SetStatus("Download complete — loading model…");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Cancelled — restarting Forge resumes the download.");
        }
        catch (Exception ex)
        {
            SetStatus($"Download failed: {ex.Message}");
        }
        finally
        {
            HideButtons();
        }
    }

    private void OnProgress(DownloadProgress p)
    {
        _bar.IsIndeterminate = p.TotalBytes <= 0;
        _bar.Value = Math.Clamp(p.Fraction * 100, 0, 100);
        SetStatus($"Downloading… {p.BytesReceived / 1024 / 1024} MB of {p.TotalBytes / 1024 / 1024} MB");
    }

    private void HideButtons()
    {
        _downloadButton.Visibility = Visibility.Collapsed;
        _cancelButton.Visibility = Visibility.Collapsed;
    }

    private void OnDownloadClicked(object sender, RoutedEventArgs e)
    {
        if (_choice is not null)
        {
            _choice.TrySetResult(true);
            return;
        }
        if (_openSettingsOnDownload)
        {
            new SettingsWindow().Show();
            Close();
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        if (_choice is not null)
        {
            _choice.TrySetResult(false);
            return;
        }
        _cts?.Cancel();
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // Closing the window (X) counts as a cancel: settle any pending choice and
        // stop an in-flight download.
        _choice?.TrySetResult(false);
        _cts?.Cancel();
    }

    private void BuildLayout()
    {
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // heading
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // spacer
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // bar
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // status
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // buttons

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

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _downloadButton = new Button
        {
            Content = "Download",
            Padding = new Thickness(12, 4, 12, 4),
            MinWidth = 90,
            Margin = new Thickness(8, 0, 0, 0),
        };
        _downloadButton.Click += OnDownloadClicked;
        _cancelButton = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(12, 4, 12, 4),
            MinWidth = 90,
        };
        _cancelButton.Click += OnCancelClicked;
        buttons.Children.Add(_downloadButton);
        buttons.Children.Add(_cancelButton);
        Grid.SetRow(buttons, 4);
        root.Children.Add(buttons);

        Content = root;
        Closed += OnClosed;
    }

    private void SetStatus(string text) => _status.Text = text;
}
