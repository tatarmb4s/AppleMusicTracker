namespace AppleMusicHistory.Core.Models;

public sealed record TrackHistoryRow(
    long TrackId,
    string Title,
    string Artist,
    string Album,
    string Subtitle,
    string? CatalogAudioVariantsJson,
    string? LastObservedAudioBadgeRaw,
    PlaybackAudioVariant? LastObservedAudioVariant,
    string? SongUrl,
    string? AlbumUrl,
    string? ArtistUrl,
    string? ArtworkUrl,
    string? ArtworkCacheRelativePath,
    DateTimeOffset LastSeenUtc);
