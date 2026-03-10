namespace AppleMusicHistory.Core.Models;

public sealed record TrackerStatistics(
    int TrackCount,
    int SessionCount,
    int OpenSessionCount,
    DateTimeOffset? LastObservedUtc);
