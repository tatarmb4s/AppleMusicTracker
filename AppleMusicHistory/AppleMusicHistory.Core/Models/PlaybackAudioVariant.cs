namespace AppleMusicHistory.Core.Models;

public enum PlaybackAudioVariant
{
    Unknown = 0,
    Lossless = 1,
    HiResLossless = 2,
    DolbyAudio = 3,
    DolbyAtmos = 4,
    Other = 5
}

public static class PlaybackAudioVariantParser
{
    public static PlaybackAudioVariant? ParseBadge(string? badge)
    {
        var normalizedBadge = NormalizeBadge(badge);
        if (string.IsNullOrWhiteSpace(normalizedBadge))
        {
            return null;
        }

        return normalizedBadge.ToLowerInvariant() switch
        {
            "lossless" => PlaybackAudioVariant.Lossless,
            "hi-res lossless" => PlaybackAudioVariant.HiResLossless,
            "high-res lossless" => PlaybackAudioVariant.HiResLossless,
            "dolby audio" => PlaybackAudioVariant.DolbyAudio,
            "dolby atmos" => PlaybackAudioVariant.DolbyAtmos,
            "unknown" => PlaybackAudioVariant.Unknown,
            _ => PlaybackAudioVariant.Other
        };
    }

    public static string? NormalizeBadge(string? badge)
    {
        var normalizedBadge = TrackFingerprint.Sanitize(badge);
        return string.IsNullOrWhiteSpace(normalizedBadge) ? null : normalizedBadge;
    }

    public static string ToDisplayName(PlaybackAudioVariant? variant, string? rawBadge = null)
    {
        var normalizedBadge = NormalizeBadge(rawBadge);
        if (!string.IsNullOrWhiteSpace(normalizedBadge))
        {
            return normalizedBadge;
        }

        return variant switch
        {
            PlaybackAudioVariant.Lossless => "Lossless",
            PlaybackAudioVariant.HiResLossless => "Hi-Res Lossless",
            PlaybackAudioVariant.DolbyAudio => "Dolby Audio",
            PlaybackAudioVariant.DolbyAtmos => "Dolby Atmos",
            PlaybackAudioVariant.Unknown => "Unknown",
            PlaybackAudioVariant.Other => "Other",
            _ => "Standard / unknown"
        };
    }
}
