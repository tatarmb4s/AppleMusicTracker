using System;
using System.Numerics;
using AppleMusicHistory.Host;
using AppleMusicHistory.WinUI.Helpers;
using AppleMusicHistory.WinUI.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace AppleMusicHistory.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly AppWindow _appWindow;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainWindowViewModel();
        RootGrid.DataContext = ViewModel;

        WindowingHelper.ConfigureWindow(this, "AppleMusicTracker");
        _appWindow = WindowingHelper.GetAppWindow(this);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        _appWindow.Closing += OnAppWindowClosing;
        _appWindow.Changed += OnAppWindowChanged;
        Activated += OnActivated;
    }

    public MainWindowViewModel ViewModel { get; }

    public void Initialize(TrackerApplicationHost host)
    {
        ViewModel.AttachHost(host, DispatcherQueue);
    }

    public void ShowWindow()
    {
        WindowingHelper.Show(this);
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        StartAmbientAnimations();
        Activated -= OnActivated;
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        WindowingHelper.Hide(this);
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPresenterChange)
        {
            return;
        }

        if (sender.Presenter is OverlappedPresenter presenter &&
            presenter.State == OverlappedPresenterState.Minimized)
        {
            WindowingHelper.Hide(this);
        }
    }

    private void StartAmbientAnimations()
    {
        StartAmbientAnimation(GlowOrbOne, new Vector3(0, 0, 0), new Vector3(-42, 38, 0), 5);
        StartAmbientAnimation(GlowOrbTwo, new Vector3(0, 0, 0), new Vector3(35, -28, 0), -6);
        StartAmbientAnimation(BackdropArtworkOne, new Vector3(0, 0, 0), new Vector3(-30, 24, 0), 8);
        StartAmbientAnimation(BackdropArtworkTwo, new Vector3(0, 0, 0), new Vector3(28, -32, 0), -10);
    }

    private static void StartAmbientAnimation(UIElement element, Vector3 fromOffset, Vector3 toOffset, float rotationDegrees)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;

        visual.CenterPoint = new Vector3((float)element.RenderSize.Width / 2, (float)element.RenderSize.Height / 2, 0);

        var offsetAnimation = compositor.CreateVector3KeyFrameAnimation();
        offsetAnimation.InsertKeyFrame(0f, fromOffset);
        offsetAnimation.InsertKeyFrame(0.5f, toOffset);
        offsetAnimation.InsertKeyFrame(1f, fromOffset);
        offsetAnimation.Duration = TimeSpan.FromSeconds(26);
        offsetAnimation.IterationBehavior = AnimationIterationBehavior.Forever;

        var rotationAnimation = compositor.CreateScalarKeyFrameAnimation();
        rotationAnimation.InsertKeyFrame(0f, 0);
        rotationAnimation.InsertKeyFrame(0.5f, rotationDegrees);
        rotationAnimation.InsertKeyFrame(1f, 0);
        rotationAnimation.Duration = TimeSpan.FromSeconds(24);
        rotationAnimation.IterationBehavior = AnimationIterationBehavior.Forever;

        var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
        scaleAnimation.InsertKeyFrame(0f, new Vector3(1f, 1f, 1f));
        scaleAnimation.InsertKeyFrame(0.5f, new Vector3(1.06f, 1.06f, 1f));
        scaleAnimation.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f));
        scaleAnimation.Duration = TimeSpan.FromSeconds(22);
        scaleAnimation.IterationBehavior = AnimationIterationBehavior.Forever;

        visual.StartAnimation(nameof(visual.Offset), offsetAnimation);
        visual.StartAnimation(nameof(visual.RotationAngleInDegrees), rotationAnimation);
        visual.StartAnimation(nameof(visual.Scale), scaleAnimation);
    }

    private void OpenSongClick(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenSongUrl();
    }

    private void OpenAlbumClick(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenAlbumUrl();
    }

    private void OpenArtistClick(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenArtistUrl();
    }

    private async void ExportSplitButtonClick(SplitButton sender, SplitButtonClickEventArgs args)
    {
        await ViewModel.ExportAsync(ExportKind.SessionsCsv);
    }

    private async void ExportSessionsCsvClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.ExportAsync(ExportKind.SessionsCsv);
    }

    private async void ExportSessionsJsonClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.ExportAsync(ExportKind.SessionsJson);
    }

    private async void ExportTracksCsvClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.ExportAsync(ExportKind.TracksCsv);
    }

    private async void ExportTracksJsonClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.ExportAsync(ExportKind.TracksJson);
    }

    private async void LaunchAtStartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        await ViewModel.UpdateLaunchAtStartupAsync(LaunchAtStartupToggle.IsOn);
    }
}
