using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AppleMusicHistory.App.ViewModels;

public sealed class StatusViewModel : INotifyPropertyChanged
{
    private string _appleMusicState = "Starting";
    private string _currentTrack = "No current track";
    private string _currentTitle = "No current track";
    private string _currentArtist = string.Empty;
    private string _currentAlbum = string.Empty;
    private string _currentComposer = string.Empty;
    private string _currentReleaseDate = string.Empty;
    private string _currentGenres = string.Empty;
    private string _currentTrackNumbers = string.Empty;
    private string _currentIsrc = string.Empty;
    private string _currentSongUrl = string.Empty;
    private string _currentAlbumUrl = string.Empty;
    private string _currentArtistUrl = string.Empty;
    private string _currentArtworkPathOrUrl = string.Empty;
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

    public string CurrentTitle
    {
        get => _currentTitle;
        set => SetField(ref _currentTitle, value);
    }

    public string CurrentArtist
    {
        get => _currentArtist;
        set => SetField(ref _currentArtist, value);
    }

    public string CurrentAlbum
    {
        get => _currentAlbum;
        set => SetField(ref _currentAlbum, value);
    }

    public string CurrentComposer
    {
        get => _currentComposer;
        set => SetField(ref _currentComposer, value);
    }

    public string CurrentReleaseDate
    {
        get => _currentReleaseDate;
        set => SetField(ref _currentReleaseDate, value);
    }

    public string CurrentGenres
    {
        get => _currentGenres;
        set => SetField(ref _currentGenres, value);
    }

    public string CurrentTrackNumbers
    {
        get => _currentTrackNumbers;
        set => SetField(ref _currentTrackNumbers, value);
    }

    public string CurrentIsrc
    {
        get => _currentIsrc;
        set => SetField(ref _currentIsrc, value);
    }

    public string CurrentSongUrl
    {
        get => _currentSongUrl;
        set => SetField(ref _currentSongUrl, value);
    }

    public string CurrentAlbumUrl
    {
        get => _currentAlbumUrl;
        set => SetField(ref _currentAlbumUrl, value);
    }

    public string CurrentArtistUrl
    {
        get => _currentArtistUrl;
        set => SetField(ref _currentArtistUrl, value);
    }

    public string CurrentArtworkPathOrUrl
    {
        get => _currentArtworkPathOrUrl;
        set => SetField(ref _currentArtworkPathOrUrl, value);
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
