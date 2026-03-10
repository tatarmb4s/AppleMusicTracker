using AppleMusicHistory.Core.Models;

namespace AppleMusicHistory.App;

public sealed record RuntimeStatus(
    bool IsTrackingPaused,
    PlaybackSnapshot? CurrentSnapshot,
    ListeningSessionRecord? ActiveSession,
    TrackerStatistics Statistics);
