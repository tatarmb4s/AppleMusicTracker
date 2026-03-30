using AppleMusicHistory.Core.Abstractions;
using AppleMusicHistory.Core.Models;
using AppleMusicHistory.Host;
using AppleMusicHistory.Infrastructure.Data;
using AppleMusicHistory.Infrastructure.Export;
using AppleMusicHistory.Infrastructure.Settings;
using AppleMusicHistory.Infrastructure.Startup;

namespace AppleMusicHistory.Tests;

public sealed class TrackerApplicationHostTests
{
    [Fact]
    public async Task RuntimeStatus_MapsToDashboardState()
    {
        var exportPicker = new StubExportFilePicker();
        var startupRegistration = new StubStartupRegistration();
        var repository = new TestHistoryRepository();
        var fakeRuntime = new StubTrackerRuntime();
        var tempSettingsPath = CreateTempPath("settings.json");
        var settingsStore = new JsonTrackerSettingsStore(tempSettingsPath);
        await settingsStore.SaveAsync(new TrackerSettings
        {
            TrackingPaused = false,
            Options = new TrackerOptions
            {
                DatabasePath = CreateTempPath("history.sqlite"),
                LaunchAtStartup = true,
                MetadataEnrichmentEnabled = true
            }
        }, CancellationToken.None);

        await using var host = new TrackerApplicationHost(
            exportPicker,
            logger: new FileLogger(),
            settingsStore: settingsStore,
            startupRegistration: startupRegistration,
            repository: repository,
            exporterFactory: _ => new StubHistoryExporter(),
            snapshotSourceFactory: _ => new StubSnapshotSource(),
            metadataEnricherFactory: _ => new NoOpMetadataEnricher(),
            runtimeFactory: (_, _, _, _, _, _, _, _) => fakeRuntime);

        await host.InitializeAsync();

        fakeRuntime.Emit(new RuntimeStatus(
            false,
            AppleMusicSnapshotReadState.Available,
            PlaybackSnapshot.Create(
                "Shape of You",
                "Ed Sheeran",
                "Divide",
                "Ed Sheeran - Divide",
                DateTimeOffset.UtcNow,
                false,
                65,
                240,
                "Dolby Audio"),
            null,
            new ListeningSessionRecord(10, 3, 1, DateTimeOffset.UtcNow.AddMinutes(-2), null, 5, 65, 65, 60, 0, 0, 0, SessionState.Playing, null, DateTimeOffset.UtcNow, "Dolby Audio", PlaybackAudioVariant.DolbyAudio),
            new TrackDetailsRecord(
                new TrackRecord(3, "fp", "Shape of You", "Ed Sheeran", "Divide", "Ed Sheeran - Divide", "shape of you", "ed sheeran", "divide", 240, "https://song", "https://artist", "https://artwork", "[\"Lossless\"]", "Dolby Audio", PlaybackAudioVariant.DolbyAudio, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
                new TrackMetadataRecord(3, "https://song", "https://album", "https://artist", "song-id", "album-id", "artist-id", 1, 2, 3, 240, DateTimeOffset.Parse("2017-01-06T00:00:00Z"), "Composer", "[\"Pop\"]", 4, 12, 1, 1, "ISRC123", null, "explicit", "[\"DolbyAudio\"]", "https://artwork", 1200, 1200, null, "us", "[\"catalog\"]", null, null, null, DateTimeOffset.UtcNow, null)),
            new TrackerStatistics(12, 40, 1, DateTimeOffset.UtcNow)));

        var state = host.CurrentState;

        Assert.Equal("Apple Music playing", state.AppleMusicState);
        Assert.Equal("Shape of You", state.CurrentTitle);
        Assert.Equal("Ed Sheeran", state.CurrentArtist);
        Assert.Equal("Divide", state.CurrentAlbum);
        Assert.Equal("Dolby Audio", state.CurrentAudioFormat);
        Assert.Equal(PlaybackAudioVariant.DolbyAudio, state.CurrentAudioVariant);
        Assert.Equal("1:05", state.ElapsedText);
        Assert.Equal("-2:55", state.RemainingText);
        Assert.True(state.PlaybackProgress > 0.25 && state.PlaybackProgress < 0.28);
        Assert.Equal("Composer", state.CurrentComposer);
        Assert.Equal("Pop", state.CurrentGenres);
        Assert.True(fakeRuntime.StartCalled);
    }

    [Fact]
    public async Task InitializeAsync_DefaultDashboardState_UsesUnknownAudioVariant()
    {
        var exportPicker = new StubExportFilePicker();
        var exporter = new StubHistoryExporter();
        var fakeRuntime = new StubTrackerRuntime();

        await using var host = await CreateHostAsync(exportPicker, exporter, fakeRuntime);

        Assert.Equal("Standard / unknown", host.CurrentState.CurrentAudioFormat);
        Assert.Equal(PlaybackAudioVariant.Unknown, host.CurrentState.CurrentAudioVariant);
    }

    [Fact]
    public async Task ExportAsync_UsesPickerPathAndDispatchesByKind()
    {
        var exportPicker = new StubExportFilePicker
        {
            SavePath = CreateTempPath("sessions.json")
        };
        var exporter = new StubHistoryExporter();
        var fakeRuntime = new StubTrackerRuntime();

        await using var host = await CreateHostAsync(exportPicker, exporter, fakeRuntime);

        await host.ExportAsync(ExportKind.SessionsJson);

        Assert.Equal(exportPicker.SavePath, exporter.JsonPath);
    }

    [Fact]
    public async Task UpdateLaunchAtStartupAsync_UpdatesRegistration()
    {
        var startupRegistration = new StubStartupRegistration();
        var exportPicker = new StubExportFilePicker();
        var exporter = new StubHistoryExporter();
        var fakeRuntime = new StubTrackerRuntime();

        await using var host = await CreateHostAsync(exportPicker, exporter, fakeRuntime, startupRegistration);

        await host.UpdateLaunchAtStartupAsync(false);

        Assert.False(startupRegistration.LastEnabled);
        Assert.False(host.CurrentState.LaunchAtStartup);
    }

    private static async Task<TrackerApplicationHost> CreateHostAsync(
        StubExportFilePicker exportPicker,
        StubHistoryExporter exporter,
        StubTrackerRuntime fakeRuntime,
        StubStartupRegistration? startupRegistration = null)
    {
        var settingsPath = CreateTempPath("settings.json");
        var settingsStore = new JsonTrackerSettingsStore(settingsPath);
        await settingsStore.SaveAsync(new TrackerSettings
        {
            TrackingPaused = false,
            Options = new TrackerOptions
            {
                DatabasePath = CreateTempPath("history.sqlite"),
                LaunchAtStartup = true,
                MetadataEnrichmentEnabled = true
            }
        }, CancellationToken.None);

        var host = new TrackerApplicationHost(
            exportPicker,
            logger: new FileLogger(),
            settingsStore: settingsStore,
            startupRegistration: startupRegistration ?? new StubStartupRegistration(),
            repository: new TestHistoryRepository(),
            exporterFactory: _ => exporter,
            snapshotSourceFactory: _ => new StubSnapshotSource(),
            metadataEnricherFactory: _ => new NoOpMetadataEnricher(),
            runtimeFactory: (_, _, _, _, _, _, _, _) => fakeRuntime);
        await host.InitializeAsync();
        return host;
    }

    private static string CreateTempPath(string fileName)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AppleMusicHistoryHostTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }

