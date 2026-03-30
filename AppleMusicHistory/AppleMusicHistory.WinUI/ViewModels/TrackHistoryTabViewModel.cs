using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AppleMusicHistory.Core.Models;
using AppleMusicHistory.Host;
using AppleMusicHistory.WinUI.Commands;
using Microsoft.UI.Dispatching;

namespace AppleMusicHistory.WinUI.ViewModels;

public enum TrackHistoryColumnId
{
    TrackId = 1,
    Title = 2,
    Artist = 3,
    Album = 4,
    Subtitle = 5,
    CatalogAudioVariantsJson = 6,
    LastObservedAudioBadgeRaw = 7,
    LastObservedAudioVariant = 8
}

public sealed class TrackHistoryTabViewModel : ViewModelBase
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<TrackHistoryRow>>>? _initialHistoryLoader;
    private Func<CancellationToken, Task<IReadOnlyList<TrackHistoryRow>>>? _historyLoader;
    private List<TrackHistoryRowViewModel> _allRows = [];
    private CancellationTokenSource? _filterDebounceCts;
    private CancellationTokenSource? _refreshDebounceCts;
    private DispatcherQueue? _dispatcherQueue;
    private bool _isLoading;
    private bool _hasLoadedOnce;
    private bool _isDirty;
    private bool _isActive;
    private string _errorMessage = string.Empty;
    private string _trackIdFilter = string.Empty;
    private string _titleFilter = string.Empty;
    private string _artistFilter = string.Empty;
    private string _albumFilter = string.Empty;
    private string _subtitleFilter = string.Empty;
    private string _catalogAudioVariantsJsonFilter = string.Empty;
    private string _lastObservedAudioBadgeRawFilter = string.Empty;
    private string _lastObservedAudioVariantFilter = string.Empty;

    public TrackHistoryTabViewModel(Func<CancellationToken, Task<IReadOnlyList<TrackHistoryRow>>>? historyLoader = null)
    {
        _initialHistoryLoader = historyLoader;
        _historyLoader = historyLoader;
        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(force: true), CanRefresh);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
    }

    public ObservableCollection<TrackHistoryRowViewModel> FilteredRows { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }

    public RelayCommand ClearFiltersCommand { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!SetField(ref _isLoading, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(HasNoRows));
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (!SetField(ref _errorMessage, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(HasNoRows));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasRows => FilteredRows.Count > 0;

    public bool HasNoRows => !IsLoading && !HasError && FilteredRows.Count == 0;

    public string TrackIdFilter
    {
        get => _trackIdFilter;
        set => SetFilter(ref _trackIdFilter, value);
    }

    public string TitleFilter
    {
        get => _titleFilter;
        set => SetFilter(ref _titleFilter, value);
    }

    public string ArtistFilter
    {
        get => _artistFilter;
        set => SetFilter(ref _artistFilter, value);
    }

    public string AlbumFilter
    {
        get => _albumFilter;
        set => SetFilter(ref _albumFilter, value);
    }

    public string SubtitleFilter
    {
        get => _subtitleFilter;
        set => SetFilter(ref _subtitleFilter, value);
    }

    public string CatalogAudioVariantsJsonFilter
    {
        get => _catalogAudioVariantsJsonFilter;
        set => SetFilter(ref _catalogAudioVariantsJsonFilter, value);
    }

    public string LastObservedAudioBadgeRawFilter
    {
        get => _lastObservedAudioBadgeRawFilter;
        set => SetFilter(ref _lastObservedAudioBadgeRawFilter, value);
    }

    public string LastObservedAudioVariantFilter
    {
        get => _lastObservedAudioVariantFilter;
        set => SetFilter(ref _lastObservedAudioVariantFilter, value);
    }

    public void AttachHost(TrackerApplicationHost host, DispatcherQueue? dispatcherQueue)
    {
        _historyLoader = host.GetTrackHistoryAsync;
        _dispatcherQueue = dispatcherQueue;
        RefreshCommand.NotifyCanExecuteChanged();
    }

    public async Task SetIsActiveAsync(bool isActive)
    {
        _isActive = isActive;
        if (isActive)
        {
            await EnsureLoadedAsync().ConfigureAwait(false);
        }
    }

    public void NotifyDashboardStateChanged()
    {
        if (!_hasLoadedOnce)
        {
            return;
        }

        _isDirty = true;
        if (_isActive)
        {
            ScheduleRefreshDebounce();
        }
    }

    public async Task EnsureLoadedAsync()
    {
        if (!_hasLoadedOnce)
        {
            await RefreshAsync(force: true).ConfigureAwait(false);
            return;
        }

        if (_isDirty)
        {
            await RefreshAsync(force: true).ConfigureAwait(false);
        }
    }

    public void ClearFilters()
    {
        _trackIdFilter = string.Empty;
        _titleFilter = string.Empty;
        _artistFilter = string.Empty;
        _albumFilter = string.Empty;
        _subtitleFilter = string.Empty;
        _catalogAudioVariantsJsonFilter = string.Empty;
        _lastObservedAudioBadgeRawFilter = string.Empty;
        _lastObservedAudioVariantFilter = string.Empty;

        OnPropertyChanged(nameof(TrackIdFilter));
        OnPropertyChanged(nameof(TitleFilter));
        OnPropertyChanged(nameof(ArtistFilter));
        OnPropertyChanged(nameof(AlbumFilter));
        OnPropertyChanged(nameof(SubtitleFilter));
        OnPropertyChanged(nameof(CatalogAudioVariantsJsonFilter));
        OnPropertyChanged(nameof(LastObservedAudioBadgeRawFilter));
        OnPropertyChanged(nameof(LastObservedAudioVariantFilter));

        ApplyFilters();
    }

    private bool CanRefresh() => _historyLoader is not null || _initialHistoryLoader is not null;

    private bool SetFilter(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        value ??= string.Empty;
        if (!SetField(ref field, value, propertyName))
        {
            return false;
        }

        ScheduleFilterDebounce();
        return true;
    }

    private void ScheduleFilterDebounce()
    {
        _filterDebounceCts?.Cancel();
        _filterDebounceCts?.Dispose();
        _filterDebounceCts = new CancellationTokenSource();
        _ = ApplyFiltersAfterDelayAsync(_filterDebounceCts.Token);
    }

    private async Task ApplyFiltersAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
            if (!cancellationToken.IsCancellationRequested)
            {
                await RunOnUiThreadAsync(ApplyFilters).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ScheduleRefreshDebounce()
    {
        _refreshDebounceCts?.Cancel();
        _refreshDebounceCts?.Dispose();
        _refreshDebounceCts = new CancellationTokenSource();
        _ = RefreshAfterDelayAsync(_refreshDebounceCts.Token);
    }

    private async Task RefreshAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            if (!cancellationToken.IsCancellationRequested && _isActive && _isDirty)
            {
                await RefreshAsync(force: true).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshAsync(bool force)
    {
        var loader = _historyLoader ?? _initialHistoryLoader;
        if (loader is null || (IsLoading && !force))
        {
            return;
        }

        await RunOnUiThreadAsync(() =>
        {
            IsLoading = true;
            ErrorMessage = string.Empty;
        }).ConfigureAwait(false);

        try
        {
            var rows = await loader(CancellationToken.None).ConfigureAwait(false);
            await RunOnUiThreadAsync(() =>
            {
                _allRows = rows.Select(row => new TrackHistoryRowViewModel(row)).ToList();
                _hasLoadedOnce = true;
                _isDirty = false;
                ApplyFilters();
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RunOnUiThreadAsync(() =>
            {
                ErrorMessage = ex.Message;
                _allRows = [];
                FilteredRows.Clear();
                OnPropertyChanged(nameof(HasRows));
                OnPropertyChanged(nameof(HasNoRows));
            }).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiThreadAsync(() => IsLoading = false).ConfigureAwait(false);
        }
    }

    private void ApplyFilters()
    {
        var rows = _allRows.Where(MatchesFilters).ToArray();
        FilteredRows.Clear();
        foreach (var row in rows)
        {
            FilteredRows.Add(row);
        }

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasNoRows));
    }

    private bool MatchesFilters(TrackHistoryRowViewModel row)
    {
        return Matches(row, TrackHistoryColumnId.TrackId, TrackIdFilter)
            && Matches(row, TrackHistoryColumnId.Title, TitleFilter)
            && Matches(row, TrackHistoryColumnId.Artist, ArtistFilter)
            && Matches(row, TrackHistoryColumnId.Album, AlbumFilter)
            && Matches(row, TrackHistoryColumnId.Subtitle, SubtitleFilter)
            && Matches(row, TrackHistoryColumnId.CatalogAudioVariantsJson, CatalogAudioVariantsJsonFilter)
            && Matches(row, TrackHistoryColumnId.LastObservedAudioBadgeRaw, LastObservedAudioBadgeRawFilter)
            && Matches(row, TrackHistoryColumnId.LastObservedAudioVariant, LastObservedAudioVariantFilter);
    }

    private static bool Matches(TrackHistoryRowViewModel row, TrackHistoryColumnId columnId, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        var value = row.GetFilterValue(columnId);
        return value.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private Task RunOnUiThreadAsync(Action action)
    {
        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var completionSource = new TaskCompletionSource();
        _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                action();
                completionSource.SetResult();
            }
            catch (Exception ex)
            {
                completionSource.SetException(ex);
            }
        });

        return completionSource.Task;
    }
}
