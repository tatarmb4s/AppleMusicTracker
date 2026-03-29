using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using AppleMusicHistory.Core.Abstractions;
using AppleMusicHistory.Core.Models;
using AppleMusicHistory.Infrastructure.Data;
using HtmlAgilityPack;

namespace AppleMusicHistory.Infrastructure.Scraping;

public sealed class AppleMusicWebMetadataEnricher : ITrackMetadataEnricher
{
    private static readonly HttpClient HttpClient = new();
    private static readonly Regex DurationRegex = new(@"(\d{1,3}:\d{2})", RegexOptions.Compiled);
    private static readonly Regex ArtworkSizeRegex = new(@"/(?<width>\d+)x(?<height>\d+)", RegexOptions.Compiled);
    private readonly string _region;
    private readonly FileLogger? _logger;

    public AppleMusicWebMetadataEnricher(string region = "us", FileLogger? logger = null)
    {
        _region = string.IsNullOrWhiteSpace(region) ? "us" : region.ToLowerInvariant();
        _logger = logger;
    }

    public async Task<TrackEnrichmentResult?> EnrichAsync(TrackFingerprint fingerprint, CancellationToken cancellationToken)
    {
        try
        {
            var searchUrl = GetSearchUrl(fingerprint);
            var result = await SearchSongsAsync(searchUrl, fingerprint, cancellationToken).ConfigureAwait(false)
                ?? await SearchTopResultsAsync(searchUrl, fingerprint, cancellationToken).ConfigureAwait(false);

            if (result is null)
            {
                return null;
            }

            var songUrl = GetSongUrl(result);
            var artistUrl = GetArtistUrl(result);
            var albumUrl = GetAlbumUrl(result);
            var artworkUrl = GetLargestImageUrl(result);
            var (artworkWidth, artworkHeight) = ParseArtworkDimensions(artworkUrl);
            var durationSeconds = songUrl is null
                ? null
                : await GetSongDurationFromAlbumPageAsync(songUrl, fingerprint, cancellationToken).ConfigureAwait(false);

            var payload = JsonSerializer.Serialize(new
            {
                searchUrl,
                songUrl,
                albumUrl,
                artistUrl,
                artworkUrl,
                artworkWidth,
                artworkHeight,
                durationSeconds,
                matchedResultHtml = result.OuterHtml
            });

            return new TrackEnrichmentResult(
                songUrl,
                albumUrl,
                artistUrl,
                null,
                null,
                null,
                null,
                null,
                null,
                durationSeconds,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                artworkUrl,
                artworkWidth,
                artworkHeight,
                _region,
                ["web"],
                payload,
                null,
                null,
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync($"Web metadata enrichment failed: {ex.Message}", cancellationToken).ConfigureAwait(false);
            }

            return null;
        }
    }

    private async Task<HtmlNode?> SearchSongsAsync(string searchUrl, TrackFingerprint fingerprint, CancellationToken cancellationToken)
    {
        var doc = await GetDocumentAsync(searchUrl, cancellationToken).ConfigureAwait(false);
        try
        {
            var nodes = doc.DocumentNode
                .Descendants("div")
                .First(x => x.HasClass("desktop-search-page"))
                .Descendants("ul");

            var list = nodes
                .First(x => x.HasClass("shelf-grid__list--grid-type-TrackLockupsShelf"))
                .ChildNodes
                .Where(x => x.Name == "li");

            return list.FirstOrDefault(result => MatchesFingerprint(result, fingerprint));
        }
        catch
        {
            return null;
        }
    }

    private async Task<HtmlNode?> SearchTopResultsAsync(string searchUrl, TrackFingerprint fingerprint, CancellationToken cancellationToken)
    {
        var doc = await GetDocumentAsync(searchUrl, cancellationToken).ConfigureAwait(false);
        try
        {
            var nodes = doc.DocumentNode
                .Descendants("ul")
                .FirstOrDefault(x => x.Attributes["class"]?.Value.Contains("grid--top-results", StringComparison.Ordinal) == true);
            if (nodes is null)
            {
                return null;
            }

            var results = nodes
                .Descendants("li")
                .Where(x => x.Attributes.Contains("data-testid") && x.Attributes["data-testid"].Value == "grid-item");
            return results.FirstOrDefault(result => MatchesFingerprint(result, fingerprint));
        }
        catch
        {
            return null;
        }
    }

    private static bool MatchesFingerprint(HtmlNode result, TrackFingerprint fingerprint)
    {
        var text = TrackFingerprint.Normalize(HttpUtility.HtmlDecode(result.InnerText));
        return text.Contains(fingerprint.NormalizedTitle, StringComparison.Ordinal)
            && text.Contains(fingerprint.NormalizedArtist, StringComparison.Ordinal);
    }

    private string GetSearchUrl(TrackFingerprint fingerprint)
    {
        var rawSearch = $"{fingerprint.NormalizedTitle} {fingerprint.NormalizedAlbum} {fingerprint.NormalizedArtist}".Trim();
        while (rawSearch.Length > 100)
        {
            rawSearch = rawSearch[..rawSearch.LastIndexOf(' ')];
        }

        return $"https://music.apple.com/{_region}/search?term={Uri.EscapeDataString(rawSearch)}";
    }

