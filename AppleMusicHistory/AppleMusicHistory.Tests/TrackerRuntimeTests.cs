using AppleMusicHistory.Core.Abstractions;
using AppleMusicHistory.Core.Models;
using AppleMusicHistory.Host;
using AppleMusicHistory.Infrastructure.Data;
using AppleMusicHistory.Infrastructure.Settings;

namespace AppleMusicHistory.Tests;

public sealed class TrackerRuntimeTests
{
    [Fact]
    public async Task RecoveringFollowedByAvailable_KeepsSessionOpen()
    {
        var repository = new TestHistoryRepository();
        var source = new SequencedSnapshotSource(
            AppleMusicSnapshotReadResult.Available(CreateSnapshot(5)),
            AppleMusicSnapshotReadResult.Recovering("resume"),
            AppleMusicSnapshotReadResult.Available(CreateSnapshot(6)));
        await using var runtime = CreateRuntime(source, repository, new TrackerOptions
        {
            DatabasePath = CreateTempPath("history.sqlite"),
            ActivePollingInterval = TimeSpan.FromMilliseconds(20),
            MissingAppPollingInterval = TimeSpan.FromMilliseconds(50),
            PausedPollingInterval = TimeSpan.FromMilliseconds(20),
            MetadataEnrichmentEnabled = false
        });

        runtime.Start();
        await WaitUntilAsync(() => repository.Sessions.Count == 1 && repository.Sessions[0].LastPositionSeconds >= 6, TimeSpan.FromSeconds(2));

        Assert.Single(repository.Sessions);
        Assert.Empty(repository.ClosedSessions);
    }

    [Fact]
    public async Task NoTrackDetected_ClosesAfterGracePeriod()
    {
        var repository = new TestHistoryRepository();
        var source = new SequencedSnapshotSource(
            AppleMusicSnapshotReadResult.Available(CreateSnapshot(5)),
            AppleMusicSnapshotReadResult.NoTrackDetected("No active Apple Music track detected."));
        var missingInterval = TimeSpan.FromMilliseconds(200);
        await using var runtime = CreateRuntime(source, repository, new TrackerOptions
        {
            DatabasePath = CreateTempPath("history.sqlite"),
            ActivePollingInterval = TimeSpan.FromMilliseconds(25),
            MissingAppPollingInterval = missingInterval,
            PausedPollingInterval = TimeSpan.FromMilliseconds(25),
            MetadataEnrichmentEnabled = false
        });

        runtime.Start();
        await Task.Delay(TimeSpan.FromMilliseconds(120));
        Assert.Empty(repository.ClosedSessions);

        await WaitUntilAsync(() => repository.ClosedSessions.Count > 0, TimeSpan.FromSeconds(2));

        Assert.Equal(SessionEndReason.NoTrackDetected, repository.ClosedSessions.Single().Reason);
    }

    [Fact]
    public async Task StatusChanged_ExposesTrackDetailsAfterMetadataEnrichment()
    {
        var repository = new TestHistoryRepository();
        var source = new SequencedSnapshotSource(AppleMusicSnapshotReadResult.Available(CreateSnapshot(5)));
        var enricher = new StaticMetadataEnricher();
        await using var runtime = CreateRuntime(source, repository, new TrackerOptions
        {
            DatabasePath = CreateTempPath("history.sqlite"),
            ActivePollingInterval = TimeSpan.FromMilliseconds(20),
            MissingAppPollingInterval = TimeSpan.FromMilliseconds(50),
            PausedPollingInterval = TimeSpan.FromMilliseconds(20),
            MetadataEnrichmentEnabled = true
        }, enricher);

        RuntimeStatus? latest = null;
        runtime.StatusChanged += status => latest = status;
        runtime.Start();

        await WaitUntilAsync(() => latest?.CurrentTrackDetails?.Metadata?.ComposerName == "Composer", TimeSpan.FromSeconds(2));

        Assert.Equal("Composer", latest!.CurrentTrackDetails!.Metadata!.ComposerName);
        Assert.Equal("catalog-song", latest.CurrentTrackDetails.Metadata.CatalogSongId);
    }

    private static TrackerRuntime CreateRuntime(
        IAppleMusicSnapshotSource snapshotSource,
        TestHistoryRepository repository,
        TrackerOptions options,
        ITrackMetadataEnricher? enricher = null)
    {
        var settingsPath = CreateTempPath("settings.json");
        var settingsStore = new JsonTrackerSettingsStore(settingsPath);
        var settings = new TrackerSettings
        {
            Options = options
        };

        return new TrackerRuntime(
            snapshotSource,
            repository,
            enricher ?? new NoOpMetadataEnricher(),
            settingsStore,
            settings,
            new FileLogger(),
            42);
    }

    private static PlaybackSnapshot CreateSnapshot(int currentPositionSeconds)
    {
        return PlaybackSnapshot.Create(
            "Song",
            "Artist",
            "Album",
            "Artist - Album",
            DateTimeOffset.UtcNow,
            false,
            currentPositionSeconds,
            240,
            "Lossless");
    }

    private static string CreateTempPath(string fileName)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AppleMusicHistoryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (!predicate())
        {
            if (DateTime.UtcNow - start > timeout)
            {
                throw new TimeoutException("Condition was not met within the allotted time.");
            }

            await Task.Delay(20);
        }
    }

    private sealed class SequencedSnapshotSource : IAppleMusicSnapshotSource
    {
        private readonly Queue<AppleMusicSnapshotReadResult> _results;
        private AppleMusicSnapshotReadResult _current;

        public SequencedSnapshotSource(params AppleMusicSnapshotReadResult[] results)
        {
            _results = new Queue<AppleMusicSnapshotReadResult>(results);
            _current = results[^1];
        }

        public Task<AppleMusicSnapshotReadResult> GetCurrentAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_results.Count > 0)
            {
                _current = _results.Dequeue();
            }

            return Task.FromResult(_current);
        }
    }

    private sealed class StaticMetadataEnricher : ITrackMetadataEnricher
    {
        public Task<TrackEnrichmentResult?> EnrichAsync(TrackFingerprint fingerprint, CancellationToken cancellationToken)
        {
            return Task.FromResult<TrackEnrichmentResult?>(new TrackEnrichmentResult(
                "https://music.apple.com/us/song/song-id",
                "https://music.apple.com/us/album/album-id",
                "https://music.apple.com/us/artist/artist-id",
                "catalog-song",
                "catalog-album",
                "catalog-artist",
                1,
                2,
                3,
                240,
                DateTimeOffset.Parse("2026-03-09T12:00:00Z"),
                "Composer",
                ["Electronic"],
                4,
                12,
                1,
                2,
                "USRC17607839",
                "https://example.com/preview.m4a",
                "explicit",
                ["Lossless"],
                null,
                null,
                null,
                "us",
                ["catalog"],
                null,
                null,
                "{\"catalog\":true}",
                DateTimeOffset.UtcNow));
        }
    }
}
