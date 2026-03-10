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
    private static readonly Regex ImageUrlRegex = new(@"http\S*?(?= \d{2,3}w)", RegexOptions.Compiled);
    private readonly string _region;
    private readonly FileLogger? _logger;

    public AppleMusicWebMetadataEnricher(string region = "us", FileLogger? logger = null)
    {
        _region = region.ToLowerInvariant();
        _logger = logger;
    }

    public async Task<TrackMetadata?> EnrichAsync(TrackFingerprint fingerprint, CancellationToken cancellationToken)
    {
        try
        {
            var result = await SearchSongsAsync(fingerprint, cancellationToken).ConfigureAwait(false)
                ?? await SearchTopResultsAsync(fingerprint, cancellationToken).ConfigureAwait(false);

            if (result is null)
            {
                return null;
            }

            var songUrl = GetSongUrl(result);
            var artistUrl = GetArtistUrl(result);
            var artworkUrl = GetLargestImageUrl(result);
            var durationSeconds = songUrl is null
                ? null
                : await GetSongDurationFromAlbumPageAsync(songUrl, fingerprint, cancellationToken).ConfigureAwait(false);

            return new TrackMetadata(durationSeconds, songUrl, artistUrl, artworkUrl, null, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync($"Metadata enrichment failed: {ex.Message}", cancellationToken).ConfigureAwait(false);
            }

            return null;
        }
    }

    private async Task<HtmlNode?> SearchSongsAsync(TrackFingerprint fingerprint, CancellationToken cancellationToken)
    {
        var doc = await GetDocumentAsync(GetSearchUrl(fingerprint), cancellationToken).ConfigureAwait(false);
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

    private async Task<HtmlNode?> SearchTopResultsAsync(TrackFingerprint fingerprint, CancellationToken cancellationToken)
    {
        var doc = await GetDocumentAsync(GetSearchUrl(fingerprint), cancellationToken).ConfigureAwait(false);
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
        var text = HttpUtility.HtmlDecode(result.InnerText).ToLowerInvariant();
        return text.Contains(fingerprint.NormalizedTitle, StringComparison.Ordinal)
            && text.Contains(fingerprint.NormalizedArtist, StringComparison.Ordinal);
    }

    private string GetSearchUrl(TrackFingerprint fingerprint)
    {
        var rawSearch = $"{fingerprint.NormalizedTitle} {fingerprint.NormalizedAlbum} {fingerprint.NormalizedArtist}";
        while (rawSearch.Length > 100)
        {
            rawSearch = rawSearch[..rawSearch.LastIndexOf(' ')];
        }

        return $"https://music.apple.com/{_region}/search?term={Uri.EscapeDataString(rawSearch)}";
    }

    private static string? GetSongUrl(HtmlNode source)
    {
        var node = source.SelectSingleNode(".//a[@data-testid='click-action']");
        var href = node?.GetAttributeValue("href", string.Empty);
        return string.IsNullOrWhiteSpace(href) ? null : href;
    }

    private static string? GetArtistUrl(HtmlNode source)
    {
        var subtitleNode = source.Descendants("span")
            .FirstOrDefault(x => x.Attributes.Contains("data-testid") && x.Attributes["data-testid"].Value == "track-lockup-subtitle");
        var artistLink = subtitleNode?.Descendants("a")
            .FirstOrDefault(x => x.Attributes["href"].Value.Contains("/artist/", StringComparison.Ordinal));
        var href = artistLink?.GetAttributeValue("href", string.Empty);
        return string.IsNullOrWhiteSpace(href) ? null : href;
    }

    private static string? GetLargestImageUrl(HtmlNode source)
    {
        var imgSources = source
            .Descendants("source")
            .Where(x => x.Attributes["type"]?.Value == "image/jpeg")
            .ToList();

        if (imgSources.Count == 0)
        {
            return null;
        }

        var srcset = imgSources[0].Attributes["srcset"]?.Value;
        if (string.IsNullOrWhiteSpace(srcset))
        {
            return null;
        }

        var match = ImageUrlRegex.Matches(srcset).LastOrDefault();
        if (match is null)
        {
            return null;
        }

        return Regex.Replace(match.Value, @"/\d+x\d+.*\.jpg$", "/1024x1024bb.jpg");
    }

    private async Task<int?> GetSongDurationFromAlbumPageAsync(string url, TrackFingerprint fingerprint, CancellationToken cancellationToken)
    {
        var doc = await GetDocumentAsync(url, cancellationToken).ConfigureAwait(false);
        try
        {
            var durationNode = doc.DocumentNode.Descendants("meta")
                .FirstOrDefault(x => x.Attributes.Contains("property") && x.Attributes["property"].Value == "music:song:duration");
            if (durationNode is not null)
            {
                return ParseIsoDuration(durationNode.GetAttributeValue("content", string.Empty));
            }

            var descNode = doc.DocumentNode.Descendants("meta")
                .FirstOrDefault(x => x.Attributes.Contains("property") && x.Attributes["property"].Value == "og:description");
            var titleNode = doc.DocumentNode.Descendants("meta")
                .FirstOrDefault(x => x.Attributes.Contains("property") && x.Attributes["property"].Value == "og:title");

            var decodedDesc = HttpUtility.HtmlDecode(descNode?.GetAttributeValue("content", string.Empty) ?? string.Empty);
            var decodedTitle = HttpUtility.HtmlDecode(titleNode?.GetAttributeValue("content", string.Empty) ?? string.Empty);
            if (!decodedDesc.Contains(fingerprint.NormalizedTitle, StringComparison.OrdinalIgnoreCase)
                && !decodedTitle.Contains(fingerprint.NormalizedTitle, StringComparison.OrdinalIgnoreCase))
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

    private async Task<HtmlDocument> GetDocumentAsync(string url, CancellationToken cancellationToken)
    {
        var cleanUrl = HttpUtility.HtmlEncode(url.Replace("&", " ", StringComparison.Ordinal));
        var response = await HttpClient.GetStringAsync(cleanUrl, cancellationToken).ConfigureAwait(false);
        var doc = new HtmlDocument();
        doc.LoadHtml(response);
        return doc;
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
        if (parts.Length != 2)
        {
            return null;
        }

        return int.TryParse(parts[0], out var minutes) && int.TryParse(parts[1], out var seconds)
            ? minutes * 60 + seconds
            : null;
    }
}