    private static string? GetSongUrl(HtmlNode source) => GetAbsoluteAppleMusicUrl(source.SelectSingleNode(".//a[@data-testid='click-action']")?.GetAttributeValue("href", string.Empty));

    private static string? GetArtistUrl(HtmlNode source)
    {
        var artistLink = source.Descendants("a")
            .FirstOrDefault(x => x.GetAttributeValue("href", string.Empty).Contains("/artist/", StringComparison.Ordinal));
        return GetAbsoluteAppleMusicUrl(artistLink?.GetAttributeValue("href", string.Empty));
    }

    private static string? GetAlbumUrl(HtmlNode source)
    {
        var albumLink = source.Descendants("a")
            .FirstOrDefault(x => x.GetAttributeValue("href", string.Empty).Contains("/album/", StringComparison.Ordinal));
        return GetAbsoluteAppleMusicUrl(albumLink?.GetAttributeValue("href", string.Empty));
    }

    private static string? GetLargestImageUrl(HtmlNode source)
    {
        var srcset = source.Descendants("source")
            .Where(x => x.Attributes["type"]?.Value == "image/jpeg")
            .Select(x => x.Attributes["srcset"]?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (string.IsNullOrWhiteSpace(srcset))
        {
            return null;
        }

        var parts = srcset.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var lastUrl = parts.LastOrDefault()?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(lastUrl) ? null : lastUrl;
    }

    private async Task<int?> GetSongDurationFromAlbumPageAsync(string url, TrackFingerprint fingerprint, CancellationToken cancellationToken)
    {
        var doc = await GetDocumentAsync(url, cancellationToken).ConfigureAwait(false);
        try
        {
            var durationNode = doc.DocumentNode.Descendants("meta")
                .FirstOrDefault(x => x.GetAttributeValue("property", string.Empty) == "music:song:duration");
            if (durationNode is not null)
            {
                return ParseIsoDuration(durationNode.GetAttributeValue("content", string.Empty));
            }

            var descNode = doc.DocumentNode.Descendants("meta")
                .FirstOrDefault(x => x.GetAttributeValue("property", string.Empty) == "og:description");
            var titleNode = doc.DocumentNode.Descendants("meta")
                .FirstOrDefault(x => x.GetAttributeValue("property", string.Empty) == "og:title");

            var decodedDesc = HttpUtility.HtmlDecode(descNode?.GetAttributeValue("content", string.Empty) ?? string.Empty);
            var decodedTitle = HttpUtility.HtmlDecode(titleNode?.GetAttributeValue("content", string.Empty) ?? string.Empty);
            if (!TrackFingerprint.Normalize(decodedDesc).Contains(fingerprint.NormalizedTitle, StringComparison.Ordinal)
                && !TrackFingerprint.Normalize(decodedTitle).Contains(fingerprint.NormalizedTitle, StringComparison.Ordinal))
            {
                return null;
            }

            var match = DurationRegex.Matches(decodedDesc).LastOrDefault();
            return match is null ? null : ParseClockDuration(match.Value);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<HtmlDocument> GetDocumentAsync(string url, CancellationToken cancellationToken)
    {
        var response = await HttpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        var doc = new HtmlDocument();
        doc.LoadHtml(response);
        return doc;
    }

    private static (int? Width, int? Height) ParseArtworkDimensions(string? artworkUrl)
    {
        if (string.IsNullOrWhiteSpace(artworkUrl))
        {
            return (null, null);
        }

        var match = ArtworkSizeRegex.Match(artworkUrl);
        return match.Success
            ? (int.Parse(match.Groups["width"].Value), int.Parse(match.Groups["height"].Value))
            : (null, null);
    }

    private static string? GetAbsoluteAppleMusicUrl(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        return href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? href
            : $"https://music.apple.com{href}";
    }

    private static int? ParseIsoDuration(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("PT", StringComparison.Ordinal))
        {
            return null;
        }

        var hours = Regex.Match(value, @"(\d+)H");
        var minutes = Regex.Match(value, @"(\d+)M");
        var seconds = Regex.Match(value, @"(\d+)S");
        var totalSeconds = 0;
        if (hours.Success)
        {
            totalSeconds += int.Parse(hours.Groups[1].Value) * 3600;
        }

        if (minutes.Success)
        {
            totalSeconds += int.Parse(minutes.Groups[1].Value) * 60;
        }

        if (seconds.Success)
        {
            totalSeconds += int.Parse(seconds.Groups[1].Value);
        }

        return totalSeconds == 0 ? null : totalSeconds;
    }

    private static int? ParseClockDuration(string value)
    {
        var parts = value.Split(':');
        return parts.Length == 2 && int.TryParse(parts[0], out var minutes) && int.TryParse(parts[1], out var seconds)
            ? minutes * 60 + seconds
            : null;
    }
}
