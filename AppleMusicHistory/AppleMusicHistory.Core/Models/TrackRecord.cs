namespace AppleMusicHistory.Core.Models;

public sealed record TrackRecord(
    long TrackId,
    string Fingerprint,
    string Title,
    string Artist,
    string Album,
    string Subtitle,
    string NormalizedTitle,
    string NormalizedArtist,
    string NormalizedAlbum,
    int? DurationSeconds,
    string? SongUrl,
    string? ArtistUrl,
    string? ArtworkUrl,
    string? CatalogAudioVariantsJson,
    string? LastObservedAudioBadgeRaw,
    PlaybackAudioVariant? LastObservedAudioVariant,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    DateTimeOffset? EnrichedAtUtc);
