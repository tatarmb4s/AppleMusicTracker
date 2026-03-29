using AppleMusicHistory.Core.Abstractions;
using AppleMusicHistory.Core.Models;

namespace AppleMusicHistory.Infrastructure.Scraping;

public sealed class CompositeTrackMetadataEnricher : ITrackMetadataEnricher
{
    private readonly IReadOnlyList<ITrackMetadataEnricher> _enrichers;

    public CompositeTrackMetadataEnricher(IEnumerable<ITrackMetadataEnricher> enrichers)
    {
        _enrichers = enrichers.ToList();
    }

    public async Task<TrackEnrichmentResult?> EnrichAsync(TrackFingerprint fingerprint, CancellationToken cancellationToken)
    {
        if (_enrichers.Count == 0)
        {
            return null;
        }

        var tasks = _enrichers
            .Select(enricher => enricher.EnrichAsync(fingerprint, cancellationToken))
            .ToArray();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        TrackEnrichmentResult? merged = null;
        foreach (var result in results.Where(result => result is not null))
        {
            merged = merged is null ? result! : Merge(merged, result!);
        }

        return merged;
    }

    private static TrackEnrichmentResult Merge(TrackEnrichmentResult current, TrackEnrichmentResult incoming)
    {
        return new TrackEnrichmentResult(
            incoming.AppleMusicSongUrl ?? current.AppleMusicSongUrl,
            incoming.AppleMusicAlbumUrl ?? current.AppleMusicAlbumUrl,
            incoming.AppleMusicArtistUrl ?? current.AppleMusicArtistUrl,
            incoming.CatalogSongId ?? current.CatalogSongId,
            incoming.CatalogAlbumId ?? current.CatalogAlbumId,
            incoming.CatalogArtistId ?? current.CatalogArtistId,
            incoming.ItunesTrackId ?? current.ItunesTrackId,
            incoming.ItunesCollectionId ?? current.ItunesCollectionId,
            incoming.ItunesArtistId ?? current.ItunesArtistId,
            incoming.DurationSeconds ?? current.DurationSeconds,
            incoming.ReleaseDateUtc ?? current.ReleaseDateUtc,
            incoming.ComposerName ?? current.ComposerName,
            incoming.GenreNames ?? current.GenreNames,
            incoming.TrackNumber ?? current.TrackNumber,
            incoming.TrackCount ?? current.TrackCount,
            incoming.DiscNumber ?? current.DiscNumber,
            incoming.DiscCount ?? current.DiscCount,
            incoming.Isrc ?? current.Isrc,
            incoming.PreviewUrl ?? current.PreviewUrl,
            incoming.ContentRating ?? current.ContentRating,
            incoming.CatalogAudioVariants ?? current.CatalogAudioVariants,
            incoming.ArtworkUrl ?? current.ArtworkUrl,
            incoming.ArtworkWidth ?? current.ArtworkWidth,
            incoming.ArtworkHeight ?? current.ArtworkHeight,
            incoming.Storefront ?? current.Storefront,
            current.MetadataSources.Concat(incoming.MetadataSources).Distinct(StringComparer.Ordinal).ToArray(),
            incoming.WebPayloadJson ?? current.WebPayloadJson,
            incoming.ItunesPayloadJson ?? current.ItunesPayloadJson,
            incoming.CatalogPayloadJson ?? current.CatalogPayloadJson,
            incoming.EnrichedAtUtc > current.EnrichedAtUtc ? incoming.EnrichedAtUtc : current.EnrichedAtUtc);
    }
}
