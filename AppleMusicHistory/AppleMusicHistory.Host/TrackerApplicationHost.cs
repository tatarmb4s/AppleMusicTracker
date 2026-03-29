using System.Diagnostics;
using AppleMusicHistory.Core.Abstractions;
using AppleMusicHistory.Core.Models;
using AppleMusicHistory.Infrastructure.Data;
using AppleMusicHistory.Infrastructure.Export;
using AppleMusicHistory.Infrastructure.Scraping;
using AppleMusicHistory.Infrastructure.Settings;
using AppleMusicHistory.Infrastructure.Startup;

namespace AppleMusicHistory.Host;

public sealed class TrackerApplicationHost : IAsyncDisposable
{
    private readonly IExportFilePicker _exportFilePicker;
    private readonly FileLogger _logger;
    private readonly JsonTrackerSettingsStore _settingsStore;
    private readonly IStartupRegistration _startupRegistration;
    private readonly IHistoryRepository? _providedRepository;
    private readonly Func<FileLogger, IAppleMusicSnapshotSource> _snapshotSourceFactory;
    private readonly Func<FileLogger, ITrackMetadataEnricher> _metadataEnricherFactory;
    private readonly Func<IHistoryRepository, IHistoryExporter> _exporterFactory;
    private readonly Func<IAppleMusicSnapshotSource, IHistoryRepository, ITrackMetadataEnricher, JsonTrackerSettingsStore, TrackerSettings, FileLogger, long, IArtworkCache?, ITrackerRuntime> _runtimeFactory;
    private IHistoryRepository? _repository;
    private IHistoryExporter? _exporter;
    private ITrackerRuntime? _runtime;
    private TrackerSettings? _settings;
    private RuntimeStatus? _lastStatus;
    private bool _initialized;

    public TrackerApplicationHost(
        IExportFilePicker exportFilePicker,
        FileLogger? logger = null,
        JsonTrackerSettingsStore? settingsStore = null,
        IStartupRegistration? startupRegistration = null,
        IHistoryRepository? repository = null,
        Func<FileLogger, IAppleMusicSnapshotSource>? snapshotSourceFactory = null,
        Func<FileLogger, ITrackMetadataEnricher>? metadataEnricherFactory = null,
        Func<IHistoryRepository, IHistoryExporter>? exporterFactory = null,
        Func<IAppleMusicSnapshotSource, IHistoryRepository, ITrackMetadataEnricher, JsonTrackerSettingsStore, TrackerSettings, FileLogger, long, IArtworkCache?, ITrackerRuntime>? runtimeFactory = null)
    {
        _exportFilePicker = exportFilePicker;
        _logger = logger ?? new FileLogger();
        _settingsStore = settingsStore ?? new JsonTrackerSettingsStore(AppPaths.SettingsPath);
        _startupRegistration = startupRegistration ?? new WindowsStartupRegistration(AppPaths.StartupShortcutPath);
        _providedRepository = repository;
        _snapshotSourceFactory = snapshotSourceFactory ?? (loggerInstance => new AppleMusicUiAutomationSnapshotSource(logger: loggerInstance));
        _metadataEnricherFactory = metadataEnricherFactory ?? CreateMetadataEnricher;
        _exporterFactory = exporterFactory ?? (historyRepository => new HistoryExporter(historyRepository));
        _runtimeFactory = runtimeFactory ?? ((snapshotSource, historyRepository, metadataEnricher, trackerSettingsStore, trackerSettings, loggerInstance, appRunId, artworkCache) =>
            new TrackerRuntime(
                snapshotSource,
                historyRepository,
                metadataEnricher,
                trackerSettingsStore,
                trackerSettings,
                loggerInstance,
                appRunId,
                artworkCache));
        CurrentState = DashboardState.CreateDefault(AppPaths.DatabasePath, true, true, false);
    }

    public event Action<DashboardState>? DashboardStateChanged;

    public DashboardState CurrentState { get; private set; }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _settings = await _settingsStore.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        CurrentState = DashboardStateMapper.Map(null, _settings);
        DashboardStateChanged?.Invoke(CurrentState);

        _repository = _providedRepository ?? new SqliteHistoryRepository(_settings.Options.DatabasePath);
        await _repository.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
        await _repository.RecoverOpenSessionsAsync(DateTimeOffset.UtcNow, SessionEndReason.RecoveredAfterCrash, CancellationToken.None).ConfigureAwait(false);

