using System.Diagnostics;
using System.Windows;
using System.Windows.Forms;
using AppleMusicHistory.App.Services;
using AppleMusicHistory.App.ViewModels;
using AppleMusicHistory.App.Views;
using AppleMusicHistory.Host;
using Application = System.Windows.Application;

namespace AppleMusicHistory.App;

// Deprecated WPF fallback shell kept only for transition/testing while WinUI becomes primary.
public partial class App : Application
{
    private NotifyIcon? _notifyIcon;
    private StatusWindow? _statusWindow;
    private StatusViewModel? _viewModel;
    private TrackerApplicationHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = new TrackerApplicationHost(new WpfExportFilePicker());
        _host.DashboardStateChanged += OnDashboardStateChanged;
        _viewModel = new StatusViewModel();
        _viewModel.Apply(_host.CurrentState);
        _statusWindow = new StatusWindow(_viewModel, this);
        CreateTrayIcon();
        await _host.InitializeAsync().ConfigureAwait(true);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.DashboardStateChanged -= OnDashboardStateChanged;
            _host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _notifyIcon?.Dispose();
        base.OnExit(e);
    }

    internal void ShowStatusWindow()
    {
        if (_statusWindow is null)
        {
            return;
        }

        _statusWindow.Show();
        _statusWindow.WindowState = WindowState.Normal;
        _statusWindow.Activate();
    }

    internal async Task ToggleTrackingAsync(bool isPaused)
    {
        if (_host is null)
        {
            return;
        }

        await _host.SetTrackingPausedAsync(isPaused).ConfigureAwait(true);
    }

    internal void UpdateLaunchAtStartup(bool enabled)
    {
        if (_host is null)
        {
            return;
        }

        _host.UpdateLaunchAtStartupAsync(enabled).GetAwaiter().GetResult();
    }

    internal async Task ExportAsync(bool asJson, bool tracks = false)
    {
        if (_host is null)
        {
            return;
        }

        var exportKind = tracks
            ? (asJson ? ExportKind.TracksJson : ExportKind.TracksCsv)
            : (asJson ? ExportKind.SessionsJson : ExportKind.SessionsCsv);
        await _host.ExportAsync(exportKind).ConfigureAwait(true);
    }

    internal void OpenDatabaseFolder()
    {
        _host?.OpenDatabaseFolder();
    }

    private void OnDashboardStateChanged(DashboardState state)
    {
        Dispatcher.Invoke(() =>
        {
            if (_viewModel is null)
            {
                return;
            }

            _viewModel.Apply(state);
            UpdatePauseMenu();
        });
    }

    private void CreateTrayIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "AppleMusicTracker"
        };
        _notifyIcon.DoubleClick += (_, _) => ShowStatusWindow();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowStatusWindow());
        menu.Items.Add("Pause Tracking", null, async (_, _) => await ToggleTrackingAsync(!(_viewModel?.IsTrackingPaused ?? false)).ConfigureAwait(false));
        menu.Items.Add("Export CSV", null, async (_, _) => await ExportAsync(false).ConfigureAwait(false));
        menu.Items.Add("Export JSON", null, async (_, _) => await ExportAsync(true).ConfigureAwait(false));
        menu.Items.Add("Export Tracks CSV", null, async (_, _) => await ExportAsync(false, true).ConfigureAwait(false));
        menu.Items.Add("Export Tracks JSON", null, async (_, _) => await ExportAsync(true, true).ConfigureAwait(false));
        menu.Items.Add("Open Database Folder", null, (_, _) => OpenDatabaseFolder());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());
        _notifyIcon.ContextMenuStrip = menu;
        UpdatePauseMenu();
    }

    private void UpdatePauseMenu()
    {
        var menu = _notifyIcon?.ContextMenuStrip;
        if (menu is null || menu.Items.Count < 2)
        {
            return;
        }

        if (menu.Items[1] is ToolStripMenuItem item)
        {
            item.Text = _viewModel?.IsTrackingPaused == true ? "Resume Tracking" : "Pause Tracking";
        }
    }
}
