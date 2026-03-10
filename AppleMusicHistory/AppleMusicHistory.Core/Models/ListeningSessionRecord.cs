namespace AppleMusicHistory.Core.Models;

public sealed record ListeningSessionRecord(
    long SessionId,
    long TrackId,
    long AppRunId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    int FirstPositionSeconds,
    int LastPositionSeconds,
    int MaxPositionSeconds,
    double HeardSeconds,
    int PauseCount,
    int ResumeCount,
    int ReplayIndex,
    SessionState State,
    SessionEndReason? EndReason,
    DateTimeOffset LastObservedUtc);
