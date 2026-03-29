using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AppleMusicHistory.Host;
using AppleMusicHistory.WinUI.Commands;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace AppleMusicHistory.WinUI.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private TrackerApplicationHost? _host;
    private DispatcherQueue? _dispatcherQueue;
    private DashboardState _currentState = DashboardState.CreateDefault(string.Empty, true, true, false);
    private ImageSource? _artworkImage;
    private Brush _backdropBrush = CreateBackdropBrush("apple-music-tracker");
    private Brush _accentGlowBrush = CreateGlowBrush(Colors.CadetBlue, 0.75);
    private Brush _secondaryGlowBrush = CreateGlowBrush(Color.FromArgb(255, 153, 101, 57), 0.55);
    private SolidColorBrush _cardBrush = new(Color.FromArgb(72, 255, 255, 255));
    private SolidColorBrush _heroCardBrush = new(Color.FromArgb(78, 255, 255, 255));
    private SolidColorBrush _cardBorderBrush = new(Color.FromArgb(96, 255, 255, 255));
    private SolidColorBrush _accentBrush = new(Color.FromArgb(255, 214, 230, 255));

    public MainWindowViewModel()
    {
        PauseResumeCommand = new AsyncRelayCommand(ToggleTrackingAsync, () => _host is not null);
        OpenDatabaseFolderCommand = new RelayCommand(() => _host?.OpenDatabaseFolder(), () => _host is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DashboardState CurrentState
    {
        get => _currentState;
        private set => SetField(ref _currentState, value);
    }

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

    public SolidColorBrush CardBrush
    {
        get => _cardBrush;
        private set => SetField(ref _cardBrush, value);
    }

    public SolidColorBrush HeroCardBrush
    {
        get => _heroCardBrush;
        private set => SetField(ref _heroCardBrush, value);
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

    public AsyncRelayCommand PauseResumeCommand { get; }

    public RelayCommand OpenDatabaseFolderCommand { get; }

    public bool HasArtwork => ArtworkImage is not null;

    public bool HasSongUrl => !string.IsNullOrWhiteSpace(CurrentState.CurrentSongUrl);

    public bool HasAlbumUrl => !string.IsNullOrWhiteSpace(CurrentState.CurrentAlbumUrl);

    public bool HasArtistUrl => !string.IsNullOrWhiteSpace(CurrentState.CurrentArtistUrl);

    public bool HasDiagnosticMessage => !string.IsNullOrWhiteSpace(CurrentState.SourceDiagnosticMessage);

    public string PauseResumeLabel => CurrentState.IsTrackingPaused ? "Resume Tracking" : "Pause Tracking";

    public string MetadataEnrichmentLabel => CurrentState.MetadataEnrichmentEnabled ? "Enabled" : "Disabled";

    public void AttachHost(TrackerApplicationHost host, DispatcherQueue dispatcherQueue)
    {
        if (_host is not null)
        {
            _host.DashboardStateChanged -= OnDashboardStateChanged;
        }

        _host = host;
        _dispatcherQueue = dispatcherQueue;
        _host.DashboardStateChanged += OnDashboardStateChanged;
        ApplyState(host.CurrentState);
        PauseResumeCommand.NotifyCanExecuteChanged();
        OpenDatabaseFolderCommand.NotifyCanExecuteChanged();
    }

    public async Task ExportAsync(ExportKind exportKind)
    {
        if (_host is null)
        {
            return;
        }

        await _host.ExportAsync(exportKind);
    }

    public async Task UpdateLaunchAtStartupAsync(bool enabled)
    {
        if (_host is null)
        {
            return;
        }

        await _host.UpdateLaunchAtStartupAsync(enabled);
    }

    public void OpenSongUrl() => OpenUrl(CurrentState.CurrentSongUrl);

    public void OpenAlbumUrl() => OpenUrl(CurrentState.CurrentAlbumUrl);

    public void OpenArtistUrl() => OpenUrl(CurrentState.CurrentArtistUrl);

    private void OnDashboardStateChanged(DashboardState state)
    {
        if (_dispatcherQueue is null)
        {
            ApplyState(state);
            return;
        }

        _dispatcherQueue.TryEnqueue(() => ApplyState(state));
    }

    private async Task ToggleTrackingAsync()
    {
        if (_host is null)
        {
            return;
        }

        await _host.SetTrackingPausedAsync(!CurrentState.IsTrackingPaused);
    }

    private void ApplyState(DashboardState state)
    {
        CurrentState = state;
        ArtworkImage = CreateArtworkImage(state.CurrentArtworkPathOrUrl);

        var palette = CreatePalette(state.PaletteSeed);
        BackdropBrush = CreateBackdropBrush(palette.Primary, palette.Secondary);
        AccentGlowBrush = CreateGlowBrush(palette.Accent, 0.78);
        SecondaryGlowBrush = CreateGlowBrush(palette.SecondaryGlow, 0.64);
        CardBrush = new SolidColorBrush(Color.FromArgb(72, 255, 255, 255));
        HeroCardBrush = new SolidColorBrush(Color.FromArgb(86, 255, 255, 255));
        CardBorderBrush = new SolidColorBrush(Color.FromArgb(98, 255, 255, 255));
        AccentBrush = new SolidColorBrush(palette.Accent);

        NotifyDerivedStateChanged();
    }

    private void NotifyDerivedStateChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasArtwork)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSongUrl)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasAlbumUrl)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasArtistUrl)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDiagnosticMessage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PauseResumeLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MetadataEnrichmentLabel)));
        PauseResumeCommand.NotifyCanExecuteChanged();
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

    private static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private static Palette CreatePalette(string seed)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(seed) ? "apple-music-tracker" : seed));
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

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed record Palette(Color Primary, Color Secondary, Color Accent, Color SecondaryGlow);
}
