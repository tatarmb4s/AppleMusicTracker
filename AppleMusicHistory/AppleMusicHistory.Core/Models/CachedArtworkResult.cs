namespace AppleMusicHistory.Core.Models;

public sealed record CachedArtworkResult(
    string RelativePath,
    DateTimeOffset CachedAtUtc);
