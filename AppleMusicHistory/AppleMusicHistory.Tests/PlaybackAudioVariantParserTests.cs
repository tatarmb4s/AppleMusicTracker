using AppleMusicHistory.Core.Models;

namespace AppleMusicHistory.Tests;

public sealed class PlaybackAudioVariantParserTests
{
    [Theory]
    [InlineData("Lossless", PlaybackAudioVariant.Lossless)]
    [InlineData("Hi-Res Lossless", PlaybackAudioVariant.HiResLossless)]
    [InlineData("Dolby Audio", PlaybackAudioVariant.DolbyAudio)]
    [InlineData("Dolby Atmos", PlaybackAudioVariant.DolbyAtmos)]
    [InlineData("Surround Deluxe", PlaybackAudioVariant.Other)]
    public void ParseBadge_MapsKnownValues(string badge, PlaybackAudioVariant expected)
    {
        Assert.Equal(expected, PlaybackAudioVariantParser.ParseBadge(badge));
    }

    [Fact]
    public void ParseBadge_ReturnsNullForMissingBadge()
    {
        Assert.Null(PlaybackAudioVariantParser.ParseBadge(null));
        Assert.Null(PlaybackAudioVariantParser.ParseBadge(" "));
    }
}
