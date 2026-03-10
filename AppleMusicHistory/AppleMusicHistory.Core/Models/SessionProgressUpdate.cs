namespace AppleMusicHistory.Core.Models;

public sealed record SessionProgressUpdate(
    long SessionId,
    int LastPositionSeconds,
    int MaxPositionSeconds,
    double HeardSecondsDelta,
    DateTimeOffset LastObservedUtc,
    SessionState State,
    int? PauseCount = null,
    int? ResumeCount = null,
    string? LastObservedAudioBadgeRaw = null,
    PlaybackAudioVariant? LastObservedAudioVariant = null);
