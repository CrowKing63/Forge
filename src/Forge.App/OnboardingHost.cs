using Forge.Core;

namespace Forge.App;

/// <summary>The outcome of a background model download (S20).</summary>
public enum ModelDownloadOutcome
{
    Completed,
    Cancelled,
    Failed,
}

/// <summary>
/// The app-side services the onboarding wizard (S20) needs: model-download ownership,
/// hotkey re-registration, auto-start, and completion persistence. Implemented by
/// <see cref="App"/>; a stub implementation lets headless probes drive the wizard without
/// touching the tray, global hotkeys, or the registry.
/// </summary>
public interface IOnboardingHost
{
    /// <summary>The configured model file path.</summary>
    string ModelPath { get; }

    bool IsModelDownloadRunning { get; }

    /// <summary>Starts the background download of the configured model (no-op when already running, downloaded, or sourceless).</summary>
    void StartModelDownload();

    void CancelModelDownload();

    /// <summary>Raised on the UI thread as the download progresses.</summary>
    event Action<DownloadProgress>? DownloadProgress;

    /// <summary>Raised on the UI thread when the download ends (completed, cancelled, or failed).</summary>
    event Action<ModelDownloadOutcome>? DownloadFinished;

    /// <summary>Saves the binding and re-registers the global hotkey. Returns false when unparseable or in use.</summary>
    bool ApplyHotkey(string binding);

    bool IsAutoStartEnabled { get; }

    void SetAutoStart(bool enabled);

    /// <summary>Marks onboarding completed and persists it (called when the wizard closes).</summary>
    void FinishOnboarding();

    void OpenSettingsWindow();
}
