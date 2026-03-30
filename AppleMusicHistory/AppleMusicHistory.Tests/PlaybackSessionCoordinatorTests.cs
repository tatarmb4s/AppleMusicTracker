using AppleMusicHistory.Core.Abstractions;
using AppleMusicHistory.Core.Models;
using AppleMusicHistory.Core.Services;

namespace AppleMusicHistory.Tests;

public sealed class PlaybackSessionCoordinatorTests
{
    [Fact]
    public async Task LongPause_CreatesNewSession()
    {
        var repository = new InMemoryHistoryRepository();
        var options = new TrackerOptions
        {
            DatabasePath = "test.sqlite",
            ResumeGap = TimeSpan.FromMinutes(30)
        };
        var coordinator = new PlaybackSessionCoordinator(repository, options, 42);
        var start = DateTimeOffset.Parse("2026-03-09T12:00:00Z");

        await coordinator.HandleSnapshotAsync(CreateSnapshot(start, false, 5), CancellationToken.None);
        await coordinator.HandleSnapshotAsync(CreateSnapshot(start.AddMinutes(1), true, 65), CancellationToken.None);
        await coordinator.HandleSnapshotAsync(CreateSnapshot(start.AddMinutes(32), false, 66), CancellationToken.None);

        Assert.Equal(2, repository.Sessions.Count);
        Assert.Equal(SessionEndReason.GapTimeout, repository.ClosedSessions.Single().Reason);
    }

    [Fact]
    public async Task ReplayAfterEnding_CreatesNewSession()
    {
        var repository = new InMemoryHistoryRepository();
        var options = new TrackerOptions
        {
            DatabasePath = "test.sqlite",
            ReplayStartThreshold = TimeSpan.FromSeconds(5),
            ReplayEndThreshold = TimeSpan.FromSeconds(5)
        };
        var coordinator = new PlaybackSessionCoordinator(repository, options, 42);
        var start = DateTimeOffset.Parse("2026-03-09T12:00:00Z");

        await coordinator.HandleSnapshotAsync(CreateSnapshot(start, false, 175, duration: 180), CancellationToken.None);
        await coordinator.HandleSnapshotAsync(CreateSnapshot(start.AddSeconds(6), false, 2, duration: 180), CancellationToken.None);

        Assert.Equal(2, repository.Sessions.Count);
        Assert.Equal(SessionEndReason.Replayed, repository.ClosedSessions.Single().Reason);
    }

    [Fact]
    public async Task SameAudioVariant_DoesNotAppendAudioVariantChangedEvent()
    {
        var repository = new InMemoryHistoryRepository();
        var coordinator = new PlaybackSessionCoordinator(repository, new TrackerOptions { DatabasePath = "test.sqlite" }, 42);
        var start = DateTimeOffset.Parse("2026-03-09T12:00:00Z");

        await coordinator.HandleSnapshotAsync(CreateSnapshot(start, false, 5, audioBadge: "Dolby Audio"), CancellationToken.None);
        await coordinator.HandleSnapshotAsync(CreateSnapshot(start.AddSeconds(5), false, 10, audioBadge: "Dolby Audio"), CancellationToken.None);

        Assert.DoesNotContain(repository.Events, x => x.EventType == SessionEventType.AudioVariantChanged);
    }

    [Fact]
    public async Task AudioVariantChange_AppendsEventAndUpdatesSession()
    {
        var repository = new InMemoryHistoryRepository();
        var coordinator = new PlaybackSessionCoordinator(repository, new TrackerOptions { DatabasePath = "test.sqlite" }, 42);
        var start = DateTimeOffset.Parse("2026-03-09T12:00:00Z");

        await coordinator.HandleSnapshotAsync(CreateSnapshot(start, false, 5, audioBadge: "Dolby Audio"), CancellationToken.None);
        await coordinator.HandleSnapshotAsync(CreateSnapshot(start.AddSeconds(5), false, 10, audioBadge: "Lossless"), CancellationToken.None);

        var changeEvent = Assert.Single(repository.Events, x => x.EventType == SessionEventType.AudioVariantChanged);
        Assert.Contains("DolbyAudio", changeEvent.PayloadJson);
        Assert.Contains("Lossless", changeEvent.PayloadJson);
        Assert.Equal(PlaybackAudioVariant.Lossless, repository.Sessions.Single().LastObservedAudioVariant);
        Assert.Equal("Lossless", repository.Sessions.Single().LastObservedAudioBadgeRaw);
    }

