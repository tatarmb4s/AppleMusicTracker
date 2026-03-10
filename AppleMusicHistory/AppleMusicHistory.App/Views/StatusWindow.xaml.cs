using System.ComponentModel;
using System.Windows;
using AppleMusicHistory.App.ViewModels;

namespace AppleMusicHistory.App.Views;

public partial class StatusWindow : Window
{
    private readonly App _app;
    private readonly StatusViewModel _viewModel;

    public StatusWindow(StatusViewModel viewModel, App app)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _app = app;
        DataContext = viewModel;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private async void ToggleTrackingClick(object sender, RoutedEventArgs e)
    {
        await _app.ToggleTrackingAsync(!_viewModel.IsTrackingPaused);
    }

    private async void ExportCsvClick(object sender, RoutedEventArgs e)
    {
        await _app.ExportAsync(false);
    }

    private async void ExportJsonClick(object sender, RoutedEventArgs e)
    {
        await _app.ExportAsync(true);
    }

    private void OpenDatabaseFolderClick(object sender, RoutedEventArgs e)
    {
        _app.OpenDatabaseFolder();
    }

    private void LaunchAtStartupChanged(object sender, RoutedEventArgs e)
    {
        _app.UpdateLaunchAtStartup(LaunchAtStartupCheckBox.IsChecked == true);
    }
}
