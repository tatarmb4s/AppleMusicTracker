using System;
using System.IO;
using AppleMusicHistory.Core.Models;
using AppleMusicHistory.WinUI.Commands;

namespace AppleMusicHistory.WinUI.ViewModels;

public sealed class TrackHistoryRowViewModel : ViewModelBase
{
    private const string ApplicationName = "AppleMusicTracker";
    private bool _isHovered;

    public TrackHistoryRowViewModel(TrackHistoryRow row)
    {
        Row = row;
        OpenAlbumCommand = new RelayCommand(() => UrlLauncher.TryOpen(Row.AlbumUrl), () => CanOpenAlbum);
        OpenArtistCommand = new RelayCommand(() => UrlLauncher.TryOpen(Row.ArtistUrl), () => CanOpenArtist);
    }

    public TrackHistoryRow Row { get; }

    public long TrackId => Row.TrackId;

    public string Title => Row.Title;

    public string Artist => Row.Artist;

    public string Album => Row.Album;

    public string Subtitle => Row.Subtitle;

    public string? CatalogAudioVariantsJson => Row.CatalogAudioVariantsJson;

    public string? LastObservedAudioBadgeRaw => Row.LastObservedAudioBadgeRaw;

    public PlaybackAudioVariant? LastObservedAudioVariant => Row.LastObservedAudioVariant;

    public string LastObservedAudioVariantText => Row.LastObservedAudioVariant?.ToString() ?? string.Empty;

    public string? AudioBadgeAssetUri => AudioBadgeAssetResolver.ResolveAssetUri(Row.LastObservedAudioVariant);

    public string AudioBadgeLabel => string.IsNullOrWhiteSpace(Row.LastObservedAudioBadgeRaw)
        ? LastObservedAudioVariantText
        : Row.LastObservedAudioBadgeRaw;

    public bool HasAudioBadge => !string.IsNullOrWhiteSpace(AudioBadgeAssetUri);

    public bool HasAudioBadgeLabel => !string.IsNullOrWhiteSpace(AudioBadgeLabel);

    public string? ArtworkPathOrUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Row.ArtworkCacheRelativePath))
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    ApplicationName,
                    Row.ArtworkCacheRelativePath);
            }

            return Row.ArtworkUrl;
        }
    }

    public bool CanOpenAlbum => !string.IsNullOrWhiteSpace(Row.AlbumUrl);

    public bool CanOpenArtist => !string.IsNullOrWhiteSpace(Row.ArtistUrl);

    public bool IsHovered
    {
        get => _isHovered;
        set => SetField(ref _isHovered, value);
    }

    public RelayCommand OpenAlbumCommand { get; }

    public RelayCommand OpenArtistCommand { get; }

    public string GetFilterValue(TrackHistoryColumnId columnId)
    {
        return columnId switch
        {
            TrackHistoryColumnId.TrackId => TrackId.ToString(),
            TrackHistoryColumnId.Title => Title,
            TrackHistoryColumnId.Artist => Artist,
            TrackHistoryColumnId.Album => Album,
            TrackHistoryColumnId.Subtitle => Subtitle,
            TrackHistoryColumnId.CatalogAudioVariantsJson => CatalogAudioVariantsJson ?? string.Empty,
            TrackHistoryColumnId.LastObservedAudioBadgeRaw => LastObservedAudioBadgeRaw ?? string.Empty,
            TrackHistoryColumnId.LastObservedAudioVariant => LastObservedAudioVariantText,
            _ => string.Empty
        };
    }
}
