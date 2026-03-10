namespace AppleMusicHistory.Core.Models;

public sealed record TrackUpsert(
    TrackFingerprint Fingerprint,
    string Title,
    string Artist,
    string Album,
    string Subtitle,
    DateTimeOffset ObservedAtUtc,
    int? DurationSeconds = null,
    string? SongUrl = null,
    string? ArtistUrl = null,
    string? ArtworkUrl = null,
    DateTimeOffset? EnrichedAtUtc = null);
