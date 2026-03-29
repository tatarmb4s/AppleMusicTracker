using AppleMusicHistory.Core.Abstractions;
using AppleMusicHistory.Core.Models;
using AppleMusicHistory.Core.Services;
using AppleMusicHistory.Infrastructure.Data;
using AppleMusicHistory.Infrastructure.Settings;

namespace AppleMusicHistory.Host;

public sealed class TrackerRuntime : ITrackerRuntime
{
    private readonly IAppleMusicSnapshotSource _snapshotSource;
    private readonly IHistoryRepository _repository;
    private readonly JsonTrackerSettingsStore _settingsStore;
    private readonly FileLogger _logger;
    private readonly PlaybackSessionCoordinator _coordinator;
    private readonly CancellationTokenSource _cts = new();
    private readonly TrackerOptions _options;
    private TrackerSettings _settings;
    private AppleMusicSnapshotReadResult _lastReadResult = AppleMusicSnapshotReadResult.AppNotRunning();
    private TrackDetailsRecord? _currentTrackDetails;
    private DateTimeOffset? _noTrackSinceUtc;
    private Task? _loopTask;

    public TrackerRuntime(
        IAppleMusicSnapshotSource snapshotSource,
        IHistoryRepository repository,
        ITrackMetadataEnricher metadataEnricher,
        JsonTrackerSettingsStore settingsStore,
        TrackerSettings settings,
        FileLogger logger,
        long appRunId,
        IArtworkCache? artworkCache = null)
    {
        _snapshotSource = snapshotSource;
        _repository = repository;
        _settingsStore = settingsStore;
        _settings = settings;
        _logger = logger;
        _options = settings.Options;
        _coordinator = new PlaybackSessionCoordinator(repository, settings.Options, appRunId, metadataEnricher, artworkCache);
    }

    public event Action<RuntimeStatus>? StatusChanged;

    public void Start()
    {
        _loopTask ??= Task.Run(RunAsync);
    }

    public async Task SetTrackingPausedAsync(bool isPaused)
    {
        if (_settings.TrackingPaused == isPaused)
        {
            return;
        }

        _settings = _settings with { TrackingPaused = isPaused };
        await _settingsStore.SaveAsync(_settings, CancellationToken.None).ConfigureAwait(false);

        if (isPaused)
        {
            await _coordinator.PauseTrackingAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var stats = await _repository.GetStatisticsAsync(CancellationToken.None).ConfigureAwait(false);
        StatusChanged?.Invoke(CreateStatus(isPaused, stats));
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await _coordinator.StopAsync(CancellationToken.None).ConfigureAwait(false);
        _cts.Dispose();
    }

    private async Task RunAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var currentResult = _lastReadResult;
                if (!_settings.TrackingPaused)
                {
                    currentResult = await _snapshotSource.GetCurrentAsync(_cts.Token).ConfigureAwait(false);
                    _lastReadResult = currentResult;
                    await HandleReadResultAsync(currentResult, _cts.Token).ConfigureAwait(false);
                }

                var stats = await _repository.GetStatisticsAsync(_cts.Token).ConfigureAwait(false);
                _currentTrackDetails = _coordinator.ActiveSession is null
                    ? null
                    : await _repository.GetTrackDetailsAsync(_coordinator.ActiveSession.TrackId, _cts.Token).ConfigureAwait(false);
                StatusChanged?.Invoke(CreateStatus(_settings.TrackingPaused, stats));

                var delay = GetDelay(currentResult, _settings.TrackingPaused);

                await Task.Delay(delay, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                await _logger.ErrorAsync($"Tracker loop error: {ex}", CancellationToken.None).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(5), _cts.Token).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleReadResultAsync(AppleMusicSnapshotReadResult readResult, CancellationToken cancellationToken)
    {
        switch (readResult.State)
        {
            case AppleMusicSnapshotReadState.Available:
                _noTrackSinceUtc = null;
                await _coordinator.HandleSnapshotAsync(readResult.Snapshot!, cancellationToken).ConfigureAwait(false);
                break;

            case AppleMusicSnapshotReadState.AppNotRunning:
                _noTrackSinceUtc = null;
                await _coordinator.HandlePlaybackUnavailableAsync(SessionEndReason.AppClosed, cancellationToken).ConfigureAwait(false);
                break;

            case AppleMusicSnapshotReadState.Recovering:
                _noTrackSinceUtc = null;
                break;

            case AppleMusicSnapshotReadState.NoTrackDetected:
                await HandleNoTrackDetectedAsync(cancellationToken).ConfigureAwait(false);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(readResult.State), readResult.State, null);
        }
    }

    private async Task HandleNoTrackDetectedAsync(CancellationToken cancellationToken)
    {
        if (_coordinator.ActiveSession is null)
        {
            _noTrackSinceUtc = null;
            return;
        }

        var observedAt = DateTimeOffset.UtcNow;
        _noTrackSinceUtc ??= observedAt;
        if (observedAt - _noTrackSinceUtc.Value < _options.MissingAppPollingInterval)
        {
            return;
        }

        await _coordinator.HandlePlaybackUnavailableAsync(SessionEndReason.NoTrackDetected, cancellationToken).ConfigureAwait(false);
        _noTrackSinceUtc = null;
    }

    private RuntimeStatus CreateStatus(bool isTrackingPaused, TrackerStatistics stats)
    {
        return new RuntimeStatus(
            isTrackingPaused,
            _lastReadResult.State,
            _lastReadResult.Snapshot,
            _lastReadResult.DiagnosticMessage,
            _coordinator.ActiveSession,
            _currentTrackDetails,
            stats);
    }

    private TimeSpan GetDelay(AppleMusicSnapshotReadResult readResult, bool isTrackingPaused)
    {
        if (isTrackingPaused)
        {
            return _options.PausedPollingInterval;
        }

        return readResult.State switch
        {
            AppleMusicSnapshotReadState.Available when readResult.Snapshot?.IsPaused == true => _options.PausedPollingInterval,
            AppleMusicSnapshotReadState.Available => _options.ActivePollingInterval,
            _ => _options.MissingAppPollingInterval
        };
    }
}
