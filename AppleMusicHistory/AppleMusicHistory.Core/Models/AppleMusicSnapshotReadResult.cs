namespace AppleMusicHistory.Core.Models;

public sealed record AppleMusicSnapshotReadResult(
    AppleMusicSnapshotReadState State,
    PlaybackSnapshot? Snapshot,
    string? DiagnosticMessage = null)
{
    public static AppleMusicSnapshotReadResult Available(PlaybackSnapshot snapshot, string? diagnosticMessage = null)
        => new(AppleMusicSnapshotReadState.Available, snapshot, diagnosticMessage);

    public static AppleMusicSnapshotReadResult AppNotRunning(string? diagnosticMessage = null)
        => new(AppleMusicSnapshotReadState.AppNotRunning, null, diagnosticMessage);

    public static AppleMusicSnapshotReadResult NoTrackDetected(string? diagnosticMessage = null)
        => new(AppleMusicSnapshotReadState.NoTrackDetected, null, diagnosticMessage);

    public static AppleMusicSnapshotReadResult Recovering(string? diagnosticMessage = null)
        => new(AppleMusicSnapshotReadState.Recovering, null, diagnosticMessage);
}
