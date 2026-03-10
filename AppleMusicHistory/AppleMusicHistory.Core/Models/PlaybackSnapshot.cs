namespace AppleMusicHistory.Core.Models;

public sealed record PlaybackSnapshot
{
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public required string Album { get; init; }
    public required string Subtitle { get; init; }
    public required TrackFingerprint Fingerprint { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public bool IsPaused { get; init; }
    public int? CurrentPositionSeconds { get; init; }
    public int? DurationSeconds { get; init; }
    public string? ObservedAudioBadgeRaw { get; init; }
    public PlaybackAudioVariant? ObservedAudioVariant { get; init; }
    public string? SourceDescription { get; init; }

    public static PlaybackSnapshot Create(
        string title,
        string artist,
        string album,
        string subtitle,
        DateTimeOffset observedAtUtc,
        bool isPaused,
        int? currentPositionSeconds,
        int? durationSeconds,
        string? observedAudioBadgeRaw = null,
        string? sourceDescription = null)
    {
        var normalizedAudioBadge = PlaybackAudioVariantParser.NormalizeBadge(observedAudioBadgeRaw);

        return new PlaybackSnapshot
        {
            Title = TrackFingerprint.Sanitize(title),
            Artist = TrackFingerprint.Sanitize(artist),
            Album = TrackFingerprint.Sanitize(album),
            Subtitle = TrackFingerprint.Sanitize(subtitle),
            Fingerprint = TrackFingerprint.From(title, artist, album),
            ObservedAtUtc = observedAtUtc,
            IsPaused = isPaused,
            CurrentPositionSeconds = currentPositionSeconds,
            DurationSeconds = durationSeconds,
            ObservedAudioBadgeRaw = normalizedAudioBadge,
            ObservedAudioVariant = PlaybackAudioVariantParser.ParseBadge(normalizedAudioBadge),
            SourceDescription = sourceDescription
        };
    }
}
