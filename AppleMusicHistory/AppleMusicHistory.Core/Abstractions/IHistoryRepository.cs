using AppleMusicHistory.Core.Models;

namespace AppleMusicHistory.Core.Abstractions;

public interface IHistoryRepository
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<long> StartAppRunAsync(AppRunInfo appRun, CancellationToken cancellationToken);
    Task RecoverOpenSessionsAsync(DateTimeOffset recoveredAtUtc, SessionEndReason reason, CancellationToken cancellationToken);
    Task<TrackRecord> UpsertTrackAsync(TrackUpsert track, CancellationToken cancellationToken);
    Task<int> GetNextReplayIndexAsync(long trackId, CancellationToken cancellationToken);
    Task<ListeningSessionRecord> StartSessionAsync(StartSessionRequest session, CancellationToken cancellationToken);
    Task UpdateSessionProgressAsync(SessionProgressUpdate update, CancellationToken cancellationToken);
    Task AppendEventAsync(SessionEventRecord sessionEvent, CancellationToken cancellationToken);
    Task CloseSessionAsync(SessionClosure closure, CancellationToken cancellationToken);
    Task<TrackerStatistics> GetStatisticsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ExportSessionRow>> ExportSessionsAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<SessionEventRecord>> GetSessionEventsAsync(long sessionId, CancellationToken cancellationToken);
}
