using AppleMusicHistory.Core.Models;

namespace AppleMusicHistory.Core.Abstractions;

public interface IAppleMusicSnapshotSource
{
    Task<PlaybackSnapshot?> GetCurrentAsync(CancellationToken cancellationToken);
}
