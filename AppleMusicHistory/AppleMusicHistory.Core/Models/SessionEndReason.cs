namespace AppleMusicHistory.Core.Models;

public enum SessionEndReason
{
    TrackChanged = 1,
    GapTimeout = 2,
    Replayed = 3,
    AppClosed = 4,
    TrackerStopped = 5,
    TrackingPaused = 6,
    RecoveredAfterCrash = 7
}
