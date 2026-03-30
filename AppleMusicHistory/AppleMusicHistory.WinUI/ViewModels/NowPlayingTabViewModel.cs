using System.Threading.Tasks;
using AppleMusicHistory.Core.Models;
using AppleMusicHistory.Host;
using AppleMusicHistory.WinUI.Commands;

namespace AppleMusicHistory.WinUI.ViewModels;

public sealed class NowPlayingTabViewModel : ViewModelBase
{
    private TrackerApplicationHost? _host;
    private DashboardState _currentState = DashboardState.CreateDefault(string.Empty, true, true, false);
    private string? _currentAudioBadgeAssetUri;

    public NowPlayingTabViewModel()
    {
        PauseResumeCommand = new AsyncRelayCommand(ToggleTrackingAsync, () => _host is not null);
        OpenDatabaseFolderCommand = new RelayCommand(() => _host?.OpenDatabaseFolder(), () => _host is not null);
    }

    public DashboardState CurrentState
    {
        get => _currentState;
        private set => SetField(ref _currentState, value);
    }

    public string? CurrentAudioBadgeAssetUri
    {
        get => _currentAudioBadgeAssetUri;
        private set => SetField(ref _currentAudioBadgeAssetUri, value);
    }

    public AsyncRelayCommand PauseResumeCommand { get; }

    public RelayCommand OpenDatabaseFolderCommand { get; }

    public bool HasSongUrl => !string.IsNullOrWhiteSpace(CurrentState.CurrentSongUrl);

    public bool HasAlbumUrl => !string.IsNullOrWhiteSpace(CurrentState.CurrentAlbumUrl);

    public bool HasArtistUrl => !string.IsNullOrWhiteSpace(CurrentState.CurrentArtistUrl);

    public bool HasDiagnosticMessage => !string.IsNullOrWhiteSpace(CurrentState.SourceDiagnosticMessage);

    public bool HasAudioBadge => !string.IsNullOrWhiteSpace(CurrentAudioBadgeAssetUri);

    public string PauseResumeLabel => CurrentState.IsTrackingPaused ? "Resume Tracking" : "Pause Tracking";

    public string MetadataEnrichmentLabel => CurrentState.MetadataEnrichmentEnabled ? "Enabled" : "Disabled";

    public void AttachHost(TrackerApplicationHost host)
    {
        _host = host;
        ApplyState(host.CurrentState);
        PauseResumeCommand.NotifyCanExecuteChanged();
        OpenDatabaseFolderCommand.NotifyCanExecuteChanged();
    }

    public void ApplyState(DashboardState state)
    {
        CurrentState = state;
        CurrentAudioBadgeAssetUri = AudioBadgeAssetResolver.ResolveAssetUri(state.CurrentAudioVariant);

        OnPropertyChanged(nameof(HasSongUrl));
        OnPropertyChanged(nameof(HasAlbumUrl));
        OnPropertyChanged(nameof(HasArtistUrl));
        OnPropertyChanged(nameof(HasDiagnosticMessage));
        OnPropertyChanged(nameof(HasAudioBadge));
        OnPropertyChanged(nameof(PauseResumeLabel));
        OnPropertyChanged(nameof(MetadataEnrichmentLabel));
        PauseResumeCommand.NotifyCanExecuteChanged();
    }

    public async Task ExportAsync(ExportKind exportKind)
    {
        if (_host is null)
        {
            return;
        }

        await _host.ExportAsync(exportKind).ConfigureAwait(false);
    }

    public async Task UpdateLaunchAtStartupAsync(bool enabled)
    {
        if (_host is null)
        {
            return;
        }

        await _host.UpdateLaunchAtStartupAsync(enabled).ConfigureAwait(false);
    }

    public void OpenSongUrl() => UrlLauncher.TryOpen(CurrentState.CurrentSongUrl);

    public void OpenAlbumUrl() => UrlLauncher.TryOpen(CurrentState.CurrentAlbumUrl);

    public void OpenArtistUrl() => UrlLauncher.TryOpen(CurrentState.CurrentArtistUrl);

    private async Task ToggleTrackingAsync()
    {
        if (_host is null)
        {
            return;
        }

        await _host.SetTrackingPausedAsync(!CurrentState.IsTrackingPaused).ConfigureAwait(false);
    }
}