        var appRunId = await _repository.StartAppRunAsync(
            new AppRunInfo(
                DateTimeOffset.UtcNow,
                GetVersion(),
                Environment.MachineName,
                Environment.UserName,
                Environment.Version.ToString(),
                Environment.OSVersion.ToString()),
            CancellationToken.None).ConfigureAwait(false);

        _exporter = _exporterFactory(_repository);
        _runtime = _runtimeFactory(
            _snapshotSourceFactory(_logger),
            _repository,
            _metadataEnricherFactory(_logger),
            _settingsStore,
            _settings,
            _logger,
            appRunId,
            new ArtworkCacheService());
        _runtime.StatusChanged += OnRuntimeStatusChanged;
        _runtime.Start();

        ConfigureStartupShortcut();
        _initialized = true;
    }

    public async Task SetTrackingPausedAsync(bool paused)
    {
        if (_runtime is null || _settings is null)
        {
            return;
        }

        _settings = _settings with { TrackingPaused = paused };
        await _runtime.SetTrackingPausedAsync(paused).ConfigureAwait(false);
        PublishState();
    }

    public async Task UpdateLaunchAtStartupAsync(bool enabled)
    {
        if (_settings is null)
        {
            return;
        }

        if (_settings.Options.LaunchAtStartup == enabled)
        {
            return;
        }

        _settings = _settings with { Options = _settings.Options with { LaunchAtStartup = enabled } };
        await _settingsStore.SaveAsync(_settings, CancellationToken.None).ConfigureAwait(false);
        ConfigureStartupShortcut();
        PublishState();
    }

    public async Task ExportAsync(ExportKind exportKind)
    {
        if (_exporter is null)
        {
            return;
        }

        var filePath = await _exportFilePicker.PickSavePathAsync(exportKind, CancellationToken.None).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        switch (exportKind)
        {
            case ExportKind.SessionsCsv:
                await _exporter.ExportCsvAsync(filePath, CancellationToken.None).ConfigureAwait(false);
                break;

            case ExportKind.SessionsJson:
                await _exporter.ExportJsonAsync(filePath, CancellationToken.None).ConfigureAwait(false);
                break;

            case ExportKind.TracksCsv:
                await _exporter.ExportTracksCsvAsync(filePath, CancellationToken.None).ConfigureAwait(false);
                break;

            case ExportKind.TracksJson:
                await _exporter.ExportTracksJsonAsync(filePath, CancellationToken.None).ConfigureAwait(false);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(exportKind), exportKind, null);
        }
    }

    public void OpenDatabaseFolder()
    {
        var directory = Path.GetDirectoryName(_settings?.Options.DatabasePath ?? AppPaths.DatabasePath) ?? AppPaths.AppDataDirectory;
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo
        {
            FileName = directory,
            UseShellExecute = true
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_runtime is not null)
        {
            _runtime.StatusChanged -= OnRuntimeStatusChanged;
            await _runtime.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void OnRuntimeStatusChanged(RuntimeStatus status)
    {
        _lastStatus = status;
        PublishState();
    }

    private void PublishState()
    {
        if (_settings is null)
        {
            return;
        }

        CurrentState = DashboardStateMapper.Map(_lastStatus, _settings);
        DashboardStateChanged?.Invoke(CurrentState);
    }

    private void ConfigureStartupShortcut()
    {
        if (_settings is null)
        {
            return;
        }

        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            _startupRegistration.SetEnabled(_settings.Options.LaunchAtStartup, exePath);
        }
    }

    private static string GetVersion()
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return "dev";
        }

        return FileVersionInfo.GetVersionInfo(exePath).FileVersion ?? "dev";
    }

    private static CompositeTrackMetadataEnricher CreateMetadataEnricher(FileLogger logger)
    {
        var enrichers = new List<ITrackMetadataEnricher>
        {
            new AppleMusicWebMetadataEnricher(logger: logger),
            new ItunesSearchMetadataEnricher(logger: logger)
        };

        var developerToken = Environment.GetEnvironmentVariable("APPLE_MUSIC_DEVELOPER_TOKEN");
        if (!string.IsNullOrWhiteSpace(developerToken))
        {
            var storefront = Environment.GetEnvironmentVariable("APPLE_MUSIC_STOREFRONT") ?? "us";
            enrichers.Add(new AppleMusicCatalogMetadataEnricher(developerToken, storefront, logger));
        }

        return new CompositeTrackMetadataEnricher(enrichers);
    }
}
