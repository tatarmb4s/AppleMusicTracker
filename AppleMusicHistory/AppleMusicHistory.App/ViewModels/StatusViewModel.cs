using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AppleMusicHistory.App.ViewModels;

public sealed class StatusViewModel : INotifyPropertyChanged
{
    private string _appleMusicState = "Starting";
    private string _currentTrack = "No current track";
    private string _activeSession = "No active session";
    private string _statistics = "Tracks: 0 | Sessions: 0 | Open: 0";
    private string _lastObserved = "Never";
    private string _databasePath = string.Empty;
    private string _currentAudioFormat = "Standard / unknown";
    private bool _isTrackingPaused;
    private bool _launchAtStartup;
    private bool _metadataEnrichmentEnabled;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string AppleMusicState
    {
        get => _appleMusicState;
        set => SetField(ref _appleMusicState, value);
    }

    public string CurrentTrack
    {
        get => _currentTrack;
        set => SetField(ref _currentTrack, value);
    }

    public string ActiveSession
    {
        get => _activeSession;
        set => SetField(ref _activeSession, value);
    }

    public string Statistics
    {
        get => _statistics;
        set => SetField(ref _statistics, value);
    }

    public string LastObserved
    {
        get => _lastObserved;
        set => SetField(ref _lastObserved, value);
    }

    public string DatabasePath
    {
        get => _databasePath;
        set => SetField(ref _databasePath, value);
    }

    public string CurrentAudioFormat
    {
        get => _currentAudioFormat;
        set => SetField(ref _currentAudioFormat, value);
    }

    public bool IsTrackingPaused
    {
        get => _isTrackingPaused;
        set => SetField(ref _isTrackingPaused, value);
    }

    public bool LaunchAtStartup
    {
        get => _launchAtStartup;
        set => SetField(ref _launchAtStartup, value);
    }

    public bool MetadataEnrichmentEnabled
    {
        get => _metadataEnrichmentEnabled;
        set => SetField(ref _metadataEnrichmentEnabled, value);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
