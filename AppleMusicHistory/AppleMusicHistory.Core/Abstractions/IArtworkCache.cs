using AppleMusicHistory.Core.Models;

namespace AppleMusicHistory.Core.Abstractions;

public interface IArtworkCache
{
    Task<CachedArtworkResult?> CacheAsync(string artworkUrl, CancellationToken cancellationToken);
}
