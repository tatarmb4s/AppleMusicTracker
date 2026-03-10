using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AppleMusicHistory.Core.Models;

public sealed record TrackFingerprint(string NormalizedTitle, string NormalizedArtist, string NormalizedAlbum, string Value)
{
    private static readonly Regex InvisibleCharactersRegex = new(@"[\u200B-\u200F\u202A-\u202E]", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static TrackFingerprint From(string title, string artist, string album)
    {
        var normalizedTitle = Normalize(title);
        var normalizedArtist = Normalize(artist);
        var normalizedAlbum = Normalize(album);
        var composite = $"{normalizedTitle}\n{normalizedArtist}\n{normalizedAlbum}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(composite)));
        return new TrackFingerprint(normalizedTitle, normalizedArtist, normalizedAlbum, hash);
    }

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var stripped = InvisibleCharactersRegex.Replace(value, string.Empty).Trim();
        return WhitespaceRegex.Replace(stripped, " ");
    }

    public static string Normalize(string? value) => Sanitize(value).ToLowerInvariant();
}
