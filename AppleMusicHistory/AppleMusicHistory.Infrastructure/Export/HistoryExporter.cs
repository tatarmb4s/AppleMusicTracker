using System.Text;
using System.Text.Json;
using AppleMusicHistory.Core.Abstractions;

namespace AppleMusicHistory.Infrastructure.Export;

public sealed class HistoryExporter : IHistoryExporter
{
    private readonly IHistoryRepository _repository;

    public HistoryExporter(IHistoryRepository repository)
    {
        _repository = repository;
    }

    public async Task ExportCsvAsync(string filePath, CancellationToken cancellationToken)
    {
        var rows = await _repository.ExportSessionsAsync(null, null, cancellationToken).ConfigureAwait(false);
        var builder = new StringBuilder();
        builder.AppendLine("SessionId,Fingerprint,Title,Artist,Album,Subtitle,StartedAtUtc,EndedAtUtc,FirstPositionSeconds,LastPositionSeconds,MaxPositionSeconds,HeardSeconds,PauseCount,ResumeCount,ReplayIndex,State,EndReason,LastObservedUtc,SongUrl,ArtistUrl,ArtworkUrl,CatalogAudioVariantsJson,LastObservedAudioBadgeRaw,LastObservedAudioVariant");

        foreach (var row in rows)
        {
            var cells = new[]
            {
                row.SessionId.ToString(),
                row.Fingerprint,
                row.Title,
                row.Artist,
                row.Album,
                row.Subtitle,
                row.StartedAtUtc.ToString("O"),
                row.EndedAtUtc?.ToString("O") ?? string.Empty,
                row.FirstPositionSeconds.ToString(),
                row.LastPositionSeconds.ToString(),
                row.MaxPositionSeconds.ToString(),
                row.HeardSeconds.ToString("F2"),
                row.PauseCount.ToString(),
                row.ResumeCount.ToString(),
                row.ReplayIndex.ToString(),
                row.State.ToString(),
                row.EndReason?.ToString() ?? string.Empty,
                row.LastObservedUtc.ToString("O"),
                row.SongUrl ?? string.Empty,
                row.ArtistUrl ?? string.Empty,
                row.ArtworkUrl ?? string.Empty,
                row.CatalogAudioVariantsJson ?? string.Empty,
                row.LastObservedAudioBadgeRaw ?? string.Empty,
                row.LastObservedAudioVariant?.ToString() ?? string.Empty
            };

            builder.AppendLine(string.Join(",", cells.Select(EscapeCsv)));
        }

        await File.WriteAllTextAsync(filePath, builder.ToString(), cancellationToken).ConfigureAwait(false);
    }

    public async Task ExportJsonAsync(string filePath, CancellationToken cancellationToken)
    {
        var sessions = await _repository.ExportSessionsAsync(null, null, cancellationToken).ConfigureAwait(false);
        var payload = new List<object>(sessions.Count);
        foreach (var session in sessions)
        {
            var events = await _repository.GetSessionEventsAsync(session.SessionId, cancellationToken).ConfigureAwait(false);
            payload.Add(new
            {
                session,
                events
            });
        }

        await File.WriteAllTextAsync(
            filePath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ExportTracksCsvAsync(string filePath, CancellationToken cancellationToken)
    {
        var rows = await _repository.ExportTracksAsync(cancellationToken).ConfigureAwait(false);
        var builder = new StringBuilder();
        builder.AppendLine("TrackId,Fingerprint,Title,Artist,Album,Subtitle,NormalizedTitle,NormalizedArtist,NormalizedAlbum,CoreDurationSeconds,AppleMusicSongUrl,AppleMusicAlbumUrl,AppleMusicArtistUrl,CatalogSongId,CatalogAlbumId,CatalogArtistId,ItunesTrackId,ItunesCollectionId,ItunesArtistId,MetadataDurationSeconds,ReleaseDateUtc,ComposerName,GenreNamesJson,TrackNumber,TrackCount,DiscNumber,DiscCount,Isrc,PreviewUrl,ContentRating,CatalogAudioVariantsJson,ArtworkUrl,ArtworkWidth,ArtworkHeight,ArtworkCacheRelativePath,Storefront,MetadataSourcesJson,WebPayloadJson,ItunesPayloadJson,CatalogPayloadJson,LastObservedAudioBadgeRaw,LastObservedAudioVariant,FirstSeenUtc,LastSeenUtc,CoreEnrichedAtUtc,MetadataEnrichedAtUtc,ArtworkCachedAtUtc");

        foreach (var row in rows)
        {
            var cells = new[]
            {
                row.TrackId.ToString(),
                row.Fingerprint,
                row.Title,
                row.Artist,
                row.Album,
                row.Subtitle,
                row.NormalizedTitle,
                row.NormalizedArtist,
                row.NormalizedAlbum,
                row.CoreDurationSeconds?.ToString() ?? string.Empty,
                row.AppleMusicSongUrl ?? string.Empty,
                row.AppleMusicAlbumUrl ?? string.Empty,
                row.AppleMusicArtistUrl ?? string.Empty,
                row.CatalogSongId ?? string.Empty,
                row.CatalogAlbumId ?? string.Empty,
                row.CatalogArtistId ?? string.Empty,
                row.ItunesTrackId?.ToString() ?? string.Empty,
                row.ItunesCollectionId?.ToString() ?? string.Empty,
                row.ItunesArtistId?.ToString() ?? string.Empty,
                row.MetadataDurationSeconds?.ToString() ?? string.Empty,
                row.ReleaseDateUtc?.ToString("O") ?? string.Empty,
                row.ComposerName ?? string.Empty,
                row.GenreNamesJson ?? string.Empty,
                row.TrackNumber?.ToString() ?? string.Empty,
                row.TrackCount?.ToString() ?? string.Empty,
                row.DiscNumber?.ToString() ?? string.Empty,
                row.DiscCount?.ToString() ?? string.Empty,
                row.Isrc ?? string.Empty,
                row.PreviewUrl ?? string.Empty,
                row.ContentRating ?? string.Empty,
                row.CatalogAudioVariantsJson ?? string.Empty,
                row.ArtworkUrl ?? string.Empty,
                row.ArtworkWidth?.ToString() ?? string.Empty,
                row.ArtworkHeight?.ToString() ?? string.Empty,
                row.ArtworkCacheRelativePath ?? string.Empty,
                row.Storefront ?? string.Empty,
                row.MetadataSourcesJson ?? string.Empty,
                row.WebPayloadJson ?? string.Empty,
                row.ItunesPayloadJson ?? string.Empty,
                row.CatalogPayloadJson ?? string.Empty,
                row.LastObservedAudioBadgeRaw ?? string.Empty,
                row.LastObservedAudioVariant?.ToString() ?? string.Empty,
                row.FirstSeenUtc.ToString("O"),
                row.LastSeenUtc.ToString("O"),
                row.CoreEnrichedAtUtc?.ToString("O") ?? string.Empty,
                row.MetadataEnrichedAtUtc?.ToString("O") ?? string.Empty,
                row.ArtworkCachedAtUtc?.ToString("O") ?? string.Empty
            };

            builder.AppendLine(string.Join(",", cells.Select(EscapeCsv)));
        }

        await File.WriteAllTextAsync(filePath, builder.ToString(), cancellationToken).ConfigureAwait(false);
    }

    public async Task ExportTracksJsonAsync(string filePath, CancellationToken cancellationToken)
    {
        var tracks = await _repository.ExportTracksAsync(cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            filePath,
            JsonSerializer.Serialize(tracks, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken).ConfigureAwait(false);
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
