using AppleMusicHistory.WinUI.Commands;

namespace AppleMusicHistory.WinUI.ViewModels;

public enum MainTabId
{
    NowPlaying = 1,
    TrackHistory = 2
}

public sealed class MainTabControllerViewModel : ViewModelBase
{
    private MainTabId _selectedTabId = MainTabId.NowPlaying;

    public MainTabControllerViewModel()
    {
        SelectNowPlayingCommand = new RelayCommand(() => SelectedTabId = MainTabId.NowPlaying, () => SelectedTabId != MainTabId.NowPlaying);
        SelectTrackHistoryCommand = new RelayCommand(() => SelectedTabId = MainTabId.TrackHistory, () => SelectedTabId != MainTabId.TrackHistory);
    }

    public MainTabId SelectedTabId
    {
        get => _selectedTabId;
        set
        {
            if (!SetField(ref _selectedTabId, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsNowPlayingSelected));
            OnPropertyChanged(nameof(IsTrackHistorySelected));
            SelectNowPlayingCommand.NotifyCanExecuteChanged();
            SelectTrackHistoryCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsNowPlayingSelected => SelectedTabId == MainTabId.NowPlaying;

    public bool IsTrackHistorySelected => SelectedTabId == MainTabId.TrackHistory;

    public RelayCommand SelectNowPlayingCommand { get; }

    public RelayCommand SelectTrackHistoryCommand { get; }
}
