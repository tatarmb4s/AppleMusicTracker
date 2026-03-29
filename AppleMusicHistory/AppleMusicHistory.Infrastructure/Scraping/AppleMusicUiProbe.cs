namespace AppleMusicHistory.Infrastructure.Scraping;

internal interface IAppleMusicUiProbe
{
    AppleMusicProcessInfo? FindProcess();

    AppleMusicProbeReadOutcome ReadPlayback(int processId, CancellationToken cancellationToken);

    bool IsProcessRunning(int processId);
}

internal sealed record AppleMusicProcessInfo(int ProcessId);

internal enum AppleMusicProbeReadState
{
    Available = 1,
    AppNotRunning = 2,
    NoTrackDetected = 3,
    Recovering = 4
}

internal sealed record AppleMusicProbeReadOutcome(
    AppleMusicProbeReadState State,
    AppleMusicProbeSnapshotData? SnapshotData = null,
    string? DiagnosticMessage = null);

internal sealed record AppleMusicProbeSnapshotData(
    string Title,
    string Artist,
    string Album,
    string Subtitle,
    DateTimeOffset ObservedAtUtc,
    string? PlayPauseButtonName,
    double SongProgressPercent,
    int? CurrentPositionSeconds,
    int? DurationSeconds,
    string? ObservedAudioBadgeRaw,
    string? SourceDescription);
