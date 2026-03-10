namespace AppleMusicHistory.Core.Models;

public sealed record SessionEventRecord(
    long SessionId,
    SessionEventType EventType,
    DateTimeOffset ObservedAtUtc,
    int PositionSeconds,
    string? PayloadJson = null);
