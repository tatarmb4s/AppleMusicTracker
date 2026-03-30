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
        RootGrid.Loaded += OnRootGridLoaded;
        RootGrid.SizeChanged += OnRootGridSizeChanged;
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

    private void OnRootGridLoaded(object sender, RoutedEventArgs e)
    {
        UpdateResponsiveStates(Math.Max(RootGrid.ActualWidth, Bounds.Width));
    }

    private void OnRootGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveStates(e.NewSize.Width);
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

    private void UpdateResponsiveStates(double windowWidth)
    {
        var controlState = windowWidth >= 760
            ? ControlLayoutState.Wide
            : windowWidth >= 520
                ? ControlLayoutState.Compact
                : ControlLayoutState.Narrow;

        ConfigureShellLayout(windowWidth >= 1180);
        ApplyDensity(windowWidth >= 1180, controlState);
        ConfigureControlStrip(controlState);
    }

    private void ConfigureShellLayout(bool isWide)
    {
        ShellLayoutGrid.ColumnDefinitions[1].Width = isWide ? new GridLength(420) : new GridLength(0);
        ShellLayoutGrid.RowDefinitions[1].Height = isWide ? new GridLength(0) : GridLength.Auto;

        Grid.SetRow(RightRail, isWide ? 0 : 1);
        Grid.SetColumn(RightRail, isWide ? 1 : 0);
    }

    private void ConfigureControlStrip(ControlLayoutState state)
    {
        ControlStripGrid.ColumnDefinitions[0].Width = GridLength.Auto;
        ControlStripGrid.ColumnDefinitions[1].Width = GridLength.Auto;
        ControlStripGrid.ColumnDefinitions[2].Width = GridLength.Auto;
        ControlStripGrid.ColumnDefinitions[3].Width = GridLength.Auto;
        ControlStripGrid.ColumnDefinitions[4].Width = GridLength.Auto;

        ControlStripGrid.RowDefinitions[0].Height = GridLength.Auto;
        ControlStripGrid.RowDefinitions[1].Height = new GridLength(0);
        ControlStripGrid.RowDefinitions[2].Height = new GridLength(0);

        PauseResumeButton.MinWidth = 220;
        PauseResumeButton.HorizontalAlignment = HorizontalAlignment.Center;
        ControlStripGrid.HorizontalAlignment = HorizontalAlignment.Center;

        switch (state)
        {
            case ControlLayoutState.Wide:
                SetControlPlacement(OpenFolderButton, row: 0, column: 0);
                SetControlPlacement(OpenSongButton, row: 0, column: 1);
                SetControlPlacement(PauseResumeButton, row: 0, column: 2);
                SetControlPlacement(OpenAlbumButton, row: 0, column: 3);
                SetControlPlacement(ExportButton, row: 0, column: 4);
                break;

            case ControlLayoutState.Compact:
                ControlStripGrid.ColumnDefinitions[4].Width = new GridLength(0);
                ControlStripGrid.RowDefinitions[1].Height = GridLength.Auto;

                SetControlPlacement(OpenFolderButton, row: 0, column: 0);
                SetControlPlacement(OpenSongButton, row: 0, column: 1);
                SetControlPlacement(OpenAlbumButton, row: 0, column: 2);
                SetControlPlacement(ExportButton, row: 0, column: 3);
                SetControlPlacement(PauseResumeButton, row: 1, column: 0, columnSpan: 4);
                break;

            case ControlLayoutState.Narrow:
                ControlStripGrid.ColumnDefinitions[2].Width = new GridLength(0);
                ControlStripGrid.ColumnDefinitions[3].Width = new GridLength(0);
                ControlStripGrid.ColumnDefinitions[4].Width = new GridLength(0);
                ControlStripGrid.RowDefinitions[1].Height = GridLength.Auto;
                ControlStripGrid.RowDefinitions[2].Height = GridLength.Auto;
                PauseResumeButton.MinWidth = 180;

                SetControlPlacement(OpenFolderButton, row: 0, column: 0);
                SetControlPlacement(OpenSongButton, row: 0, column: 1);
                SetControlPlacement(PauseResumeButton, row: 1, column: 0, columnSpan: 2);
                SetControlPlacement(OpenAlbumButton, row: 2, column: 0);
                SetControlPlacement(ExportButton, row: 2, column: 1);
                break;
        }
    }

    private void ApplyDensity(bool isWideShell, ControlLayoutState controlState)
    {
        if (controlState == ControlLayoutState.Narrow)
        {
            ResponsiveRoot.Margin = new Thickness(10);
            ShellLayoutGrid.ColumnSpacing = 10;
            ShellLayoutGrid.RowSpacing = 10;
            HeroCard.Padding = new Thickness(12);
            StatusCard.Padding = new Thickness(12);
            MetadataCard.Padding = new Thickness(12);
            StatisticsCard.Padding = new Thickness(12);
            ToolsCard.Padding = new Thickness(12);
            HeroContentGrid.RowSpacing = 10;
            ArtworkFrame.MaxWidth = 420;
            TrackTitleText.FontSize = 20;
            TrackArtistAlbumText.FontSize = 14;
            StatusHeaderText.FontSize = 18;
            MetadataHeaderText.FontSize = 18;
            StatisticsHeaderText.FontSize = 18;
            ToolsHeaderText.FontSize = 18;
            ControlStripGrid.ColumnSpacing = 8;
            ControlStripGrid.RowSpacing = 8;
            return;
        }

        ResponsiveRoot.Margin = new Thickness(isWideShell ? 20 : 14);
        ShellLayoutGrid.ColumnSpacing = isWideShell ? 18 : 12;
        ShellLayoutGrid.RowSpacing = isWideShell ? 0 : 12;
        HeroCard.Padding = new Thickness(isWideShell ? 24 : 16);
        StatusCard.Padding = new Thickness(isWideShell ? 18 : 14);
        MetadataCard.Padding = new Thickness(isWideShell ? 18 : 14);
        StatisticsCard.Padding = new Thickness(isWideShell ? 18 : 14);
        ToolsCard.Padding = new Thickness(isWideShell ? 18 : 14);
        HeroContentGrid.RowSpacing = isWideShell ? 14 : 12;
        ArtworkFrame.MaxWidth = isWideShell ? 620 : 560;
        TrackTitleText.FontSize = isWideShell ? 25 : 22;
        TrackArtistAlbumText.FontSize = isWideShell ? 18 : 16;
        StatusHeaderText.FontSize = isWideShell ? 22 : 20;
        MetadataHeaderText.FontSize = isWideShell ? 22 : 20;
        StatisticsHeaderText.FontSize = isWideShell ? 22 : 20;
        ToolsHeaderText.FontSize = isWideShell ? 22 : 20;
        ControlStripGrid.ColumnSpacing = controlState == ControlLayoutState.Wide ? 10 : 8;
        ControlStripGrid.RowSpacing = controlState == ControlLayoutState.Wide ? 0 : 10;
    }

    private static void SetControlPlacement(FrameworkElement element, int row, int column, int columnSpan = 1)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, columnSpan);
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

    private enum ControlLayoutState
    {
        Wide,
        Compact,
        Narrow
    }
}
