using AppleMusicHistory.Core.Models;
using AppleMusicHistory.Infrastructure.Data;

namespace AppleMusicHistory.Tests;

public sealed class SqliteHistoryRepositoryTests : IDisposable
{
    private readonly string _databasePath;

    public SqliteHistoryRepositoryTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite");
    }

    [Fact]
    public async Task UpsertTrack_IsIdempotent_AndExportIncludesClosedSession()
    {
        var repository = new SqliteHistoryRepository(_databasePath);
        await repository.InitializeAsync(CancellationToken.None);

        var appRunId = await repository.StartAppRunAsync(
            new AppRunInfo(DateTimeOffset.UtcNow, "test", "machine", "user", ".NET", "Windows"),
            CancellationToken.None);

        var observedAt = DateTimeOffset.Parse("2026-03-09T12:00:00Z");
        var fingerprint = TrackFingerprint.From("Song", "Artist", "Album");
        var first = await repository.UpsertTrackAsync(
            new TrackUpsert(fingerprint, "Song", "Artist", "Album", "Artist — Album", observedAt, 180),
            CancellationToken.None);
        var second = await repository.UpsertTrackAsync(
            new TrackUpsert(fingerprint, "Song", "Artist", "Album", "Artist — Album", observedAt.AddMinutes(1), 180),
            CancellationToken.None);

        Assert.Equal(first.TrackId, second.TrackId);

        var session = await repository.StartSessionAsync(
            new StartSessionRequest(first.TrackId, appRunId, observedAt, 0, 0, observedAt, SessionState.Playing),
            CancellationToken.None);
        await repository.UpdateSessionProgressAsync(
            new SessionProgressUpdate(session.SessionId, 30, 30, 30, observedAt.AddSeconds(30), SessionState.Playing),
            CancellationToken.None);
        await repository.AppendEventAsync(
            new SessionEventRecord(session.SessionId, SessionEventType.ProgressCheckpoint, observedAt.AddSeconds(30), 30),
            CancellationToken.None);
        await repository.CloseSessionAsync(
            new SessionClosure(session.SessionId, observedAt.AddSeconds(30), 30, 30, observedAt.AddSeconds(30), SessionEndReason.TrackChanged),
            CancellationToken.None);

        var exports = await repository.ExportSessionsAsync(null, null, CancellationToken.None);
        var events = await repository.GetSessionEventsAsync(session.SessionId, CancellationToken.None);

        Assert.Single(exports);
        Assert.Single(events);
        Assert.Equal("Song", exports[0].Title);
        Assert.Equal(SessionEndReason.TrackChanged, exports[0].EndReason);
    }

    [Fact]
    public async Task RecoverOpenSessions_ClosesLingeringRows()
    {
        var repository = new SqliteHistoryRepository(_databasePath);
        await repository.InitializeAsync(CancellationToken.None);

        var appRunId = await repository.StartAppRunAsync(
            new AppRunInfo(DateTimeOffset.UtcNow, "test", "machine", "user", ".NET", "Windows"),
            CancellationToken.None);

        var fingerprint = TrackFingerprint.From("Song", "Artist", "Album");
        var track = await repository.UpsertTrackAsync(
            new TrackUpsert(fingerprint, "Song", "Artist", "Album", "Artist — Album", DateTimeOffset.UtcNow, 180),
            CancellationToken.None);

        await repository.StartSessionAsync(
            new StartSessionRequest(track.TrackId, appRunId, DateTimeOffset.UtcNow, 0, 0, DateTimeOffset.UtcNow, SessionState.Playing),
            CancellationToken.None);

        await repository.RecoverOpenSessionsAsync(DateTimeOffset.UtcNow, SessionEndReason.RecoveredAfterCrash, CancellationToken.None);
        var exports = await repository.ExportSessionsAsync(null, null, CancellationToken.None);

        Assert.Single(exports);
        Assert.Equal(SessionEndReason.RecoveredAfterCrash, exports[0].EndReason);
        Assert.Equal(SessionState.Closed, exports[0].State);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            try
            {
                File.Delete(_databasePath);
            }
            catch (IOException)
            {
            }
        }
    }
}
