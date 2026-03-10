namespace AppleMusicHistory.Core.Models;

public sealed record SessionClosure(
    long SessionId,
    DateTimeOffset EndedAtUtc,
    int LastPositionSeconds,
    double HeardSeconds,
    DateTimeOffset LastObservedUtc,
    SessionEndReason Reason);