    private sealed class StubSnapshotSource : IAppleMusicSnapshotSource
    {
        public Task<AppleMusicSnapshotReadResult> GetCurrentAsync(CancellationToken cancellationToken)
            => Task.FromResult(AppleMusicSnapshotReadResult.AppNotRunning());
    }

    private sealed class StubTrackerRuntime : ITrackerRuntime
    {
        public bool StartCalled { get; private set; }

        public event Action<RuntimeStatus>? StatusChanged;

        public void Start()
        {
            StartCalled = true;
        }

        public Task SetTrackingPausedAsync(bool isPaused) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Emit(RuntimeStatus status)
        {
            StatusChanged?.Invoke(status);
        }
    }

    private sealed class StubExportFilePicker : IExportFilePicker
    {
        public string? SavePath { get; set; }

        public Task<string?> PickSavePathAsync(ExportKind exportKind, CancellationToken cancellationToken)
            => Task.FromResult(SavePath);
    }

    private sealed class StubHistoryExporter : IHistoryExporter
    {
        public string? CsvPath { get; private set; }
        public string? JsonPath { get; private set; }
        public string? TracksCsvPath { get; private set; }
        public string? TracksJsonPath { get; private set; }

        public Task ExportCsvAsync(string filePath, CancellationToken cancellationToken)
        {
            CsvPath = filePath;
            return Task.CompletedTask;
        }

        public Task ExportJsonAsync(string filePath, CancellationToken cancellationToken)
        {
            JsonPath = filePath;
            return Task.CompletedTask;
        }

        public Task ExportTracksCsvAsync(string filePath, CancellationToken cancellationToken)
        {
            TracksCsvPath = filePath;
            return Task.CompletedTask;
        }

        public Task ExportTracksJsonAsync(string filePath, CancellationToken cancellationToken)
        {
            TracksJsonPath = filePath;
            return Task.CompletedTask;
        }
    }

    private sealed class StubStartupRegistration : IStartupRegistration
    {
        public bool LastEnabled { get; private set; }
        public string? LastTargetPath { get; private set; }

        public bool IsEnabled() => LastEnabled;

        public void SetEnabled(bool enabled, string targetPath)
        {
            LastEnabled = enabled;
            LastTargetPath = targetPath;
        }
    }
}
