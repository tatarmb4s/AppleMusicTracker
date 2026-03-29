using AppleMusicHistory.Core.Models;

namespace AppleMusicHistory.Host;

public sealed record RuntimeStatus(
    bool IsTrackingPaused,
    AppleMusicSnapshotReadState SourceState,
    PlaybackSnapshot? CurrentSnapshot,
    string? SourceDiagnosticMessage,
    ListeningSessionRecord? ActiveSession,
    TrackDetailsRecord? CurrentTrackDetails,
    TrackerStatistics Statistics);
