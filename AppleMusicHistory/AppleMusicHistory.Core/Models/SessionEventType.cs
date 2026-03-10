namespace AppleMusicHistory.Core.Models;

public enum SessionEventType
{
    SessionStarted = 1,
    ProgressCheckpoint = 2,
    Paused = 3,
    Resumed = 4,
    SessionEnded = 5
}
