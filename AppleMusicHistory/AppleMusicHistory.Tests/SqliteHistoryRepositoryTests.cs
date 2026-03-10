using AppleMusicHistory.Core.Models;
using AppleMusicHistory.Infrastructure.Data;
using Microsoft.Data.Sqlite;

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
            new TrackUpsert(fingerprint, "Song", "Artist", "Album", "Artist — Album", observedAt, 180, CatalogAudioVariantsJson: "[\"Lossless\"]", LastObservedAudioBadgeRaw: "Dolby Audio", LastObservedAudioVariant: PlaybackAudioVariant.DolbyAudio),
            CancellationToken.None);
        var second = await repository.UpsertTrackAsync(
            new TrackUpsert(fingerprint, "Song", "Artist", "Album", "Artist — Album", observedAt.AddMinutes(1), 180, CatalogAudioVariantsJson: "[\"Lossless\"]", LastObservedAudioBadgeRaw: "Lossless", LastObservedAudioVariant: PlaybackAudioVariant.Lossless),
            CancellationToken.None);

        Assert.Equal(first.TrackId, second.TrackId);

        var session = await repository.StartSessionAsync(
            new StartSessionRequest(first.TrackId, appRunId, observedAt, 0, 0, observedAt, SessionState.Playing, "Dolby Audio", PlaybackAudioVariant.DolbyAudio),
            CancellationToken.None);
        await repository.UpdateSessionProgressAsync(
            new SessionProgressUpdate(
                session.SessionId,
                30,
                30,
                30,
                observedAt.AddSeconds(30),
                SessionState.Playing,
                LastObservedAudioBadgeRaw: "Lossless",
                LastObservedAudioVariant: PlaybackAudioVariant.Lossless),
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
        Assert.Equal("[\"Lossless\"]", exports[0].CatalogAudioVariantsJson);
        Assert.Equal("Lossless", exports[0].LastObservedAudioBadgeRaw);
        Assert.Equal(PlaybackAudioVariant.Lossless, exports[0].LastObservedAudioVariant);
        Assert.Equal("Lossless", second.LastObservedAudioBadgeRaw);
        Assert.Equal(PlaybackAudioVariant.Lossless, second.LastObservedAudioVariant);
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
            new StartSessionRequest(track.TrackId, appRunId, DateTimeOffset.UtcNow, 0, 0, DateTimeOffset.UtcNow, SessionState.Playing, null, null),
            CancellationToken.None);

        await repository.RecoverOpenSessionsAsync(DateTimeOffset.UtcNow, SessionEndReason.RecoveredAfterCrash, CancellationToken.None);
        var exports = await repository.ExportSessionsAsync(null, null, CancellationToken.None);

        Assert.Single(exports);
        Assert.Equal(SessionEndReason.RecoveredAfterCrash, exports[0].EndReason);
        Assert.Equal(SessionState.Closed, exports[0].State);
    }

    [Fact]
    public async Task UpsertTrack_CanClearLiveAudioObservation()
    {
        var repository = new SqliteHistoryRepository(_databasePath);
        await repository.InitializeAsync(CancellationToken.None);

        var observedAt = DateTimeOffset.Parse("2026-03-09T12:00:00Z");
        var fingerprint = TrackFingerprint.From("Song", "Artist", "Album");

        await repository.UpsertTrackAsync(
            new TrackUpsert(fingerprint, "Song", "Artist", "Album", "Artist — Album", observedAt, 180, LastObservedAudioBadgeRaw: "Dolby Audio", LastObservedAudioVariant: PlaybackAudioVariant.DolbyAudio),
            CancellationToken.None);
        var cleared = await repository.UpsertTrackAsync(
            new TrackUpsert(fingerprint, "Song", "Artist", "Album", "Artist — Album", observedAt.AddMinutes(1), 180, LastObservedAudioBadgeRaw: null, LastObservedAudioVariant: null),
            CancellationToken.None);

        Assert.Null(cleared.LastObservedAudioBadgeRaw);
        Assert.Null(cleared.LastObservedAudioVariant);
    }

    [Fact]
    public async Task InitializeAsync_UpgradesSchemaToVersion3()
    {
        var repository = new SqliteHistoryRepository(_databasePath);
        await repository.InitializeAsync(CancellationToken.None);

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWrite
        }.ToString());
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(await command.ExecuteScalarAsync());

        Assert.Equal(3, version);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
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
