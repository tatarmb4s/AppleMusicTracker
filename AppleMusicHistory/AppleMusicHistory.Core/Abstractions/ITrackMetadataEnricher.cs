using AppleMusicHistory.Core.Models;

namespace AppleMusicHistory.Core.Abstractions;

public interface ITrackMetadataEnricher
{
    Task<TrackEnrichmentResult?> EnrichAsync(TrackFingerprint fingerprint, CancellationToken cancellationToken);
}
