using System.Text.Json;
using AppleMusicHistory.Core.Models;
using AppleMusicHistory.Infrastructure.Data;
using AppleMusicHistory.Infrastructure.Settings;

namespace AppleMusicHistory.Host;

internal static class DashboardStateMapper
{
    public static DashboardState Map(RuntimeStatus? status, TrackerSettings settings)
    {
        if (status is null)
        {
            return DashboardState.CreateDefault(
                settings.Options.DatabasePath,
                settings.Options.LaunchAtStartup,
                settings.Options.MetadataEnrichmentEnabled,
                settings.TrackingPaused);
        }

        var details = status.CurrentTrackDetails;
        var snapshot = status.CurrentSnapshot;
        var durationSeconds = snapshot?.DurationSeconds;
        var currentPositionSeconds = snapshot?.CurrentPositionSeconds;
        var progress = CalculateProgress(currentPositionSeconds, durationSeconds);
        var currentTrack = status.SourceState == AppleMusicSnapshotReadState.Available && snapshot is not null
            ? $"{snapshot.Title} | {snapshot.Artist} | {snapshot.Album}"
            : "No current track";
        var currentTitle = snapshot?.Title ?? "No current track";
        var currentArtist = details?.Track.Artist ?? snapshot?.Artist ?? string.Empty;
        var currentAlbum = details?.Track.Album ?? snapshot?.Album ?? string.Empty;
        var currentArtwork = ResolveArtworkPathOrUrl(details);

        return new DashboardState(
            status.IsTrackingPaused,
            settings.Options.LaunchAtStartup,
            settings.Options.MetadataEnrichmentEnabled,
            status.IsTrackingPaused ? "Tracking paused" : "Tracking active",
            FormatAppleMusicState(status),
            currentTrack,
            currentTitle,
            currentArtist,
            currentAlbum,
            details?.Metadata?.ComposerName ?? string.Empty,
            details?.Metadata?.ReleaseDateUtc?.ToLocalTime().ToString("d") ?? string.Empty,
            FormatJsonStringArray(details?.Metadata?.GenreNamesJson),
            FormatTrackNumbers(details?.Metadata),
            details?.Metadata?.Isrc ?? string.Empty,
            details?.Metadata?.AppleMusicSongUrl ?? details?.Track.SongUrl ?? string.Empty,
            details?.Metadata?.AppleMusicAlbumUrl ?? string.Empty,
            details?.Metadata?.AppleMusicArtistUrl ?? details?.Track.ArtistUrl ?? string.Empty,
            currentArtwork,
            status.ActiveSession is null
                ? "No active session"
                : $"Session #{status.ActiveSession.SessionId} | Replay {status.ActiveSession.ReplayIndex} | Last pos {status.ActiveSession.LastPositionSeconds}s",
            $"Tracks: {status.Statistics.TrackCount} | Sessions: {status.Statistics.SessionCount} | Open: {status.Statistics.OpenSessionCount}",
            status.Statistics.LastObservedUtc?.ToLocalTime().ToString("G") ?? "Never",
            settings.Options.DatabasePath,
            status.SourceState == AppleMusicSnapshotReadState.Available && snapshot is not null
                ? PlaybackAudioVariantParser.ToDisplayName(snapshot.ObservedAudioVariant, snapshot.ObservedAudioBadgeRaw)
                : "Standard / unknown",
            status.SourceDiagnosticMessage ?? string.Empty,
            currentPositionSeconds,
            durationSeconds,
            progress,
            FormatElapsed(currentPositionSeconds),
            FormatRemaining(currentPositionSeconds, durationSeconds),
            $"{currentArtwork}|{currentTitle}|{currentArtist}|{currentAlbum}");
    }

    private static string FormatAppleMusicState(RuntimeStatus status)
    {
        return status.SourceState switch
        {
            AppleMusicSnapshotReadState.AppNotRunning => "Apple Music not running",
            AppleMusicSnapshotReadState.NoTrackDetected => "Apple Music open, no track detected",
            AppleMusicSnapshotReadState.Recovering => "Apple Music running, reconnecting",
            AppleMusicSnapshotReadState.Available when status.CurrentSnapshot?.IsPaused == true => "Apple Music paused",
            AppleMusicSnapshotReadState.Available => "Apple Music playing",
            _ => "Apple Music status unknown"
        };
    }

    private static double CalculateProgress(int? currentPositionSeconds, int? durationSeconds)
    {
        if (!currentPositionSeconds.HasValue || !durationSeconds.HasValue || durationSeconds.Value <= 0)
        {
            return 0;
        }

        return Math.Clamp((double)currentPositionSeconds.Value / durationSeconds.Value, 0, 1);
    }

    private static string ResolveArtworkPathOrUrl(TrackDetailsRecord? details)
    {
        var relativePath = details?.Metadata?.ArtworkCacheRelativePath;
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            return Path.Combine(AppPaths.AppDataDirectory, relativePath);
        }

        return details?.Metadata?.ArtworkUrl
            ?? details?.Track.ArtworkUrl
            ?? string.Empty;
    }

    private static string FormatJsonStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            var items = JsonSerializer.Deserialize<string[]>(json);
            return items is null ? string.Empty : string.Join(", ", items.Where(item => !string.IsNullOrWhiteSpace(item)));
        }
        catch
        {
            return json;
        }
    }

    private static string FormatTrackNumbers(TrackMetadataRecord? metadata)
    {
        if (metadata is null)
        {
            return string.Empty;
        }

        var track = metadata.TrackNumber is null
            ? string.Empty
            : $"Track {metadata.TrackNumber}/{metadata.TrackCount?.ToString() ?? "?"}";
        var disc = metadata.DiscNumber is null
            ? string.Empty
            : $"Disc {metadata.DiscNumber}/{metadata.DiscCount?.ToString() ?? "?"}";
        return string.Join(" | ", new[] { track, disc }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string FormatElapsed(int? currentPositionSeconds)
        => FormatTime(currentPositionSeconds.GetValueOrDefault());

    private static string FormatRemaining(int? currentPositionSeconds, int? durationSeconds)
    {
        if (!durationSeconds.HasValue)
        {
            return "-0:00";
        }

        var remaining = Math.Max(0, durationSeconds.Value - currentPositionSeconds.GetValueOrDefault());
        return $"-{FormatTime(remaining)}";
    }

    private static string FormatTime(int seconds)
    {
        var safeSeconds = Math.Max(0, seconds);
        var timeSpan = TimeSpan.FromSeconds(safeSeconds);
        return timeSpan.TotalHours >= 1
            ? $"{(int)timeSpan.TotalHours}:{timeSpan:mm\\:ss}"
            : timeSpan.ToString("m\\:ss");
    }
}
