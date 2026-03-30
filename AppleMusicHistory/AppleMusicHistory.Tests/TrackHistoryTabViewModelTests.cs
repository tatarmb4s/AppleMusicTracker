using AppleMusicHistory.Core.Models;
using AppleMusicHistory.WinUI.ViewModels;

namespace AppleMusicHistory.Tests;

public sealed class TrackHistoryTabViewModelTests
{
    [Fact]
    public async Task Filters_ApplyPerColumn_WithAndSemantics()
    {
        var viewModel = CreateViewModel(
            new TrackHistoryRow(1, "Before You Go", "Lewis Capaldi", "Divinely Uninspired", "Extended", "[\"Lossless\"]", "Lossless", PlaybackAudioVariant.Lossless, null, null, null, null, null, DateTimeOffset.UtcNow),
            new TrackHistoryRow(2, "Astronaut In The Ocean", "Masked Wolf", "Astronaut In The Ocean - Single", "Single", "[\"Dolby Atmos\"]", "Dolby Atmos", PlaybackAudioVariant.DolbyAtmos, null, null, null, null, null, DateTimeOffset.UtcNow));

        await viewModel.EnsureLoadedAsync();

        viewModel.TitleFilter = "astronaut";
        viewModel.ArtistFilter = "masked";
        await Task.Delay(300);

        var row = Assert.Single(viewModel.FilteredRows);
        Assert.Equal(2, row.TrackId);
    }

    [Fact]
    public async Task TrackIdFilter_MatchesNumericText()
    {
        var viewModel = CreateViewModel(
            new TrackHistoryRow(12, "Song", "Artist", "Album", "Subtitle", null, null, PlaybackAudioVariant.Unknown, null, null, null, null, null, DateTimeOffset.UtcNow),
            new TrackHistoryRow(55, "Other", "Artist", "Album", "Subtitle", null, null, PlaybackAudioVariant.Unknown, null, null, null, null, null, DateTimeOffset.UtcNow));

        await viewModel.EnsureLoadedAsync();

        viewModel.TrackIdFilter = "55";
        await Task.Delay(300);

        var row = Assert.Single(viewModel.FilteredRows);
        Assert.Equal(55, row.TrackId);
    }

    [Fact]
    public async Task AudioBadgeMapping_UsesExistingAssetResolver()
    {
        var viewModel = CreateViewModel(
            new TrackHistoryRow(1, "Song", "Artist", "Album", "Subtitle", null, "Lossless", PlaybackAudioVariant.Lossless, null, null, null, null, null, DateTimeOffset.UtcNow));

        await viewModel.EnsureLoadedAsync();

        var row = Assert.Single(viewModel.FilteredRows);
        Assert.Contains("losless.png", row.AudioBadgeAssetUri, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Lossless", row.AudioBadgeLabel);
    }

    private static TrackHistoryTabViewModel CreateViewModel(params TrackHistoryRow[] rows)
    {
        return new TrackHistoryTabViewModel(_ => Task.FromResult((IReadOnlyList<TrackHistoryRow>)rows));
    }
}
