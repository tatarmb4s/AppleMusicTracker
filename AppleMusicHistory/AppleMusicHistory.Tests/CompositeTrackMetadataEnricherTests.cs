using AppleMusicHistory.Core.Abstractions;
using AppleMusicHistory.Core.Models;
using AppleMusicHistory.Infrastructure.Scraping;

namespace AppleMusicHistory.Tests;

public sealed class CompositeTrackMetadataEnricherTests
{
    [Fact]
    public async Task Merge_PrefersLaterStructuredFields_AndPreservesRawPayloads()
    {
        var enricher = new CompositeTrackMetadataEnricher(
        [
            new StaticEnricher(new TrackEnrichmentResult(
                "https://music.apple.com/us/song/web-song",
                "https://music.apple.com/us/album/web-album",
                "https://music.apple.com/us/artist/web-artist",
                null,
                null,
                null,
                null,
                null,
                null,
                180,
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
                "https://example.com/web.jpg",
                600,
                600,
                "us",
                ["web"],
                "{\"web\":true}",
                null,
                null,
                DateTimeOffset.Parse("2026-03-09T12:00:00Z"))),
            new StaticEnricher(new TrackEnrichmentResult(
                "https://music.apple.com/us/song/catalog-song",
                "https://music.apple.com/us/album/catalog-album",
                "https://music.apple.com/us/artist/catalog-artist",
                "catalog-song",
                "catalog-album",
                "catalog-artist",
                null,
                null,
                null,
                181,
                DateTimeOffset.Parse("2026-03-01T00:00:00Z"),
                "Composer",
                ["Electronic"],
                4,
                12,
                1,
                2,
                "USRC17607839",
                "https://example.com/preview.m4a",
                "explicit",
                ["Lossless"],
                "https://example.com/catalog.jpg",
                1000,
                1000,
                "us",
                ["catalog"],
                null,
                null,
                "{\"catalog\":true}",
                DateTimeOffset.Parse("2026-03-09T12:01:00Z")))
        ]);

        var result = await enricher.EnrichAsync(TrackFingerprint.From("Song", "Artist", "Album"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("catalog-song", result!.CatalogSongId);
        Assert.Equal(181, result.DurationSeconds);
        Assert.Equal("{\"web\":true}", result.WebPayloadJson);
        Assert.Equal("{\"catalog\":true}", result.CatalogPayloadJson);
        Assert.Contains("web", result.MetadataSources);
        Assert.Contains("catalog", result.MetadataSources);
    }

    private sealed class StaticEnricher : ITrackMetadataEnricher
    {
        private readonly TrackEnrichmentResult _result;

        public StaticEnricher(TrackEnrichmentResult result)
        {
            _result = result;
        }

        public Task<TrackEnrichmentResult?> EnrichAsync(TrackFingerprint fingerprint, CancellationToken cancellationToken)
            => Task.FromResult<TrackEnrichmentResult?>(_result);
    }
}
