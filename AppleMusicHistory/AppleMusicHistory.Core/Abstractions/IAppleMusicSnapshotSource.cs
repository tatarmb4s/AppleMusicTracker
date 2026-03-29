using AppleMusicHistory.Core.Models;

namespace AppleMusicHistory.Core.Abstractions;

public interface IAppleMusicSnapshotSource
{
    Task<AppleMusicSnapshotReadResult> GetCurrentAsync(CancellationToken cancellationToken);
}
