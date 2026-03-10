using AppleMusicHistory.Core.Abstractions;
using AppleMusicHistory.Core.Models;
using AppleMusicHistory.Core.Services;
using AppleMusicHistory.Infrastructure.Data;
using AppleMusicHistory.Infrastructure.Settings;

namespace AppleMusicHistory.App;

public sealed class TrackerRuntime : IAsyncDisposable
{
    private readonly IAppleMusicSnapshotSource _snapshotSource;
    private readonly IHistoryRepository _repository;
    private readonly JsonTrackerSettingsStore _settingsStore;
    private readonly FileLogger _logger;
    private readonly PlaybackSessionCoordinator _coordinator;
    private readonly CancellationTokenSource _cts = new();
    private readonly TrackerOptions _options;
    private TrackerSettings _settings;
    private Task? _loopTask;

    public TrackerRuntime(
        IAppleMusicSnapshotSource snapshotSource,
        IHistoryRepository repository,
        ITrackMetadataEnricher metadataEnricher,
        JsonTrackerSettingsStore settingsStore,
        TrackerSettings settings,
        FileLogger logger,
        long appRunId)
    {
        _snapshotSource = snapshotSource;
        _repository = repository;
        _settingsStore = settingsStore;
        _settings = settings;
        _logger = logger;
        _options = settings.Options;
        _coordinator = new PlaybackSessionCoordinator(repository, settings.Options, appRunId, metadataEnricher);
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
        StatusChanged?.Invoke(new RuntimeStatus(isPaused, null, _coordinator.ActiveSession, stats));
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
                PlaybackSnapshot? snapshot = null;
                if (!_settings.TrackingPaused)
                {
                    snapshot = await _snapshotSource.GetCurrentAsync(_cts.Token).ConfigureAwait(false);
                    await _coordinator.HandleSnapshotAsync(snapshot, _cts.Token).ConfigureAwait(false);
                }

                var stats = await _repository.GetStatisticsAsync(_cts.Token).ConfigureAwait(false);
                StatusChanged?.Invoke(new RuntimeStatus(_settings.TrackingPaused, snapshot, _coordinator.ActiveSession, stats));

                var delay = _settings.TrackingPaused
                    ? _options.PausedPollingInterval
                    : snapshot is null
                        ? _options.MissingAppPollingInterval
                        : snapshot.IsPaused
                            ? _options.PausedPollingInterval
                            : _options.ActivePollingInterval;

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
}
