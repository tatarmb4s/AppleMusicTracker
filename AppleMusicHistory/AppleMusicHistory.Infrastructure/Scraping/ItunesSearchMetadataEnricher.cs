using System.Text.Json;
using AppleMusicHistory.Core.Abstractions;
using AppleMusicHistory.Core.Models;
using AppleMusicHistory.Infrastructure.Data;

namespace AppleMusicHistory.Infrastructure.Scraping;

public sealed class ItunesSearchMetadataEnricher : ITrackMetadataEnricher
{
    private static readonly HttpClient HttpClient = new();
    private readonly string _storefront;
    private readonly FileLogger? _logger;

    public ItunesSearchMetadataEnricher(string storefront = "us", FileLogger? logger = null)
    {
        _storefront = string.IsNullOrWhiteSpace(storefront) ? "us" : storefront.ToLowerInvariant();
        _logger = logger;
    }

    public async Task<TrackEnrichmentResult?> EnrichAsync(TrackFingerprint fingerprint, CancellationToken cancellationToken)
    {
        try
        {
            var bestMatch = await SearchBestSongAsync(fingerprint, cancellationToken).ConfigureAwait(false);
            if (bestMatch is null)
            {
                return null;
            }

            var result = bestMatch.Value;
            var artworkUrl = result.TryGetProperty("artworkUrl100", out var artworkElement)
                ? UpscaleArtworkUrl(artworkElement.GetString())
                : null;
            return new TrackEnrichmentResult(
                null,
                null,
                null,
                null,
                null,
                null,
                TryGetInt64(result, "trackId"),
                TryGetInt64(result, "collectionId"),
                TryGetInt64(result, "artistId"),
                TryGetInt32(result, "trackTimeMillis") is { } durationMs ? durationMs / 1000 : null,
                TryGetDate(result, "releaseDate"),
                TryGetString(result, "composer"),
                TryGetString(result, "primaryGenreName") is { } genre ? [genre] : null,
                TryGetInt32(result, "trackNumber"),
                TryGetInt32(result, "trackCount"),
                TryGetInt32(result, "discNumber"),
                TryGetInt32(result, "discCount"),
                TryGetString(result, "isrc"),
                TryGetString(result, "previewUrl"),
                TryGetString(result, "trackExplicitness"),
                null,
                artworkUrl,
                artworkUrl is null ? null : 1000,
                artworkUrl is null ? null : 1000,
                _storefront,
                ["itunes"],
                null,
                result.GetRawText(),
                null,
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync($"iTunes metadata enrichment failed: {ex.Message}", cancellationToken).ConfigureAwait(false);
            }

            return null;
        }
    }

    private async Task<JsonElement?> SearchBestSongAsync(TrackFingerprint fingerprint, CancellationToken cancellationToken)
    {
        var query = $"{fingerprint.NormalizedTitle} {fingerprint.NormalizedArtist} {fingerprint.NormalizedAlbum}".Trim();
        var url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(query)}&entity=song&limit=10&country={_storefront}";
        await using var stream = await HttpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        JsonElement? best = null;
        var bestScore = int.MinValue;
        foreach (var item in results.EnumerateArray())
        {
            var title = TrackFingerprint.Normalize(TryGetString(item, "trackName"));
            var artist = TrackFingerprint.Normalize(TryGetString(item, "artistName"));
            if (title != fingerprint.NormalizedTitle || artist != fingerprint.NormalizedArtist)
            {
                continue;
            }

            var score = 100;
            var album = TrackFingerprint.Normalize(TryGetString(item, "collectionName"));
            if (!string.IsNullOrWhiteSpace(fingerprint.NormalizedAlbum) && album == fingerprint.NormalizedAlbum)
            {
                score += 25;
            }

            if (score > bestScore)
            {
                best = item.Clone();
                bestScore = score;
            }
        }

        return best;
    }

    private static string? UpscaleArtworkUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return url.Replace("100x100bb", "1000x1000bb", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? TryGetInt32(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : null;

    private static long? TryGetInt64(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt64(out var parsed) ? parsed : null;

    private static DateTimeOffset? TryGetDate(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;
}
