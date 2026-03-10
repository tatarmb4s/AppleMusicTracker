namespace AppleMusicHistory.Core.Models;

public sealed record TrackerOptions
{
    public required string DatabasePath { get; init; }
    public TimeSpan ActivePollingInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MissingAppPollingInterval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan PausedPollingInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan ResumeGap { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan CheckpointInterval { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan ReplayStartThreshold { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan ReplayEndThreshold { get; init; } = TimeSpan.FromSeconds(5);
    public bool MetadataEnrichmentEnabled { get; init; } = true;
    public bool LaunchAtStartup { get; init; } = true;
}
