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

    public async Task<TrackMetadata?> EnrichAsync(TrackFingerprint fingerprint, CancellationToken cancellationToken)
    {
        if (_enrichers.Count == 0)
        {
            return null;
        }

        var tasks = _enrichers
            .Select(enricher => enricher.EnrichAsync(fingerprint, cancellationToken))
            .ToArray();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        TrackMetadata? merged = null;
        foreach (var result in results)
        {
            if (result is null)
            {
                continue;
            }

            merged = merged is null
                ? result
                : new TrackMetadata(
                    result.DurationSeconds ?? merged.DurationSeconds,
                    result.SongUrl ?? merged.SongUrl,
                    result.ArtistUrl ?? merged.ArtistUrl,
                    result.ArtworkUrl ?? merged.ArtworkUrl,
                    result.CatalogAudioVariantsJson ?? merged.CatalogAudioVariantsJson,
                    result.EnrichedAtUtc > merged.EnrichedAtUtc ? result.EnrichedAtUtc : merged.EnrichedAtUtc);
        }

        return merged;
    }
}
