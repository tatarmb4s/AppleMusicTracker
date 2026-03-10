namespace AppleMusicHistory.Core.Models;

public sealed record StartSessionRequest(
    long TrackId,
    long AppRunId,
    DateTimeOffset StartedAtUtc,
    int FirstPositionSeconds,
    int ReplayIndex,
    DateTimeOffset LastObservedUtc,
    SessionState State,
    string? LastObservedAudioBadgeRaw,
    PlaybackAudioVariant? LastObservedAudioVariant);
