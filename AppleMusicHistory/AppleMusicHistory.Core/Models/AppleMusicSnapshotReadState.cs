namespace AppleMusicHistory.Core.Models;

public enum AppleMusicSnapshotReadState
{
    Available = 1,
    AppNotRunning = 2,
    NoTrackDetected = 3,
    Recovering = 4
}
