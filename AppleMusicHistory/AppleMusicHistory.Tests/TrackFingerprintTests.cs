using AppleMusicHistory.Core.Models;

namespace AppleMusicHistory.Tests;

public sealed class TrackFingerprintTests
{
    [Fact]
    public void Sanitize_RemovesInvisibleCharactersAndCollapsesWhitespace()
    {
        var value = "  Song\u200B   Title  ";

        var sanitized = TrackFingerprint.Sanitize(value);
        var fingerprint = TrackFingerprint.From("Song\u200B Title", "Artist", "Album");

        Assert.Equal("Song Title", sanitized);
        Assert.Equal("song title", fingerprint.NormalizedTitle);
    }
}
