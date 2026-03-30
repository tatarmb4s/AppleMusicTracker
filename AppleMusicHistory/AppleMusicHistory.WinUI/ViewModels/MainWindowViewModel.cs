using System;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AppleMusicHistory.Host;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace AppleMusicHistory.WinUI.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private const string DefaultPaletteSeed = "apple-music-tracker";

    private TrackerApplicationHost? _host;
    private DispatcherQueue? _dispatcherQueue;
    private ImageSource? _artworkImage;
    private Brush _backdropBrush = CreateBackdropBrush(DefaultPaletteSeed);
    private Brush _accentGlowBrush = CreateGlowBrush(Colors.CadetBlue, 0.75);
    private Brush _secondaryGlowBrush = CreateGlowBrush(Color.FromArgb(255, 153, 101, 57), 0.55);
    private SolidColorBrush _cardBorderBrush = new(Color.FromArgb(96, 255, 255, 255));
    private SolidColorBrush _accentBrush = new(Color.FromArgb(255, 214, 230, 255));
    private string _lastArtworkSource = string.Empty;
    private string _lastPaletteSeed = DefaultPaletteSeed;

    public MainWindowViewModel()
    {
        Tabs = new MainTabControllerViewModel();
        NowPlaying = new NowPlayingTabViewModel();
        TrackHistory = new TrackHistoryTabViewModel();
        Tabs.PropertyChanged += OnTabsPropertyChanged;
    }

    public MainTabControllerViewModel Tabs { get; }

    public NowPlayingTabViewModel NowPlaying { get; }

    public TrackHistoryTabViewModel TrackHistory { get; }

    public ImageSource? ArtworkImage
    {
        get => _artworkImage;
        private set => SetField(ref _artworkImage, value);
    }

    public Brush BackdropBrush
    {
        get => _backdropBrush;
        private set => SetField(ref _backdropBrush, value);
    }

    public Brush AccentGlowBrush
    {
        get => _accentGlowBrush;
        private set => SetField(ref _accentGlowBrush, value);
    }

    public Brush SecondaryGlowBrush
    {
        get => _secondaryGlowBrush;
        private set => SetField(ref _secondaryGlowBrush, value);
    }

    public SolidColorBrush CardBorderBrush
    {
        get => _cardBorderBrush;
        private set => SetField(ref _cardBorderBrush, value);
    }

    public SolidColorBrush AccentBrush
    {
        get => _accentBrush;
        private set => SetField(ref _accentBrush, value);
    }

    public bool HasArtwork => ArtworkImage is not null;

    public bool IsNowPlayingTabSelected => Tabs.IsNowPlayingSelected;

    public bool IsTrackHistoryTabSelected => Tabs.IsTrackHistorySelected;

    public void AttachHost(TrackerApplicationHost host, DispatcherQueue dispatcherQueue)
    {
        if (_host is not null)
        {
            _host.DashboardStateChanged -= OnDashboardStateChanged;
        }

        _host = host;
        _dispatcherQueue = dispatcherQueue;
        _host.DashboardStateChanged += OnDashboardStateChanged;

        NowPlaying.AttachHost(host);
        TrackHistory.AttachHost(host, dispatcherQueue);
        ApplyDashboardState(host.CurrentState);
        _ = TrackHistory.SetIsActiveAsync(Tabs.IsTrackHistorySelected);
    }

    public Task ExportAsync(ExportKind exportKind) => NowPlaying.ExportAsync(exportKind);

    public Task UpdateLaunchAtStartupAsync(bool enabled) => NowPlaying.UpdateLaunchAtStartupAsync(enabled);

    public void OpenSongUrl() => NowPlaying.OpenSongUrl();

    public void OpenAlbumUrl() => NowPlaying.OpenAlbumUrl();

    public void OpenArtistUrl() => NowPlaying.OpenArtistUrl();

    private void OnTabsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainTabControllerViewModel.SelectedTabId))
        {
            return;
        }

        OnPropertyChanged(nameof(IsNowPlayingTabSelected));
        OnPropertyChanged(nameof(IsTrackHistoryTabSelected));
        _ = TrackHistory.SetIsActiveAsync(Tabs.IsTrackHistorySelected);
    }

    private void OnDashboardStateChanged(AppleMusicHistory.Host.DashboardState state)
    {
        if (_dispatcherQueue is null)
        {
            ApplyDashboardState(state);
            return;
        }

        _dispatcherQueue.TryEnqueue(() => ApplyDashboardState(state));
    }

    private void ApplyDashboardState(AppleMusicHistory.Host.DashboardState state)
    {
        NowPlaying.ApplyState(state);
        TrackHistory.NotifyDashboardStateChanged();

        var artworkSource = state.CurrentArtworkPathOrUrl ?? string.Empty;
        if (!string.Equals(_lastArtworkSource, artworkSource, StringComparison.Ordinal))
        {
            _lastArtworkSource = artworkSource;
            ArtworkImage = CreateArtworkImage(artworkSource);
            OnPropertyChanged(nameof(HasArtwork));
        }

        var paletteSeed = string.IsNullOrWhiteSpace(state.PaletteSeed) ? DefaultPaletteSeed : state.PaletteSeed;
        if (!string.Equals(_lastPaletteSeed, paletteSeed, StringComparison.Ordinal))
        {
            _lastPaletteSeed = paletteSeed;
            var palette = CreatePalette(paletteSeed);
            BackdropBrush = CreateBackdropBrush(palette.Primary, palette.Secondary);
            AccentGlowBrush = CreateGlowBrush(palette.Accent, 0.78);
            SecondaryGlowBrush = CreateGlowBrush(palette.SecondaryGlow, 0.64);
            CardBorderBrush = new SolidColorBrush(Color.FromArgb(98, 255, 255, 255));
            AccentBrush = new SolidColorBrush(palette.Accent);
        }
    }

    private static ImageSource? CreateArtworkImage(string pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
        {
            return null;
        }

        try
        {
            if (Path.IsPathRooted(pathOrUrl) && !File.Exists(pathOrUrl))
            {
                return null;
            }

            if (!Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri))
            {
                uri = new Uri(pathOrUrl, UriKind.RelativeOrAbsolute);
            }

            return new BitmapImage(uri);
        }
        catch
        {
            return null;
        }
    }

    private static Palette CreatePalette(string seed)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(seed) ? DefaultPaletteSeed : seed));
        var warm = bytes[0] % 2 == 0;
        var primaryHue = warm ? 18 + bytes[1] % 18 : 188 + bytes[1] % 18;
        var secondaryHue = warm ? primaryHue + 16 : primaryHue - 18;
        var accentHue = warm ? primaryHue + 10 : primaryHue + 6;

        return new Palette(
            FromHsl(primaryHue, 0.62, 0.34),
            FromHsl(secondaryHue, 0.58, 0.18),
            FromHsl(accentHue, 0.82, 0.78),
            FromHsl(primaryHue, 0.46, 0.26));
    }

    private static Brush CreateBackdropBrush(string seed)
    {
        var palette = CreatePalette(seed);
        return CreateBackdropBrush(palette.Primary, palette.Secondary);
    }

    private static Brush CreateBackdropBrush(Color primary, Color secondary)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop { Color = primary, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = secondary, Offset = 1 });
        return brush;
    }

    private static Brush CreateGlowBrush(Color color, double opacity)
    {
        var brush = new RadialGradientBrush
        {
            Center = new Windows.Foundation.Point(0.5, 0.5),
            GradientOrigin = new Windows.Foundation.Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B), Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, color.R, color.G, color.B), Offset = 1 });
        return brush;
    }

    private static Color FromHsl(double hue, double saturation, double lightness)
    {
        hue %= 360;
        var chroma = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
        var x = chroma * (1 - Math.Abs(((hue / 60) % 2) - 1));
        var m = lightness - (chroma / 2);

        (double r, double g, double b) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };

        return Color.FromArgb(
            255,
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private sealed record Palette(Color Primary, Color Secondary, Color Accent, Color SecondaryGlow);
}
