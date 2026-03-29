using AppleMusicHistory.Core.Abstractions;
using AppleMusicHistory.Core.Models;

namespace AppleMusicHistory.Tests;

internal sealed class TestHistoryRepository : IHistoryRepository
{
    private long _nextTrackId = 1;
    private long _nextSessionId = 1;
    private readonly Dictionary<string, TrackRecord> _tracks = new();
    private readonly Dictionary<long, TrackMetadataRecord> _trackMetadata = new();

    public List<ListeningSessionRecord> Sessions { get; } = [];
    public List<SessionClosure> ClosedSessions { get; } = [];
    public List<SessionEventRecord> Events { get; } = [];

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<long> StartAppRunAsync(AppRunInfo appRun, CancellationToken cancellationToken) => Task.FromResult(1L);

    public Task RecoverOpenSessionsAsync(DateTimeOffset recoveredAtUtc, SessionEndReason reason, CancellationToken cancellationToken)
        => Task.CompletedTask;

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
        _trackMetadata[trackId] = new TrackMetadataRecord(
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
        => Task.FromResult(new TrackerStatistics(
            _tracks.Count,
            Sessions.Count,
            Sessions.Count(s => s.State != SessionState.Closed),
            Sessions.LastOrDefault()?.LastObservedUtc));

    public Task<TrackDetailsRecord?> GetTrackDetailsAsync(long trackId, CancellationToken cancellationToken)
    {
        var track = _tracks.Values.SingleOrDefault(item => item.TrackId == trackId);
        if (track is null)
        {
            return Task.FromResult<TrackDetailsRecord?>(null);
        }

        _trackMetadata.TryGetValue(trackId, out var metadata);
        return Task.FromResult<TrackDetailsRecord?>(new TrackDetailsRecord(track, metadata));
    }

    public Task<IReadOnlyList<ExportSessionRow>> ExportSessionsAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken cancellationToken)
        => Task.FromResult((IReadOnlyList<ExportSessionRow>)Array.Empty<ExportSessionRow>());

    public Task<IReadOnlyList<ExportTrackRow>> ExportTracksAsync(CancellationToken cancellationToken)
        => Task.FromResult((IReadOnlyList<ExportTrackRow>)Array.Empty<ExportTrackRow>());

    public Task<IReadOnlyList<SessionEventRecord>> GetSessionEventsAsync(long sessionId, CancellationToken cancellationToken)
        => Task.FromResult((IReadOnlyList<SessionEventRecord>)Array.Empty<SessionEventRecord>());
}

internal sealed class NoOpMetadataEnricher : ITrackMetadataEnricher
{
    public Task<TrackEnrichmentResult?> EnrichAsync(TrackFingerprint fingerprint, CancellationToken cancellationToken)
        => Task.FromResult<TrackEnrichmentResult?>(null);
}
