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

    public async Task<TrackMetadata?> EnrichAsync(TrackFingerprint fingerprint, CancellationToken cancellationToken)
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

            var variants = ExtractAudioVariants(song.Value);
            if (variants.Count == 0)
            {
                return null;
            }

            return new TrackMetadata(
                null,
                null,
                null,
                null,
                JsonSerializer.Serialize(variants),
                DateTimeOffset.UtcNow);
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

        JsonElement? fallback = null;
        foreach (var song in data.EnumerateArray())
        {
            fallback ??= song.Clone();
            if (MatchesFingerprint(song, fingerprint))
            {
                return song.Clone();
            }
        }

        return fallback;
    }

    private HttpRequestMessage CreateRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _developerToken);
        return request;
    }

    private static bool MatchesFingerprint(JsonElement song, TrackFingerprint fingerprint)
    {
        if (!song.TryGetProperty("attributes", out var attributes))
        {
            return false;
        }

        var title = attributes.TryGetProperty("name", out var nameElement) ? TrackFingerprint.Normalize(nameElement.GetString()) : string.Empty;
        var artist = attributes.TryGetProperty("artistName", out var artistElement) ? TrackFingerprint.Normalize(artistElement.GetString()) : string.Empty;
        var album = attributes.TryGetProperty("albumName", out var albumElement) ? TrackFingerprint.Normalize(albumElement.GetString()) : string.Empty;

        return title == fingerprint.NormalizedTitle
            && artist == fingerprint.NormalizedArtist
            && (string.IsNullOrWhiteSpace(fingerprint.NormalizedAlbum) || album == fingerprint.NormalizedAlbum);
    }

    private static List<string> ExtractAudioVariants(JsonElement song)
    {
        var values = new List<string>();
        CollectAudioVariantValues(song, values);

        return values
            .Select(NormalizeCatalogVariant)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList()!;
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
                        continue;
                    }

                    CollectAudioVariantValues(property.Value, values);
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
}
