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
    public async Task UpsertTrackMetadata_AndExportTracks_RoundTripsMetadata()
    {
        var repository = new SqliteHistoryRepository(_databasePath);
        await repository.InitializeAsync(CancellationToken.None);

        var observedAt = DateTimeOffset.Parse("2026-03-09T12:00:00Z");
        var fingerprint = TrackFingerprint.From("Song", "Artist", "Album");
        var track = await repository.UpsertTrackAsync(
            new TrackUpsert(fingerprint, "Song", "Artist", "Album", "Artist - Album", observedAt, 180),
            CancellationToken.None);

        await repository.UpsertTrackMetadataAsync(
            track.TrackId,
            new TrackMetadataUpsert(
                "https://music.apple.com/us/song/song-id",
                "https://music.apple.com/us/album/album-id",
                "https://music.apple.com/us/artist/artist-id",
                "catalog-song",
                "catalog-album",
                "catalog-artist",
                1,
                2,
                3,
                181,
                observedAt,
                "Composer",
                "[\"Electronic\"]",
                4,
                12,
                1,
                2,
                "USRC17607839",
                "https://example.com/preview.m4a",
                "explicit",
                "[\"Lossless\"]",
                "https://example.com/artwork.jpg",
                1000,
                1000,
                Path.Combine("artwork", "cover.jpg"),
                "us",
                "[\"web\",\"itunes\"]",
                "{\"web\":true}",
                "{\"itunes\":true}",
                null,
                observedAt.AddMinutes(1),
                observedAt.AddMinutes(1)),
            CancellationToken.None);

        var details = await repository.GetTrackDetailsAsync(track.TrackId, CancellationToken.None);
        var exports = await repository.ExportTracksAsync(CancellationToken.None);

        Assert.NotNull(details);
        Assert.NotNull(details!.Metadata);
        Assert.Equal("Composer", details.Metadata!.ComposerName);
        Assert.Equal("catalog-song", details.Metadata.CatalogSongId);
        Assert.Equal("https://music.apple.com/us/album/album-id", details.Metadata.AppleMusicAlbumUrl);
        Assert.Single(exports);
        Assert.Equal("USRC17607839", exports[0].Isrc);
        Assert.Equal(Path.Combine("artwork", "cover.jpg"), exports[0].ArtworkCacheRelativePath);
    }

    [Fact]
    public async Task GetTrackHistoryAsync_ReturnsRawFields_WithMetadataUrls_OrderedByLastSeen()
    {
        var repository = new SqliteHistoryRepository(_databasePath);
        await repository.InitializeAsync(CancellationToken.None);

        var firstObservedAt = DateTimeOffset.Parse("2026-03-09T12:00:00Z");
        var secondObservedAt = DateTimeOffset.Parse("2026-03-09T12:05:00Z");

        var firstTrack = await repository.UpsertTrackAsync(
            new TrackUpsert(
                TrackFingerprint.From("First Song", "First Artist", "First Album"),
                "First Song",
                "First Artist",
                "First Album",
                "First Subtitle",
                firstObservedAt,
                180,
                SongUrl: "https://track/song",
                ArtistUrl: "https://track/artist",
                ArtworkUrl: "https://track/artwork.jpg",
                CatalogAudioVariantsJson: "[\"Lossless\"]",
                LastObservedAudioBadgeRaw: "Lossless",
                LastObservedAudioVariant: PlaybackAudioVariant.Lossless),
            CancellationToken.None);

        await repository.UpsertTrackMetadataAsync(
            firstTrack.TrackId,
            new TrackMetadataUpsert(
                "https://metadata/song",
                "https://metadata/album",
                "https://metadata/artist",
                "catalog-song",
                "catalog-album",
                "catalog-artist",
                null,
                null,
                null,
                180,
                firstObservedAt,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                "[\"Lossless\"]",
                "https://metadata/artwork.jpg",
                1000,
                1000,
                Path.Combine("artwork", "cover.jpg"),
                "us",
                "[\"catalog\"]",
                null,
                null,
                null,
                firstObservedAt.AddMinutes(1),
                firstObservedAt.AddMinutes(1)),
            CancellationToken.None);

        await repository.UpsertTrackAsync(
            new TrackUpsert(
                TrackFingerprint.From("Second Song", "Second Artist", "Second Album"),
                "Second Song",
                "Second Artist",
                "Second Album",
                "Second Subtitle",
                secondObservedAt,
                200,
                CatalogAudioVariantsJson: "[\"Dolby Atmos\"]",
                LastObservedAudioBadgeRaw: "Dolby Atmos",
                LastObservedAudioVariant: PlaybackAudioVariant.DolbyAtmos),
            CancellationToken.None);

        var rows = await repository.GetTrackHistoryAsync(CancellationToken.None);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Second Song", rows[0].Title);
        Assert.Equal("First Song", rows[1].Title);
        Assert.Equal("[\"Lossless\"]", rows[1].CatalogAudioVariantsJson);
        Assert.Equal("Lossless", rows[1].LastObservedAudioBadgeRaw);
        Assert.Equal(PlaybackAudioVariant.Lossless, rows[1].LastObservedAudioVariant);
        Assert.Equal("https://metadata/song", rows[1].SongUrl);
        Assert.Equal("https://metadata/album", rows[1].AlbumUrl);
        Assert.Equal("https://metadata/artist", rows[1].ArtistUrl);
        Assert.Equal("https://metadata/artwork.jpg", rows[1].ArtworkUrl);
        Assert.Equal(Path.Combine("artwork", "cover.jpg"), rows[1].ArtworkCacheRelativePath);
    }

    [Fact]
    public async Task GetTrackHistoryAsync_ReturnsTrackUrls_WhenMetadataIsMissing()
    {
        var repository = new SqliteHistoryRepository(_databasePath);
        await repository.InitializeAsync(CancellationToken.None);

        var observedAt = DateTimeOffset.Parse("2026-03-09T12:00:00Z");
        await repository.UpsertTrackAsync(
            new TrackUpsert(
                TrackFingerprint.From("Song", "Artist", "Album"),
                "Song",
                "Artist",
                "Album",
                "Subtitle",
                observedAt,
                180,
                SongUrl: "https://track/song",
                ArtistUrl: "https://track/artist",
                ArtworkUrl: "https://track/art.jpg",
                CatalogAudioVariantsJson: "[\"Lossless\"]",
                LastObservedAudioBadgeRaw: "Lossless",
                LastObservedAudioVariant: PlaybackAudioVariant.Lossless),
            CancellationToken.None);

        var rows = await repository.GetTrackHistoryAsync(CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal("https://track/song", row.SongUrl);
        Assert.Null(row.AlbumUrl);
        Assert.Equal("https://track/artist", row.ArtistUrl);
        Assert.Equal("https://track/art.jpg", row.ArtworkUrl);
        Assert.Null(row.ArtworkCacheRelativePath);
    }

    [Fact]
    public async Task InitializeAsync_UpgradesSchemaToVersion4()
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

        Assert.Equal(4, version);
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
