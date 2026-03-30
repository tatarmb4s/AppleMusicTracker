using System.Globalization;
using System.Reflection;
using AppleMusicHistory.Core.Abstractions;
using AppleMusicHistory.Core.Models;
using Microsoft.Data.Sqlite;

namespace AppleMusicHistory.Infrastructure.Data;

public sealed class SqliteHistoryRepository : IHistoryRepository
{
    private const int CurrentSchemaVersion = 4;
    private static readonly string[] MigrationResources =
    [
        "AppleMusicHistory.Infrastructure.Sql.Migrations.001_initial.sql",
        "AppleMusicHistory.Infrastructure.Sql.Migrations.002_audio_variants.sql",
        "AppleMusicHistory.Infrastructure.Sql.Migrations.003_track_audio_observation.sql",
        "AppleMusicHistory.Infrastructure.Sql.Migrations.004_track_metadata.sql"
    ];

    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SqliteHistoryRepository(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var version = await GetUserVersionAsync(connection, cancellationToken).ConfigureAwait(false);
            if (version >= CurrentSchemaVersion)
            {
                return;
            }

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            for (var index = version; index < CurrentSchemaVersion; index++)
            {
                var migrationSql = await ReadEmbeddedTextAsync(MigrationResources[index], cancellationToken).ConfigureAwait(false);
                var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = migrationSql;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var pragma = connection.CreateCommand();
            pragma.Transaction = transaction;
            pragma.CommandText = $"PRAGMA user_version = {CurrentSchemaVersion};";
            await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<long> StartAppRunAsync(AppRunInfo appRun, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO app_runs (started_at_utc, app_version, machine_name, user_name, runtime, os_version)
            VALUES (@started_at_utc, @app_version, @machine_name, @user_name, @runtime, @os_version);
            SELECT last_insert_rowid();
            """;

        return await ExecuteScalarInt64Async(
            sql,
            [
                ("@started_at_utc", ToDbDate(appRun.StartedAtUtc)),
                ("@app_version", appRun.AppVersion),
                ("@machine_name", appRun.MachineName),
                ("@user_name", appRun.UserName),
                ("@runtime", appRun.Runtime),
                ("@os_version", appRun.OsVersion)
            ],
            cancellationToken).ConfigureAwait(false);
    }

    public Task RecoverOpenSessionsAsync(DateTimeOffset recoveredAtUtc, SessionEndReason reason, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE listening_sessions
            SET ended_at_utc = COALESCE(ended_at_utc, last_observed_utc, @recovered_at_utc),
                state = @closed_state,
                end_reason = @end_reason
            WHERE state <> @closed_state;
            """;

        return ExecuteNonQueryAsync(
            sql,
            [
                ("@recovered_at_utc", ToDbDate(recoveredAtUtc)),
                ("@closed_state", (int)SessionState.Closed),
                ("@end_reason", (int)reason)
            ],
            cancellationToken);
    }

    public async Task<TrackRecord> UpsertTrackAsync(TrackUpsert track, CancellationToken cancellationToken)
    {
        const string writeSql = """
            INSERT INTO tracks (
                fingerprint, title, artist, album, subtitle,
                normalized_title, normalized_artist, normalized_album,
                duration_seconds, song_url, artist_url, artwork_url, catalog_audio_variants_json,
                last_observed_audio_badge_raw, last_observed_audio_variant,
                first_seen_utc, last_seen_utc, enriched_at_utc)
            VALUES (
                @fingerprint, @title, @artist, @album, @subtitle,
                @normalized_title, @normalized_artist, @normalized_album,
                @duration_seconds, @song_url, @artist_url, @artwork_url, @catalog_audio_variants_json,
                @last_observed_audio_badge_raw, @last_observed_audio_variant,
                @observed_at_utc, @observed_at_utc, @enriched_at_utc)
            ON CONFLICT(fingerprint) DO UPDATE SET
                title = excluded.title,
                artist = excluded.artist,
                album = excluded.album,
                subtitle = excluded.subtitle,
                duration_seconds = COALESCE(excluded.duration_seconds, tracks.duration_seconds),
                song_url = COALESCE(excluded.song_url, tracks.song_url),
                artist_url = COALESCE(excluded.artist_url, tracks.artist_url),
                artwork_url = COALESCE(excluded.artwork_url, tracks.artwork_url),
                catalog_audio_variants_json = COALESCE(excluded.catalog_audio_variants_json, tracks.catalog_audio_variants_json),
                last_observed_audio_badge_raw = excluded.last_observed_audio_badge_raw,
                last_observed_audio_variant = excluded.last_observed_audio_variant,
                last_seen_utc = excluded.last_seen_utc,
                enriched_at_utc = COALESCE(excluded.enriched_at_utc, tracks.enriched_at_utc);
            """;

        await ExecuteNonQueryAsync(
            writeSql,
            [
                ("@fingerprint", track.Fingerprint.Value),
                ("@title", track.Title),
                ("@artist", track.Artist),
                ("@album", track.Album),
                ("@subtitle", track.Subtitle),
                ("@normalized_title", track.Fingerprint.NormalizedTitle),
                ("@normalized_artist", track.Fingerprint.NormalizedArtist),
                ("@normalized_album", track.Fingerprint.NormalizedAlbum),
                ("@duration_seconds", track.DurationSeconds),
                ("@song_url", track.SongUrl),
                ("@artist_url", track.ArtistUrl),
                ("@artwork_url", track.ArtworkUrl),
                ("@catalog_audio_variants_json", track.CatalogAudioVariantsJson),
                ("@last_observed_audio_badge_raw", track.LastObservedAudioBadgeRaw),
                ("@last_observed_audio_variant", (int?)track.LastObservedAudioVariant),
                ("@observed_at_utc", ToDbDate(track.ObservedAtUtc)),
                ("@enriched_at_utc", track.EnrichedAtUtc is null ? null : ToDbDate(track.EnrichedAtUtc.Value))
            ],
            cancellationToken).ConfigureAwait(false);

        return await GetTrackByFingerprintAsync(track.Fingerprint.Value, cancellationToken).ConfigureAwait(false);
    }

    public Task UpsertTrackMetadataAsync(long trackId, TrackMetadataUpsert metadata, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO track_metadata (
                track_id, apple_music_song_url, apple_music_album_url, apple_music_artist_url,
                catalog_song_id, catalog_album_id, catalog_artist_id,
                itunes_track_id, itunes_collection_id, itunes_artist_id,
                duration_seconds, release_date_utc, composer_name, genre_names_json,
                track_number, track_count, disc_number, disc_count, isrc, preview_url,
                content_rating, catalog_audio_variants_json, artwork_url, artwork_width, artwork_height,
                artwork_cache_relative_path, storefront, metadata_sources_json,
                web_payload_json, itunes_payload_json, catalog_payload_json, enriched_at_utc, artwork_cached_at_utc)
            VALUES (
                @track_id, @apple_music_song_url, @apple_music_album_url, @apple_music_artist_url,
                @catalog_song_id, @catalog_album_id, @catalog_artist_id,
                @itunes_track_id, @itunes_collection_id, @itunes_artist_id,
                @duration_seconds, @release_date_utc, @composer_name, @genre_names_json,
                @track_number, @track_count, @disc_number, @disc_count, @isrc, @preview_url,
                @content_rating, @catalog_audio_variants_json, @artwork_url, @artwork_width, @artwork_height,
                @artwork_cache_relative_path, @storefront, @metadata_sources_json,
                @web_payload_json, @itunes_payload_json, @catalog_payload_json, @enriched_at_utc, @artwork_cached_at_utc)
            ON CONFLICT(track_id) DO UPDATE SET
                apple_music_song_url = COALESCE(excluded.apple_music_song_url, track_metadata.apple_music_song_url),
                apple_music_album_url = COALESCE(excluded.apple_music_album_url, track_metadata.apple_music_album_url),
                apple_music_artist_url = COALESCE(excluded.apple_music_artist_url, track_metadata.apple_music_artist_url),
                catalog_song_id = COALESCE(excluded.catalog_song_id, track_metadata.catalog_song_id),
                catalog_album_id = COALESCE(excluded.catalog_album_id, track_metadata.catalog_album_id),
                catalog_artist_id = COALESCE(excluded.catalog_artist_id, track_metadata.catalog_artist_id),
                itunes_track_id = COALESCE(excluded.itunes_track_id, track_metadata.itunes_track_id),
                itunes_collection_id = COALESCE(excluded.itunes_collection_id, track_metadata.itunes_collection_id),
                itunes_artist_id = COALESCE(excluded.itunes_artist_id, track_metadata.itunes_artist_id),
                duration_seconds = COALESCE(excluded.duration_seconds, track_metadata.duration_seconds),
                release_date_utc = COALESCE(excluded.release_date_utc, track_metadata.release_date_utc),
                composer_name = COALESCE(excluded.composer_name, track_metadata.composer_name),
                genre_names_json = COALESCE(excluded.genre_names_json, track_metadata.genre_names_json),
                track_number = COALESCE(excluded.track_number, track_metadata.track_number),
                track_count = COALESCE(excluded.track_count, track_metadata.track_count),
                disc_number = COALESCE(excluded.disc_number, track_metadata.disc_number),
                disc_count = COALESCE(excluded.disc_count, track_metadata.disc_count),
                isrc = COALESCE(excluded.isrc, track_metadata.isrc),
                preview_url = COALESCE(excluded.preview_url, track_metadata.preview_url),
                content_rating = COALESCE(excluded.content_rating, track_metadata.content_rating),
                catalog_audio_variants_json = COALESCE(excluded.catalog_audio_variants_json, track_metadata.catalog_audio_variants_json),
                artwork_url = COALESCE(excluded.artwork_url, track_metadata.artwork_url),
                artwork_width = COALESCE(excluded.artwork_width, track_metadata.artwork_width),
                artwork_height = COALESCE(excluded.artwork_height, track_metadata.artwork_height),
                artwork_cache_relative_path = COALESCE(excluded.artwork_cache_relative_path, track_metadata.artwork_cache_relative_path),
                storefront = COALESCE(excluded.storefront, track_metadata.storefront),
                metadata_sources_json = COALESCE(excluded.metadata_sources_json, track_metadata.metadata_sources_json),
                web_payload_json = COALESCE(excluded.web_payload_json, track_metadata.web_payload_json),
                itunes_payload_json = COALESCE(excluded.itunes_payload_json, track_metadata.itunes_payload_json),
                catalog_payload_json = COALESCE(excluded.catalog_payload_json, track_metadata.catalog_payload_json),
                enriched_at_utc = excluded.enriched_at_utc,
                artwork_cached_at_utc = COALESCE(excluded.artwork_cached_at_utc, track_metadata.artwork_cached_at_utc);
            """;

        return ExecuteNonQueryAsync(
            sql,
            [
                ("@track_id", trackId),
                ("@apple_music_song_url", metadata.AppleMusicSongUrl),
                ("@apple_music_album_url", metadata.AppleMusicAlbumUrl),
                ("@apple_music_artist_url", metadata.AppleMusicArtistUrl),
                ("@catalog_song_id", metadata.CatalogSongId),
                ("@catalog_album_id", metadata.CatalogAlbumId),
                ("@catalog_artist_id", metadata.CatalogArtistId),
                ("@itunes_track_id", metadata.ItunesTrackId),
                ("@itunes_collection_id", metadata.ItunesCollectionId),
                ("@itunes_artist_id", metadata.ItunesArtistId),
                ("@duration_seconds", metadata.DurationSeconds),
                ("@release_date_utc", metadata.ReleaseDateUtc is null ? null : ToDbDate(metadata.ReleaseDateUtc.Value)),
                ("@composer_name", metadata.ComposerName),
                ("@genre_names_json", metadata.GenreNamesJson),
                ("@track_number", metadata.TrackNumber),
                ("@track_count", metadata.TrackCount),
                ("@disc_number", metadata.DiscNumber),
                ("@disc_count", metadata.DiscCount),
                ("@isrc", metadata.Isrc),
                ("@preview_url", metadata.PreviewUrl),
                ("@content_rating", metadata.ContentRating),
                ("@catalog_audio_variants_json", metadata.CatalogAudioVariantsJson),
                ("@artwork_url", metadata.ArtworkUrl),
                ("@artwork_width", metadata.ArtworkWidth),
                ("@artwork_height", metadata.ArtworkHeight),
                ("@artwork_cache_relative_path", metadata.ArtworkCacheRelativePath),
                ("@storefront", metadata.Storefront),
                ("@metadata_sources_json", metadata.MetadataSourcesJson),
                ("@web_payload_json", metadata.WebPayloadJson),
                ("@itunes_payload_json", metadata.ItunesPayloadJson),
                ("@catalog_payload_json", metadata.CatalogPayloadJson),
                ("@enriched_at_utc", ToDbDate(metadata.EnrichedAtUtc)),
                ("@artwork_cached_at_utc", metadata.ArtworkCachedAtUtc is null ? null : ToDbDate(metadata.ArtworkCachedAtUtc.Value))
            ],
            cancellationToken);
    }

    public async Task<int> GetNextReplayIndexAsync(long trackId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT COALESCE(MAX(replay_index), -1) + 1 FROM listening_sessions WHERE track_id = @track_id;";
        return (int)await ExecuteScalarInt64Async(sql, [("@track_id", trackId)], cancellationToken).ConfigureAwait(false);
    }

    public async Task<ListeningSessionRecord> StartSessionAsync(StartSessionRequest session, CancellationToken cancellationToken)
    {
        const string insertSql = """
            INSERT INTO listening_sessions (
                track_id, app_run_id, started_at_utc, ended_at_utc,
                first_position_seconds, last_position_seconds, max_position_seconds,
                heard_seconds, pause_count, resume_count, replay_index,
                state, end_reason, last_observed_utc, last_observed_audio_badge_raw, last_observed_audio_variant)
            VALUES (
                @track_id, @app_run_id, @started_at_utc, NULL,
                @first_position_seconds, @first_position_seconds, @first_position_seconds,
                0, 0, 0, @replay_index,
                @state, NULL, @last_observed_utc, @last_observed_audio_badge_raw, @last_observed_audio_variant);
            SELECT last_insert_rowid();
            """;

        var sessionId = await ExecuteScalarInt64Async(
            insertSql,
            [
                ("@track_id", session.TrackId),
                ("@app_run_id", session.AppRunId),
                ("@started_at_utc", ToDbDate(session.StartedAtUtc)),
                ("@first_position_seconds", session.FirstPositionSeconds),
                ("@replay_index", session.ReplayIndex),
                ("@state", (int)session.State),
                ("@last_observed_utc", ToDbDate(session.LastObservedUtc)),
                ("@last_observed_audio_badge_raw", session.LastObservedAudioBadgeRaw),
                ("@last_observed_audio_variant", (int?)session.LastObservedAudioVariant)
            ],
            cancellationToken).ConfigureAwait(false);

        return await GetSessionAsync("SELECT * FROM listening_sessions WHERE session_id = @session_id;", [("@session_id", sessionId)], cancellationToken).ConfigureAwait(false);
    }

    public Task UpdateSessionProgressAsync(SessionProgressUpdate update, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE listening_sessions
            SET last_position_seconds = @last_position_seconds,
                max_position_seconds = @max_position_seconds,
                heard_seconds = heard_seconds + @heard_seconds_delta,
                last_observed_utc = @last_observed_utc,
                last_observed_audio_badge_raw = @last_observed_audio_badge_raw,
                last_observed_audio_variant = @last_observed_audio_variant,
                state = @state,
                pause_count = COALESCE(@pause_count, pause_count),
                resume_count = COALESCE(@resume_count, resume_count)
            WHERE session_id = @session_id;
            """;

        return ExecuteNonQueryAsync(
            sql,
            [
                ("@session_id", update.SessionId),
                ("@last_position_seconds", update.LastPositionSeconds),
                ("@max_position_seconds", update.MaxPositionSeconds),
                ("@heard_seconds_delta", update.HeardSecondsDelta),
                ("@last_observed_utc", ToDbDate(update.LastObservedUtc)),
                ("@last_observed_audio_badge_raw", update.LastObservedAudioBadgeRaw),
                ("@last_observed_audio_variant", (int?)update.LastObservedAudioVariant),
                ("@state", (int)update.State),
                ("@pause_count", update.PauseCount),
                ("@resume_count", update.ResumeCount)
            ],
            cancellationToken);
    }

    public Task AppendEventAsync(SessionEventRecord sessionEvent, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO session_events (session_id, event_type, observed_at_utc, position_seconds, payload_json)
            VALUES (@session_id, @event_type, @observed_at_utc, @position_seconds, @payload_json);
            """;

        return ExecuteNonQueryAsync(
            sql,
            [
                ("@session_id", sessionEvent.SessionId),
                ("@event_type", (int)sessionEvent.EventType),
                ("@observed_at_utc", ToDbDate(sessionEvent.ObservedAtUtc)),
                ("@position_seconds", sessionEvent.PositionSeconds),
                ("@payload_json", sessionEvent.PayloadJson)
            ],
            cancellationToken);
    }

    public Task CloseSessionAsync(SessionClosure closure, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE listening_sessions
            SET ended_at_utc = @ended_at_utc,
                last_position_seconds = @last_position_seconds,
                heard_seconds = @heard_seconds,
                last_observed_utc = @last_observed_utc,
                state = @closed_state,
                end_reason = @end_reason
            WHERE session_id = @session_id;
            """;

        return ExecuteNonQueryAsync(
            sql,
            [
                ("@session_id", closure.SessionId),
                ("@ended_at_utc", ToDbDate(closure.EndedAtUtc)),
                ("@last_position_seconds", closure.LastPositionSeconds),
                ("@heard_seconds", closure.HeardSeconds),
                ("@last_observed_utc", ToDbDate(closure.LastObservedUtc)),
                ("@closed_state", (int)SessionState.Closed),
                ("@end_reason", (int)closure.Reason)
            ],
            cancellationToken);
    }

    public async Task<TrackerStatistics> GetStatisticsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                (SELECT COUNT(*) FROM tracks) AS track_count,
                (SELECT COUNT(*) FROM listening_sessions) AS session_count,
                (SELECT COUNT(*) FROM listening_sessions WHERE state <> @closed_state) AS open_session_count,
                (SELECT MAX(last_observed_utc) FROM listening_sessions) AS last_observed_utc;
            """;

        return await ExecuteReaderAsync(
            sql,
            [("@closed_state", (int)SessionState.Closed)],
            async reader =>
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return new TrackerStatistics(0, 0, 0, null);
                }

                return new TrackerStatistics(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture));
            },
            cancellationToken).ConfigureAwait(false);
    }

    public Task<TrackDetailsRecord?> GetTrackDetailsAsync(long trackId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                t.track_id, t.fingerprint, t.title, t.artist, t.album, t.subtitle,
                t.normalized_title, t.normalized_artist, t.normalized_album,
                t.duration_seconds, t.song_url, t.artist_url, t.artwork_url, t.catalog_audio_variants_json,
                t.last_observed_audio_badge_raw, t.last_observed_audio_variant,
                t.first_seen_utc, t.last_seen_utc, t.enriched_at_utc,
                tm.track_id, tm.apple_music_song_url, tm.apple_music_album_url, tm.apple_music_artist_url,
                tm.catalog_song_id, tm.catalog_album_id, tm.catalog_artist_id,
                tm.itunes_track_id, tm.itunes_collection_id, tm.itunes_artist_id,
                tm.duration_seconds, tm.release_date_utc, tm.composer_name, tm.genre_names_json,
                tm.track_number, tm.track_count, tm.disc_number, tm.disc_count, tm.isrc, tm.preview_url,
                tm.content_rating, tm.catalog_audio_variants_json, tm.artwork_url, tm.artwork_width, tm.artwork_height,
                tm.artwork_cache_relative_path, tm.storefront, tm.metadata_sources_json,
                tm.web_payload_json, tm.itunes_payload_json, tm.catalog_payload_json,
                tm.enriched_at_utc, tm.artwork_cached_at_utc
            FROM tracks t
            LEFT JOIN track_metadata tm ON tm.track_id = t.track_id
            WHERE t.track_id = @track_id;
            """;

        return ExecuteReaderAsync(
            sql,
            [("@track_id", trackId)],
            async reader =>
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }

                return new TrackDetailsRecord(
                    MapTrack(reader, 0),
                    reader.IsDBNull(19) ? null : MapTrackMetadata(reader, 19));
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<TrackHistoryRow>> GetTrackHistoryAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                t.track_id,
                t.title,
                t.artist,
                t.album,
                t.subtitle,
                t.catalog_audio_variants_json,
                t.last_observed_audio_badge_raw,
                t.last_observed_audio_variant,
                COALESCE(tm.apple_music_song_url, t.song_url),
                tm.apple_music_album_url,
                COALESCE(tm.apple_music_artist_url, t.artist_url),
                COALESCE(tm.artwork_url, t.artwork_url),
                tm.artwork_cache_relative_path,
                t.last_seen_utc
            FROM tracks t
            LEFT JOIN track_metadata tm ON tm.track_id = t.track_id
            ORDER BY t.last_seen_utc DESC, t.track_id DESC;
            """;

        return await ExecuteReaderAsync(
            sql,
            [],
            async reader =>
            {
                var rows = new List<TrackHistoryRow>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    rows.Add(new TrackHistoryRow(
                        reader.GetInt64(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetString(5),
                        reader.IsDBNull(6) ? null : reader.GetString(6),
                        reader.IsDBNull(7) ? null : (PlaybackAudioVariant)reader.GetInt32(7),
                        reader.IsDBNull(8) ? null : reader.GetString(8),
                        reader.IsDBNull(9) ? null : reader.GetString(9),
                        reader.IsDBNull(10) ? null : reader.GetString(10),
                        reader.IsDBNull(11) ? null : reader.GetString(11),
                        reader.IsDBNull(12) ? null : reader.GetString(12),
                        ParseDate(reader, 13)!.Value));
                }

                return (IReadOnlyList<TrackHistoryRow>)rows;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ExportSessionRow>> ExportSessionsAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                s.session_id, t.fingerprint, t.title, t.artist, t.album, t.subtitle,
                s.started_at_utc, s.ended_at_utc, s.first_position_seconds, s.last_position_seconds,
                s.max_position_seconds, s.heard_seconds, s.pause_count, s.resume_count, s.replay_index,
                s.state, s.end_reason, s.last_observed_utc,
                COALESCE(tm.apple_music_song_url, t.song_url),
                COALESCE(tm.apple_music_artist_url, t.artist_url),
                COALESCE(tm.artwork_url, t.artwork_url),
                COALESCE(tm.catalog_audio_variants_json, t.catalog_audio_variants_json),
                s.last_observed_audio_badge_raw, s.last_observed_audio_variant
            FROM listening_sessions s
            INNER JOIN tracks t ON t.track_id = s.track_id
            LEFT JOIN track_metadata tm ON tm.track_id = t.track_id
            WHERE (@from_utc IS NULL OR s.started_at_utc >= @from_utc)
              AND (@to_utc IS NULL OR s.started_at_utc <= @to_utc)
            ORDER BY s.started_at_utc;
            """;

        return await ExecuteReaderAsync(
            sql,
            [
                ("@from_utc", fromUtc is null ? null : ToDbDate(fromUtc.Value)),
                ("@to_utc", toUtc is null ? null : ToDbDate(toUtc.Value))
            ],
            async reader =>
            {
                var rows = new List<ExportSessionRow>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    rows.Add(new ExportSessionRow(
                        reader.GetInt64(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.GetString(5),
                        ParseDate(reader, 6)!.Value,
                        ParseDate(reader, 7),
                        reader.GetInt32(8),
                        reader.GetInt32(9),
                        reader.GetInt32(10),
                        reader.GetDouble(11),
                        reader.GetInt32(12),
                        reader.GetInt32(13),
                        reader.GetInt32(14),
                        (SessionState)reader.GetInt32(15),
                        reader.IsDBNull(16) ? null : (SessionEndReason)reader.GetInt32(16),
                        ParseDate(reader, 17)!.Value,
                        reader.IsDBNull(18) ? null : reader.GetString(18),
                        reader.IsDBNull(19) ? null : reader.GetString(19),
                        reader.IsDBNull(20) ? null : reader.GetString(20),
                        reader.IsDBNull(21) ? null : reader.GetString(21),
                        reader.IsDBNull(22) ? null : reader.GetString(22),
                        reader.IsDBNull(23) ? null : (PlaybackAudioVariant)reader.GetInt32(23)));
                }

                return (IReadOnlyList<ExportSessionRow>)rows;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ExportTrackRow>> ExportTracksAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                t.track_id, t.fingerprint, t.title, t.artist, t.album, t.subtitle,
                t.normalized_title, t.normalized_artist, t.normalized_album,
                t.duration_seconds,
                tm.apple_music_song_url, tm.apple_music_album_url, tm.apple_music_artist_url,
                tm.catalog_song_id, tm.catalog_album_id, tm.catalog_artist_id,
                tm.itunes_track_id, tm.itunes_collection_id, tm.itunes_artist_id,
                tm.duration_seconds, tm.release_date_utc, tm.composer_name, tm.genre_names_json,
                tm.track_number, tm.track_count, tm.disc_number, tm.disc_count, tm.isrc, tm.preview_url,
                tm.content_rating, tm.catalog_audio_variants_json, tm.artwork_url, tm.artwork_width, tm.artwork_height,
                tm.artwork_cache_relative_path, tm.storefront, tm.metadata_sources_json,
                tm.web_payload_json, tm.itunes_payload_json, tm.catalog_payload_json,
                t.last_observed_audio_badge_raw, t.last_observed_audio_variant,
                t.first_seen_utc, t.last_seen_utc, t.enriched_at_utc, tm.enriched_at_utc, tm.artwork_cached_at_utc
            FROM tracks t
            LEFT JOIN track_metadata tm ON tm.track_id = t.track_id
            ORDER BY t.artist, t.album, t.title;
            """;

        return await ExecuteReaderAsync(
            sql,
            [],
            async reader =>
            {
                var rows = new List<ExportTrackRow>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    rows.Add(new ExportTrackRow(
                        reader.GetInt64(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.GetString(5),
                        reader.GetString(6),
                        reader.GetString(7),
                        reader.GetString(8),
                        reader.IsDBNull(9) ? null : reader.GetInt32(9),
                        reader.IsDBNull(10) ? null : reader.GetString(10),
                        reader.IsDBNull(11) ? null : reader.GetString(11),
                        reader.IsDBNull(12) ? null : reader.GetString(12),
                        reader.IsDBNull(13) ? null : reader.GetString(13),
                        reader.IsDBNull(14) ? null : reader.GetString(14),
                        reader.IsDBNull(15) ? null : reader.GetString(15),
                        reader.IsDBNull(16) ? null : reader.GetInt64(16),
                        reader.IsDBNull(17) ? null : reader.GetInt64(17),
                        reader.IsDBNull(18) ? null : reader.GetInt64(18),
                        reader.IsDBNull(19) ? null : reader.GetInt32(19),
                        ParseDate(reader, 20),
                        reader.IsDBNull(21) ? null : reader.GetString(21),
                        reader.IsDBNull(22) ? null : reader.GetString(22),
                        reader.IsDBNull(23) ? null : reader.GetInt32(23),
                        reader.IsDBNull(24) ? null : reader.GetInt32(24),
                        reader.IsDBNull(25) ? null : reader.GetInt32(25),
                        reader.IsDBNull(26) ? null : reader.GetInt32(26),
                        reader.IsDBNull(27) ? null : reader.GetString(27),
                        reader.IsDBNull(28) ? null : reader.GetString(28),
                        reader.IsDBNull(29) ? null : reader.GetString(29),
                        reader.IsDBNull(30) ? null : reader.GetString(30),
                        reader.IsDBNull(31) ? null : reader.GetString(31),
                        reader.IsDBNull(32) ? null : reader.GetInt32(32),
                        reader.IsDBNull(33) ? null : reader.GetInt32(33),
                        reader.IsDBNull(34) ? null : reader.GetString(34),
                        reader.IsDBNull(35) ? null : reader.GetString(35),
                        reader.IsDBNull(36) ? null : reader.GetString(36),
                        reader.IsDBNull(37) ? null : reader.GetString(37),
                        reader.IsDBNull(38) ? null : reader.GetString(38),
                        reader.IsDBNull(39) ? null : reader.GetString(39),
                        reader.IsDBNull(40) ? null : reader.GetString(40),
                        reader.IsDBNull(41) ? null : (PlaybackAudioVariant)reader.GetInt32(41),
                        ParseDate(reader, 42)!.Value,
                        ParseDate(reader, 43)!.Value,
                        ParseDate(reader, 44),
                        ParseDate(reader, 45),
                        ParseDate(reader, 46)));
                }

                return (IReadOnlyList<ExportTrackRow>)rows;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SessionEventRecord>> GetSessionEventsAsync(long sessionId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT session_id, event_type, observed_at_utc, position_seconds, payload_json
            FROM session_events
            WHERE session_id = @session_id
            ORDER BY observed_at_utc, session_event_id;
            """;

        return await ExecuteReaderAsync(
            sql,
            [("@session_id", sessionId)],
            async reader =>
            {
                var rows = new List<SessionEventRecord>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    rows.Add(new SessionEventRecord(
                        reader.GetInt64(0),
                        (SessionEventType)reader.GetInt32(1),
                        ParseDate(reader, 2)!.Value,
                        reader.GetInt32(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4)));
                }

                return (IReadOnlyList<SessionEventRecord>)rows;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private Task<TrackRecord> GetTrackByFingerprintAsync(string fingerprint, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                track_id, fingerprint, title, artist, album, subtitle,
                normalized_title, normalized_artist, normalized_album,
                duration_seconds, song_url, artist_url, artwork_url, catalog_audio_variants_json,
                last_observed_audio_badge_raw, last_observed_audio_variant,
                first_seen_utc, last_seen_utc, enriched_at_utc
            FROM tracks WHERE fingerprint = @fingerprint;
            """;

        return ExecuteReaderAsync(
            sql,
            [("@fingerprint", fingerprint)],
            async reader =>
            {
                await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                return MapTrack(reader, 0);
            },
            cancellationToken);
    }

    private Task<ListeningSessionRecord> GetSessionAsync(string sql, IEnumerable<(string Name, object? Value)> parameters, CancellationToken cancellationToken)
        => ExecuteReaderAsync(
            sql,
            parameters,
            async reader =>
            {
                await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                return MapSession(reader);
            },
            cancellationToken);

    private async Task<long> ExecuteScalarInt64Async(string sql, IEnumerable<(string Name, object? Value)> parameters, CancellationToken cancellationToken)
        => await ExecuteReaderAsync(
            sql,
            parameters,
            async reader =>
            {
                await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                return reader.GetInt64(0);
            },
            cancellationToken).ConfigureAwait(false);

    private async Task<T> ExecuteReaderAsync<T>(
        string sql,
        IEnumerable<(string Name, object? Value)> parameters,
        Func<SqliteDataReader, Task<T>> read,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await read(reader).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ExecuteNonQueryAsync(string sql, IEnumerable<(string Name, object? Value)> parameters, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task<int> GetUserVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task<string> ReadEmbeddedTextAsync(string resourceName, CancellationToken cancellationToken)
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static TrackRecord MapTrack(SqliteDataReader reader, int offset)
    {
        return new TrackRecord(
            reader.GetInt64(offset + 0),
            reader.GetString(offset + 1),
            reader.GetString(offset + 2),
            reader.GetString(offset + 3),
            reader.GetString(offset + 4),
            reader.GetString(offset + 5),
            reader.GetString(offset + 6),
            reader.GetString(offset + 7),
            reader.GetString(offset + 8),
            reader.IsDBNull(offset + 9) ? null : reader.GetInt32(offset + 9),
            reader.IsDBNull(offset + 10) ? null : reader.GetString(offset + 10),
            reader.IsDBNull(offset + 11) ? null : reader.GetString(offset + 11),
            reader.IsDBNull(offset + 12) ? null : reader.GetString(offset + 12),
            reader.IsDBNull(offset + 13) ? null : reader.GetString(offset + 13),
            reader.IsDBNull(offset + 14) ? null : reader.GetString(offset + 14),
            reader.IsDBNull(offset + 15) ? null : (PlaybackAudioVariant)reader.GetInt32(offset + 15),
            ParseDate(reader, offset + 16)!.Value,
            ParseDate(reader, offset + 17)!.Value,
            ParseDate(reader, offset + 18));
    }

    private static TrackMetadataRecord MapTrackMetadata(SqliteDataReader reader, int offset)
    {
        return new TrackMetadataRecord(
            reader.GetInt64(offset + 0),
            reader.IsDBNull(offset + 1) ? null : reader.GetString(offset + 1),
            reader.IsDBNull(offset + 2) ? null : reader.GetString(offset + 2),
            reader.IsDBNull(offset + 3) ? null : reader.GetString(offset + 3),
            reader.IsDBNull(offset + 4) ? null : reader.GetString(offset + 4),
            reader.IsDBNull(offset + 5) ? null : reader.GetString(offset + 5),
            reader.IsDBNull(offset + 6) ? null : reader.GetString(offset + 6),
            reader.IsDBNull(offset + 7) ? null : reader.GetInt64(offset + 7),
            reader.IsDBNull(offset + 8) ? null : reader.GetInt64(offset + 8),
            reader.IsDBNull(offset + 9) ? null : reader.GetInt64(offset + 9),
            reader.IsDBNull(offset + 10) ? null : reader.GetInt32(offset + 10),
            ParseDate(reader, offset + 11),
            reader.IsDBNull(offset + 12) ? null : reader.GetString(offset + 12),
            reader.IsDBNull(offset + 13) ? null : reader.GetString(offset + 13),
            reader.IsDBNull(offset + 14) ? null : reader.GetInt32(offset + 14),
            reader.IsDBNull(offset + 15) ? null : reader.GetInt32(offset + 15),
            reader.IsDBNull(offset + 16) ? null : reader.GetInt32(offset + 16),
            reader.IsDBNull(offset + 17) ? null : reader.GetInt32(offset + 17),
            reader.IsDBNull(offset + 18) ? null : reader.GetString(offset + 18),
            reader.IsDBNull(offset + 19) ? null : reader.GetString(offset + 19),
            reader.IsDBNull(offset + 20) ? null : reader.GetString(offset + 20),
            reader.IsDBNull(offset + 21) ? null : reader.GetString(offset + 21),
            reader.IsDBNull(offset + 22) ? null : reader.GetString(offset + 22),
            reader.IsDBNull(offset + 23) ? null : reader.GetInt32(offset + 23),
            reader.IsDBNull(offset + 24) ? null : reader.GetInt32(offset + 24),
            reader.IsDBNull(offset + 25) ? null : reader.GetString(offset + 25),
            reader.IsDBNull(offset + 26) ? null : reader.GetString(offset + 26),
            reader.IsDBNull(offset + 27) ? null : reader.GetString(offset + 27),
            reader.IsDBNull(offset + 28) ? null : reader.GetString(offset + 28),
            reader.IsDBNull(offset + 29) ? null : reader.GetString(offset + 29),
            reader.IsDBNull(offset + 30) ? null : reader.GetString(offset + 30),
            ParseDate(reader, offset + 31)!.Value,
            ParseDate(reader, offset + 32));
    }

    private static ListeningSessionRecord MapSession(SqliteDataReader reader)
    {
        return new ListeningSessionRecord(
            reader.GetInt64(reader.GetOrdinal("session_id")),
            reader.GetInt64(reader.GetOrdinal("track_id")),
            reader.GetInt64(reader.GetOrdinal("app_run_id")),
            ParseDate(reader, reader.GetOrdinal("started_at_utc"))!.Value,
            ParseDate(reader, reader.GetOrdinal("ended_at_utc")),
            reader.GetInt32(reader.GetOrdinal("first_position_seconds")),
            reader.GetInt32(reader.GetOrdinal("last_position_seconds")),
            reader.GetInt32(reader.GetOrdinal("max_position_seconds")),
            reader.GetDouble(reader.GetOrdinal("heard_seconds")),
            reader.GetInt32(reader.GetOrdinal("pause_count")),
            reader.GetInt32(reader.GetOrdinal("resume_count")),
            reader.GetInt32(reader.GetOrdinal("replay_index")),
            (SessionState)reader.GetInt32(reader.GetOrdinal("state")),
            reader.IsDBNull(reader.GetOrdinal("end_reason")) ? null : (SessionEndReason)reader.GetInt32(reader.GetOrdinal("end_reason")),
            ParseDate(reader, reader.GetOrdinal("last_observed_utc"))!.Value,
            reader.IsDBNull(reader.GetOrdinal("last_observed_audio_badge_raw")) ? null : reader.GetString(reader.GetOrdinal("last_observed_audio_badge_raw")),
            reader.IsDBNull(reader.GetOrdinal("last_observed_audio_variant")) ? null : (PlaybackAudioVariant)reader.GetInt32(reader.GetOrdinal("last_observed_audio_variant")));
    }

    private static string ToDbDate(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);
    }
}
