using System.Net.Http.Headers;
using System.Text.Json;
using AppleMusicHistory.Core.Abstractions;
using AppleMusicHistory.Core.Models;
using AppleMusicHistory.Infrastructure.Data;

namespace AppleMusicHistory.Infrastructure.Scraping;

public sealed class AppleMusicCatalogMetadataEnricher : ITrackMetadataEnricher
{
    private static readonly HttpClient HttpClient = new();
    private readonly string _developerToken;
    private readonly string _storefront;
    private readonly FileLogger? _logger;

    public AppleMusicCatalogMetadataEnricher(string developerToken, string storefront = "us", FileLogger? logger = null)
    {
        _developerToken = developerToken;
        _storefront = string.IsNullOrWhiteSpace(storefront) ? "us" : storefront.ToLowerInvariant();
        _logger = logger;
    }

    public async Task<TrackEnrichmentResult?> EnrichAsync(TrackFingerprint fingerprint, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_developerToken))
        {
            return null;
        }

        try
        {
            var song = await SearchBestSongAsync(fingerprint, cancellationToken).ConfigureAwait(false);
            if (song is null)
            {
                return null;
            }

            var songId = song.Value.GetProperty("id").GetString();
            var detailedSong = string.IsNullOrWhiteSpace(songId)
                ? song.Value
                : await LookupSongAsync(songId, cancellationToken).ConfigureAwait(false) ?? song.Value;

            return MapSong(detailedSong, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync($"Catalog metadata enrichment failed: {ex.Message}", cancellationToken).ConfigureAwait(false);
            }

            return null;
        }
    }

    private async Task<JsonElement?> SearchBestSongAsync(TrackFingerprint fingerprint, CancellationToken cancellationToken)
    {
        var query = $"{fingerprint.NormalizedTitle} {fingerprint.NormalizedArtist} {fingerprint.NormalizedAlbum}".Trim();
        var url = $"https://api.music.apple.com/v1/catalog/{_storefront}/search?term={Uri.EscapeDataString(query)}&types=songs&limit=10";
        using var request = CreateRequest(url);
        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("results", out var results)
            || !results.TryGetProperty("songs", out var songs)
            || !songs.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        JsonElement? best = null;
        var bestScore = int.MinValue;
        foreach (var song in data.EnumerateArray())
        {
            var score = ScoreCandidate(song, fingerprint);
            if (score > bestScore)
            {
                best = song.Clone();
                bestScore = score;
            }
        }

        return bestScore >= 100 ? best : null;
    }

    private async Task<JsonElement?> LookupSongAsync(string songId, CancellationToken cancellationToken)
    {
        var url = $"https://api.music.apple.com/v1/catalog/{_storefront}/songs/{songId}?include=albums,artists";
        using var request = CreateRequest(url);
        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array
            || data.GetArrayLength() == 0)
        {
            return null;
        }

        return data[0].Clone();
    }

    private HttpRequestMessage CreateRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _developerToken);
        return request;
    }

    private static int ScoreCandidate(JsonElement song, TrackFingerprint fingerprint)
    {
        if (!song.TryGetProperty("attributes", out var attributes))
        {
            return int.MinValue;
        }

        var title = TrackFingerprint.Normalize(TryGetString(attributes, "name"));
        var artist = TrackFingerprint.Normalize(TryGetString(attributes, "artistName"));
        if (title != fingerprint.NormalizedTitle || artist != fingerprint.NormalizedArtist)
        {
            return int.MinValue;
        }

        var score = 100;
        var album = TrackFingerprint.Normalize(TryGetString(attributes, "albumName"));
        if (!string.IsNullOrWhiteSpace(fingerprint.NormalizedAlbum) && album == fingerprint.NormalizedAlbum)
        {
            score += 25;
        }

        return score;
    }

    private TrackEnrichmentResult MapSong(JsonElement song, DateTimeOffset enrichedAtUtc)
    {
        var attributes = song.GetProperty("attributes");
        var artworkUrl = GetArtworkUrl(attributes, out var artworkWidth, out var artworkHeight);
        var genreNames = TryGetStringArray(attributes, "genreNames");
        var audioVariants = ExtractAudioVariants(song);
        var albumId = GetRelatedId(song, "albums", out var albumUrl);
        var artistId = GetRelatedId(song, "artists", out var artistUrl);

        return new TrackEnrichmentResult(
            TryGetString(attributes, "url"),
            albumUrl,
            artistUrl,
            song.GetProperty("id").GetString(),
            albumId,
            artistId,
            null,
            null,
            null,
            TryGetInt32(attributes, "durationInMillis") is { } durationMs ? durationMs / 1000 : null,
            TryGetDate(attributes, "releaseDate"),
            TryGetString(attributes, "composerName"),
            genreNames,
            TryGetInt32(attributes, "trackNumber"),
            null,
            TryGetInt32(attributes, "discNumber"),
            null,
            TryGetString(attributes, "isrc"),
            TryGetPreviewUrl(attributes),
            TryGetString(attributes, "contentRating"),
            audioVariants,
            artworkUrl,
            artworkWidth,
            artworkHeight,
            _storefront,
            ["catalog"],
            null,
            null,
            song.GetRawText(),
            enrichedAtUtc);
    }

    private static string? GetArtworkUrl(JsonElement attributes, out int? width, out int? height)
    {
        width = null;
        height = null;
        if (!attributes.TryGetProperty("artwork", out var artwork))
        {
            return null;
        }

        width = TryGetInt32(artwork, "width");
        height = TryGetInt32(artwork, "height");
        var template = TryGetString(artwork, "url");
        if (string.IsNullOrWhiteSpace(template))
        {
            return null;
        }

        var resolvedWidth = width ?? 1000;
        var resolvedHeight = height ?? 1000;
        return template
            .Replace("{w}", resolvedWidth.ToString(), StringComparison.Ordinal)
            .Replace("{h}", resolvedHeight.ToString(), StringComparison.Ordinal)
            .Replace("{f}", "jpg", StringComparison.Ordinal);
    }

    private static string? GetRelatedId(JsonElement song, string relationshipName, out string? url)
    {
        url = null;
        if (!song.TryGetProperty("relationships", out var relationships)
            || !relationships.TryGetProperty(relationshipName, out var relationship)
            || !relationship.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array
            || data.GetArrayLength() == 0)
        {
            return null;
        }

        var first = data[0];
        if (first.TryGetProperty("attributes", out var attributes))
        {
            url = TryGetString(attributes, "url");
        }

        return first.TryGetProperty("id", out var id) ? id.GetString() : null;
    }

    private static IReadOnlyList<string>? ExtractAudioVariants(JsonElement song)
    {
        var values = new List<string>();
        CollectAudioVariantValues(song, values);
        var normalized = values
            .Select(NormalizeCatalogVariant)
            .OfType<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return normalized.Length == 0 ? null : normalized;
    }

    private static void CollectAudioVariantValues(JsonElement element, List<string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("audioVariants") || property.NameEquals("audioTraits"))
                    {
                        CollectStringValues(property.Value, values);
                    }
                    else
                    {
                        CollectAudioVariantValues(property.Value, values);
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectAudioVariantValues(item, values);
                }
                break;
        }
    }

    private static void CollectStringValues(JsonElement element, List<string> values)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            values.Add(element.GetString() ?? string.Empty);
            return;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                values.Add(item.GetString() ?? string.Empty);
            }
        }
    }

    private static string? NormalizeCatalogVariant(string rawValue)
    {
        var sanitized = PlaybackAudioVariantParser.NormalizeBadge(rawValue);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return null;
        }

        return PlaybackAudioVariantParser.ParseBadge(sanitized) switch
        {
            PlaybackAudioVariant.Lossless => "Lossless",
            PlaybackAudioVariant.HiResLossless => "Hi-Res Lossless",
            PlaybackAudioVariant.DolbyAudio => "Dolby Audio",
            PlaybackAudioVariant.DolbyAtmos => "Dolby Atmos",
            PlaybackAudioVariant.Unknown => "Unknown",
            PlaybackAudioVariant.Other => sanitized,
            _ => sanitized
        };
    }

    private static string? TryGetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? TryGetInt32(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : null;

    private static DateTimeOffset? TryGetDate(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;

    private static IReadOnlyList<string>? TryGetStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
        return values.Length == 0 ? null : values;
    }

    private static string? TryGetPreviewUrl(JsonElement attributes)
    {
        if (!attributes.TryGetProperty("previews", out var previews)
            || previews.ValueKind != JsonValueKind.Array
            || previews.GetArrayLength() == 0)
        {
            return null;
        }

        return previews[0].TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String
            ? url.GetString()
            : null;
    }
}
