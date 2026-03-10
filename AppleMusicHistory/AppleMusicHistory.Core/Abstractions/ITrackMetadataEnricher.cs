using AppleMusicHistory.Core.Models;

namespace AppleMusicHistory.Core.Abstractions;

public interface ITrackMetadataEnricher
{
    Task<TrackMetadata?> EnrichAsync(TrackFingerprint fingerprint, CancellationToken cancellationToken);
}
