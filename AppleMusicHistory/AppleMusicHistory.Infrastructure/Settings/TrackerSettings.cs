using AppleMusicHistory.Core.Models;
using AppleMusicHistory.Infrastructure.Data;

namespace AppleMusicHistory.Infrastructure.Settings;

public sealed record TrackerSettings
{
    public TrackerOptions Options { get; init; } = new()
    {
        DatabasePath = AppPaths.DatabasePath
    };

    public bool TrackingPaused { get; init; }
}
