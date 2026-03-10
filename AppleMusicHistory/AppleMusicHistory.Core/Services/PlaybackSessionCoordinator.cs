using AppleMusicHistory.Core.Abstractions;
using AppleMusicHistory.Core.Models;
using System.Text.Json;

namespace AppleMusicHistory.Core.Services;

public sealed class PlaybackSessionCoordinator
{
    private readonly IHistoryRepository _repository;
    private readonly ITrackMetadataEnricher? _metadataEnricher;
    private readonly TrackerOptions _options;
    private readonly long _appRunId;
    private ActiveSessionState? _active;

    public PlaybackSessionCoordinator(
        IHistoryRepository repository,
        TrackerOptions options,
        long appRunId,
        ITrackMetadataEnricher? metadataEnricher = null)
    {
        _repository = repository;
        _options = options;
        _appRunId = appRunId;
        _metadataEnricher = options.MetadataEnrichmentEnabled ? metadataEnricher : null;
    }

    public ListeningSessionRecord? ActiveSession => _active?.Record;

    public async Task HandleSnapshotAsync(PlaybackSnapshot? snapshot, CancellationToken cancellationToken)
    {
        if (snapshot is null)
        {
            await CloseActiveSessionAsync(SessionEndReason.AppClosed, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_active is null)
        {
            await StartSessionAsync(snapshot, 0, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_active.Fingerprint != snapshot.Fingerprint)
        {
            await CloseActiveSessionAsync(SessionEndReason.TrackChanged, cancellationToken).ConfigureAwait(false);
            await StartSessionAsync(snapshot, 0, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (DetectReplay(_active, snapshot))
        {
            var replayIndex = _active.Record.ReplayIndex + 1;
            await CloseActiveSessionAsync(SessionEndReason.Replayed, cancellationToken).ConfigureAwait(false);
            await StartSessionAsync(snapshot, replayIndex, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (snapshot.IsPaused)
        {
            await HandlePausedSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
            return;
        }

        await HandlePlayingSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => CloseActiveSessionAsync(SessionEndReason.TrackerStopped, cancellationToken);

    public Task PauseTrackingAsync(CancellationToken cancellationToken) => CloseActiveSessionAsync(SessionEndReason.TrackingPaused, cancellationToken);

    private async Task StartSessionAsync(PlaybackSnapshot snapshot, int replayIndex, CancellationToken cancellationToken)
    {
        var track = await _repository.UpsertTrackAsync(
            new TrackUpsert(
                snapshot.Fingerprint,
                snapshot.Title,
                snapshot.Artist,
                snapshot.Album,
                snapshot.Subtitle,
                snapshot.ObservedAtUtc,
                snapshot.DurationSeconds,
                CatalogAudioVariantsJson: null),
            cancellationToken).ConfigureAwait(false);

        if (_metadataEnricher is not null)
        {
            _ = Task.Run(() => EnrichTrackAsync(track, snapshot.Fingerprint), CancellationToken.None);
        }

        if (replayIndex == 0)
        {
            replayIndex = await _repository.GetNextReplayIndexAsync(track.TrackId, cancellationToken).ConfigureAwait(false);
        }

        var position = snapshot.CurrentPositionSeconds ?? 0;
        var record = await _repository.StartSessionAsync(
            new StartSessionRequest(
                track.TrackId,
                _appRunId,
                snapshot.ObservedAtUtc,
                position,
                replayIndex,
                snapshot.ObservedAtUtc,
                snapshot.IsPaused ? SessionState.Paused : SessionState.Playing,
                snapshot.ObservedAudioBadgeRaw,
                snapshot.ObservedAudioVariant),
            cancellationToken).ConfigureAwait(false);

        _active = new ActiveSessionState(record, track, snapshot.Fingerprint)
        {
            LastSnapshot = snapshot,
            LastCheckpointUtc = snapshot.ObservedAtUtc,
            PauseStartedUtc = snapshot.IsPaused ? snapshot.ObservedAtUtc : null
        };

        await _repository.AppendEventAsync(
            new SessionEventRecord(record.SessionId, SessionEventType.SessionStarted, snapshot.ObservedAtUtc, position),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandlePausedSnapshotAsync(PlaybackSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (_active is null)
        {
            return;
        }

        var position = snapshot.CurrentPositionSeconds ?? _active.Record.LastPositionSeconds;
        var maxPosition = Math.Max(_active.Record.MaxPositionSeconds, position);
        var previousAudioBadgeRaw = _active.Record.LastObservedAudioBadgeRaw;
        var previousAudioVariant = _active.Record.LastObservedAudioVariant;

        if (_active.Record.State != SessionState.Paused)
        {
            _active.PauseStartedUtc = snapshot.ObservedAtUtc;
            _active.Record = _active.Record with
            {
                PauseCount = _active.Record.PauseCount + 1,
                LastPositionSeconds = position,
                MaxPositionSeconds = maxPosition,
                LastObservedUtc = snapshot.ObservedAtUtc,
                State = SessionState.Paused,
                LastObservedAudioBadgeRaw = snapshot.ObservedAudioBadgeRaw,
                LastObservedAudioVariant = snapshot.ObservedAudioVariant
            };

            await _repository.UpdateSessionProgressAsync(
                new SessionProgressUpdate(
                    _active.Record.SessionId,
                    position,
                    maxPosition,
                    0,
                    snapshot.ObservedAtUtc,
                    SessionState.Paused,
                    PauseCount: _active.Record.PauseCount,
                    LastObservedAudioBadgeRaw: snapshot.ObservedAudioBadgeRaw,
                    LastObservedAudioVariant: snapshot.ObservedAudioVariant),
                cancellationToken).ConfigureAwait(false);

            await _repository.AppendEventAsync(
                new SessionEventRecord(_active.Record.SessionId, SessionEventType.Paused, snapshot.ObservedAtUtc, position),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _active.Record = _active.Record with
            {
                LastPositionSeconds = position,
                MaxPositionSeconds = maxPosition,
                LastObservedUtc = snapshot.ObservedAtUtc,
                LastObservedAudioBadgeRaw = snapshot.ObservedAudioBadgeRaw,
                LastObservedAudioVariant = snapshot.ObservedAudioVariant
            };

            await _repository.UpdateSessionProgressAsync(
                new SessionProgressUpdate(
                    _active.Record.SessionId,
                    position,
                    maxPosition,
                    0,
                    snapshot.ObservedAtUtc,
                    SessionState.Paused,
                    LastObservedAudioBadgeRaw: snapshot.ObservedAudioBadgeRaw,
                    LastObservedAudioVariant: snapshot.ObservedAudioVariant),
                cancellationToken).ConfigureAwait(false);
        }

        await AppendAudioVariantChangeEventAsync(previousAudioBadgeRaw, previousAudioVariant, snapshot, position, cancellationToken).ConfigureAwait(false);
        _active.LastSnapshot = snapshot;
    }

    private async Task HandlePlayingSnapshotAsync(PlaybackSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (_active is null)
        {
            return;
        }

        if (_active.Record.State == SessionState.Paused && _active.PauseStartedUtc.HasValue)
        {
            if (snapshot.ObservedAtUtc - _active.PauseStartedUtc.Value > _options.ResumeGap)
            {
                await CloseActiveSessionAsync(SessionEndReason.GapTimeout, cancellationToken).ConfigureAwait(false);
                await StartSessionAsync(snapshot, 0, cancellationToken).ConfigureAwait(false);
                return;
            }

            _active.Record = _active.Record with { ResumeCount = _active.Record.ResumeCount + 1 };
            await _repository.AppendEventAsync(
                new SessionEventRecord(_active.Record.SessionId, SessionEventType.Resumed, snapshot.ObservedAtUtc, snapshot.CurrentPositionSeconds ?? 0),
                cancellationToken).ConfigureAwait(false);
        }

        var position = snapshot.CurrentPositionSeconds ?? _active.Record.LastPositionSeconds;
        var maxPosition = Math.Max(_active.Record.MaxPositionSeconds, position);
        var heardDelta = CalculateHeardSecondsDelta(_active, snapshot);
        var previousAudioBadgeRaw = _active.Record.LastObservedAudioBadgeRaw;
        var previousAudioVariant = _active.Record.LastObservedAudioVariant;

        _active.Record = _active.Record with
        {
            LastPositionSeconds = position,
            MaxPositionSeconds = maxPosition,
            HeardSeconds = _active.Record.HeardSeconds + heardDelta,
            LastObservedUtc = snapshot.ObservedAtUtc,
            State = SessionState.Playing,
            LastObservedAudioBadgeRaw = snapshot.ObservedAudioBadgeRaw,
            LastObservedAudioVariant = snapshot.ObservedAudioVariant
        };

        await _repository.UpdateSessionProgressAsync(
            new SessionProgressUpdate(
                _active.Record.SessionId,
                position,
                maxPosition,
                heardDelta,
                snapshot.ObservedAtUtc,
                SessionState.Playing,
                ResumeCount: _active.Record.ResumeCount,
                LastObservedAudioBadgeRaw: snapshot.ObservedAudioBadgeRaw,
                LastObservedAudioVariant: snapshot.ObservedAudioVariant),
            cancellationToken).ConfigureAwait(false);

        if (snapshot.ObservedAtUtc - _active.LastCheckpointUtc >= _options.CheckpointInterval)
        {
            _active.LastCheckpointUtc = snapshot.ObservedAtUtc;
            await _repository.AppendEventAsync(
                new SessionEventRecord(_active.Record.SessionId, SessionEventType.ProgressCheckpoint, snapshot.ObservedAtUtc, position),
                cancellationToken).ConfigureAwait(false);
        }

        await AppendAudioVariantChangeEventAsync(previousAudioBadgeRaw, previousAudioVariant, snapshot, position, cancellationToken).ConfigureAwait(false);
        _active.PauseStartedUtc = null;
        _active.LastSnapshot = snapshot;
    }

    private async Task CloseActiveSessionAsync(SessionEndReason reason, CancellationToken cancellationToken)
    {
        if (_active is null)
        {
            return;
        }

        var endedAt = _active.LastSnapshot?.ObservedAtUtc ?? _active.Record.LastObservedUtc;

        await _repository.CloseSessionAsync(
            new SessionClosure(
                _active.Record.SessionId,
                endedAt,
                _active.Record.LastPositionSeconds,
                _active.Record.HeardSeconds,
                endedAt,
                reason),
            cancellationToken).ConfigureAwait(false);

        await _repository.AppendEventAsync(
            new SessionEventRecord(_active.Record.SessionId, SessionEventType.SessionEnded, endedAt, _active.Record.LastPositionSeconds, reason.ToString()),
            cancellationToken).ConfigureAwait(false);

        _active = null;
    }

    private async Task EnrichTrackAsync(TrackRecord track, TrackFingerprint fingerprint)
    {
        try
        {
            var metadata = await _metadataEnricher!.EnrichAsync(fingerprint, CancellationToken.None).ConfigureAwait(false);
            if (metadata is null)
            {
                return;
            }

            await _repository.UpsertTrackAsync(
                new TrackUpsert(
                    fingerprint,
                    track.Title,
                    track.Artist,
                    track.Album,
                    track.Subtitle,
                    DateTimeOffset.UtcNow,
                    metadata.DurationSeconds ?? track.DurationSeconds,
                    metadata.SongUrl ?? track.SongUrl,
                    metadata.ArtistUrl ?? track.ArtistUrl,
                    metadata.ArtworkUrl ?? track.ArtworkUrl,
                    metadata.CatalogAudioVariantsJson ?? track.CatalogAudioVariantsJson,
                    metadata.EnrichedAtUtc),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Capture must continue even when enrichment fails.
        }
    }

    private static double CalculateHeardSecondsDelta(ActiveSessionState active, PlaybackSnapshot snapshot)
    {
        if (active.LastSnapshot is null || active.LastSnapshot.IsPaused)
        {
            return 0;
        }

        var wallClockDelta = snapshot.ObservedAtUtc - active.LastSnapshot.ObservedAtUtc;
        if (wallClockDelta <= TimeSpan.Zero)
        {
            return 0;
        }

        if (snapshot.CurrentPositionSeconds is null || active.LastSnapshot.CurrentPositionSeconds is null)
        {
            return wallClockDelta.TotalSeconds;
        }

        var progressDelta = snapshot.CurrentPositionSeconds.Value - active.LastSnapshot.CurrentPositionSeconds.Value;
        if (progressDelta <= 0)
        {
            return 0;
        }

        return Math.Min(wallClockDelta.TotalSeconds, progressDelta);
    }

    private bool DetectReplay(ActiveSessionState active, PlaybackSnapshot snapshot)
    {
        if (active.LastSnapshot?.CurrentPositionSeconds is null || snapshot.CurrentPositionSeconds is null)
        {
            return false;
        }

        if (snapshot.CurrentPositionSeconds.Value >= active.LastSnapshot.CurrentPositionSeconds.Value)
        {
            return false;
        }

        var duration = snapshot.DurationSeconds ?? active.LastSnapshot.DurationSeconds;
        if (duration is null)
        {
            return false;
        }

        var wasNearEnd = active.LastSnapshot.CurrentPositionSeconds.Value >= duration.Value - (int)_options.ReplayEndThreshold.TotalSeconds;
        var nowNearStart = snapshot.CurrentPositionSeconds.Value <= (int)_options.ReplayStartThreshold.TotalSeconds;
        return wasNearEnd && nowNearStart;
    }

    private async Task AppendAudioVariantChangeEventAsync(
        string? previousAudioBadgeRaw,
        PlaybackAudioVariant? previousAudioVariant,
        PlaybackSnapshot snapshot,
        int position,
        CancellationToken cancellationToken)
    {
        if (_active is null || !HasObservedAudioChanged(previousAudioBadgeRaw, previousAudioVariant, snapshot))
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            previousRaw = previousAudioBadgeRaw,
            previousVariant = previousAudioVariant?.ToString(),
            currentRaw = snapshot.ObservedAudioBadgeRaw,
            currentVariant = snapshot.ObservedAudioVariant?.ToString(),
            observedAtUtc = snapshot.ObservedAtUtc
        });

        await _repository.AppendEventAsync(
            new SessionEventRecord(_active.Record.SessionId, SessionEventType.AudioVariantChanged, snapshot.ObservedAtUtc, position, payload),
            cancellationToken).ConfigureAwait(false);
    }

    private static bool HasObservedAudioChanged(string? previousAudioBadgeRaw, PlaybackAudioVariant? previousAudioVariant, PlaybackSnapshot snapshot)
    {
        return !string.Equals(previousAudioBadgeRaw, snapshot.ObservedAudioBadgeRaw, StringComparison.Ordinal)
            || previousAudioVariant != snapshot.ObservedAudioVariant;
    }

    private sealed class ActiveSessionState
    {
        public ActiveSessionState(ListeningSessionRecord record, TrackRecord track, TrackFingerprint fingerprint)
        {
            Record = record;
            Track = track;
            Fingerprint = fingerprint;
        }

        public ListeningSessionRecord Record { get; set; }
        public TrackRecord Track { get; }
        public TrackFingerprint Fingerprint { get; }
        public PlaybackSnapshot? LastSnapshot { get; set; }
        public DateTimeOffset LastCheckpointUtc { get; set; }
        public DateTimeOffset? PauseStartedUtc { get; set; }
    }
}