    private static PlaybackSnapshot CreateSnapshot(
        DateTimeOffset observedAtUtc,
        bool isPaused,
        int current,
        int duration = 240,
        string? audioBadge = null)
    {
        return PlaybackSnapshot.Create(
            "Song",
            "Artist",
            "Album",
            "Artist — Album",
            observedAtUtc,
            isPaused,
            current,
            duration,
            audioBadge);
    }

    private sealed class InMemoryHistoryRepository : IHistoryRepository
    {
        private long _nextTrackId = 1;
        private long _nextSessionId = 1;
        private readonly Dictionary<string, TrackRecord> _tracks = new();
        private readonly Dictionary<long, TrackMetadataRecord> _metadata = new();

        public List<ListeningSessionRecord> Sessions { get; } = [];
        public List<SessionClosure> ClosedSessions { get; } = [];
        public List<SessionEventRecord> Events { get; } = [];

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<long> StartAppRunAsync(AppRunInfo appRun, CancellationToken cancellationToken) => Task.FromResult(1L);

        public Task RecoverOpenSessionsAsync(DateTimeOffset recoveredAtUtc, SessionEndReason reason, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<TrackRecord> UpsertTrackAsync(TrackUpsert track, CancellationToken cancellationToken)
        {
            if (_tracks.TryGetValue(track.Fingerprint.Value, out var existing))
            {
                var updated = existing with
                {
                    Title = track.Title,
                    Artist = track.Artist,
                    Album = track.Album,
                    Subtitle = track.Subtitle,
                    DurationSeconds = track.DurationSeconds ?? existing.DurationSeconds,
                    CatalogAudioVariantsJson = track.CatalogAudioVariantsJson ?? existing.CatalogAudioVariantsJson,
                    LastObservedAudioBadgeRaw = track.LastObservedAudioBadgeRaw,
                    LastObservedAudioVariant = track.LastObservedAudioVariant,
                    LastSeenUtc = track.ObservedAtUtc
                };
                _tracks[track.Fingerprint.Value] = updated;
                return Task.FromResult(updated);
            }

            var record = new TrackRecord(
                _nextTrackId++,
                track.Fingerprint.Value,
                track.Title,
                track.Artist,
                track.Album,
                track.Subtitle,
                track.Fingerprint.NormalizedTitle,
                track.Fingerprint.NormalizedArtist,
                track.Fingerprint.NormalizedAlbum,
                track.DurationSeconds,
                track.SongUrl,
                track.ArtistUrl,
                track.ArtworkUrl,
                track.CatalogAudioVariantsJson,
                track.LastObservedAudioBadgeRaw,
                track.LastObservedAudioVariant,
                track.ObservedAtUtc,
                track.ObservedAtUtc,
                track.EnrichedAtUtc);
            _tracks.Add(track.Fingerprint.Value, record);
            return Task.FromResult(record);
        }

        public Task UpsertTrackMetadataAsync(long trackId, TrackMetadataUpsert metadata, CancellationToken cancellationToken)
        {
            _metadata[trackId] = new TrackMetadataRecord(
                trackId,
                metadata.AppleMusicSongUrl,
                metadata.AppleMusicAlbumUrl,
                metadata.AppleMusicArtistUrl,
                metadata.CatalogSongId,
                metadata.CatalogAlbumId,
                metadata.CatalogArtistId,
                metadata.ItunesTrackId,
                metadata.ItunesCollectionId,
                metadata.ItunesArtistId,
                metadata.DurationSeconds,
                metadata.ReleaseDateUtc,
                metadata.ComposerName,
                metadata.GenreNamesJson,
                metadata.TrackNumber,
                metadata.TrackCount,
                metadata.DiscNumber,
                metadata.DiscCount,
                metadata.Isrc,
                metadata.PreviewUrl,
                metadata.ContentRating,
                metadata.CatalogAudioVariantsJson,
                metadata.ArtworkUrl,
                metadata.ArtworkWidth,
                metadata.ArtworkHeight,
                metadata.ArtworkCacheRelativePath,
                metadata.Storefront,
                metadata.MetadataSourcesJson,
                metadata.WebPayloadJson,
                metadata.ItunesPayloadJson,
                metadata.CatalogPayloadJson,
                metadata.EnrichedAtUtc,
                metadata.ArtworkCachedAtUtc);
            return Task.CompletedTask;
        }

        public Task<int> GetNextReplayIndexAsync(long trackId, CancellationToken cancellationToken)
            => Task.FromResult(Sessions.Count(s => s.TrackId == trackId));

        public Task<ListeningSessionRecord> StartSessionAsync(StartSessionRequest session, CancellationToken cancellationToken)
        {
            var record = new ListeningSessionRecord(
                _nextSessionId++,
                session.TrackId,
                session.AppRunId,
                session.StartedAtUtc,
                null,
                session.FirstPositionSeconds,
                session.FirstPositionSeconds,
                session.FirstPositionSeconds,
                0,
                0,
                0,
                session.ReplayIndex,
                session.State,
                null,
                session.LastObservedUtc,
                session.LastObservedAudioBadgeRaw,
                session.LastObservedAudioVariant);
            Sessions.Add(record);
            return Task.FromResult(record);
        }

        public Task UpdateSessionProgressAsync(SessionProgressUpdate update, CancellationToken cancellationToken)
        {
            var session = Sessions.Single(s => s.SessionId == update.SessionId);
            Sessions[Sessions.IndexOf(session)] = session with
            {
                LastPositionSeconds = update.LastPositionSeconds,
                MaxPositionSeconds = update.MaxPositionSeconds,
                HeardSeconds = session.HeardSeconds + update.HeardSecondsDelta,
                LastObservedUtc = update.LastObservedUtc,
                LastObservedAudioBadgeRaw = update.LastObservedAudioBadgeRaw,
                LastObservedAudioVariant = update.LastObservedAudioVariant,
                State = update.State,
                PauseCount = update.PauseCount ?? session.PauseCount,
                ResumeCount = update.ResumeCount ?? session.ResumeCount
            };
            return Task.CompletedTask;
        }

        public Task AppendEventAsync(SessionEventRecord sessionEvent, CancellationToken cancellationToken)
        {
            Events.Add(sessionEvent);
            return Task.CompletedTask;
        }

        public Task CloseSessionAsync(SessionClosure closure, CancellationToken cancellationToken)
        {
            ClosedSessions.Add(closure);
            var session = Sessions.Single(s => s.SessionId == closure.SessionId);
            Sessions[Sessions.IndexOf(session)] = session with
            {
                EndedAtUtc = closure.EndedAtUtc,
                LastPositionSeconds = closure.LastPositionSeconds,
                HeardSeconds = closure.HeardSeconds,
                LastObservedUtc = closure.LastObservedUtc,
                State = SessionState.Closed,
                EndReason = closure.Reason
            };
            return Task.CompletedTask;
        }

        public Task<TrackerStatistics> GetStatisticsAsync(CancellationToken cancellationToken)
            => Task.FromResult(new TrackerStatistics(_tracks.Count, Sessions.Count, Sessions.Count(s => s.State != SessionState.Closed), Sessions.LastOrDefault()?.LastObservedUtc));

        public Task<TrackDetailsRecord?> GetTrackDetailsAsync(long trackId, CancellationToken cancellationToken)
        {
            var track = _tracks.Values.SingleOrDefault(item => item.TrackId == trackId);
            if (track is null)
            {
                return Task.FromResult<TrackDetailsRecord?>(null);
            }

            _metadata.TryGetValue(trackId, out var metadata);
            return Task.FromResult<TrackDetailsRecord?>(new TrackDetailsRecord(track, metadata));
        }

        public Task<IReadOnlyList<TrackHistoryRow>> GetTrackHistoryAsync(CancellationToken cancellationToken)
            => Task.FromResult((IReadOnlyList<TrackHistoryRow>)Array.Empty<TrackHistoryRow>());

        public Task<IReadOnlyList<ExportSessionRow>> ExportSessionsAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken cancellationToken)
            => Task.FromResult((IReadOnlyList<ExportSessionRow>)Array.Empty<ExportSessionRow>());

        public Task<IReadOnlyList<ExportTrackRow>> ExportTracksAsync(CancellationToken cancellationToken)
            => Task.FromResult((IReadOnlyList<ExportTrackRow>)Array.Empty<ExportTrackRow>());

        public Task<IReadOnlyList<SessionEventRecord>> GetSessionEventsAsync(long sessionId, CancellationToken cancellationToken)
            => Task.FromResult((IReadOnlyList<SessionEventRecord>)Array.Empty<SessionEventRecord>());
    }
}
