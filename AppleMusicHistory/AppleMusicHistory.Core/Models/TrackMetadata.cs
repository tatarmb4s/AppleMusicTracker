namespace AppleMusicHistory.Core.Models;

public sealed record TrackMetadata(
    int? DurationSeconds,
    string? SongUrl,
    string? ArtistUrl,
    string? ArtworkUrl,
    string? CatalogAudioVariantsJson,
    DateTimeOffset EnrichedAtUtc);
