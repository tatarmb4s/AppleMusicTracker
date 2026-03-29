using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using AppleMusicHistory.Host;
using AppleMusicHistory.WinUI.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Application = Microsoft.UI.Xaml.Application;

namespace AppleMusicHistory.WinUI;

public partial class App : Application
{
    private NotifyIcon? _notifyIcon;
    private MainWindow? _window;
    private TrackerApplicationHost? _host;
    private DispatcherQueue? _dispatcherQueue;
    private bool _isExitRequested;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        _window = new MainWindow();
        _host = new TrackerApplicationHost(new WinUiExportFilePicker(_window));
        _window.Initialize(_host);
        _host.DashboardStateChanged += OnDashboardStateChanged;

        CreateTrayIcon();
        UpdatePauseMenu(_host.CurrentState.IsTrackingPaused);

        await _host.InitializeAsync();
    }

    private void CreateTrayIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "AppleMusicTracker"
        };

        _notifyIcon.DoubleClick += async (_, _) => await RunOnUiThreadAsync(() =>
        {
            _window?.ShowWindow();
            return Task.CompletedTask;
        });

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Dashboard", null, async (_, _) => await RunOnUiThreadAsync(() =>
        {
            _window?.ShowWindow();
            return Task.CompletedTask;
        }));
        menu.Items.Add("Pause Tracking", null, async (_, _) => await RunOnUiThreadAsync(async () =>
        {
            if (_host is not null)
            {
                await _host.SetTrackingPausedAsync(!_host.CurrentState.IsTrackingPaused);
            }
        }));
        menu.Items.Add("Export Sessions CSV", null, async (_, _) => await RunOnUiThreadAsync(() => _host?.ExportAsync(ExportKind.SessionsCsv) ?? Task.CompletedTask));
        menu.Items.Add("Export Sessions JSON", null, async (_, _) => await RunOnUiThreadAsync(() => _host?.ExportAsync(ExportKind.SessionsJson) ?? Task.CompletedTask));
        menu.Items.Add("Export Tracks CSV", null, async (_, _) => await RunOnUiThreadAsync(() => _host?.ExportAsync(ExportKind.TracksCsv) ?? Task.CompletedTask));
        menu.Items.Add("Export Tracks JSON", null, async (_, _) => await RunOnUiThreadAsync(() => _host?.ExportAsync(ExportKind.TracksJson) ?? Task.CompletedTask));
        menu.Items.Add("Open Database Folder", null, async (_, _) => await RunOnUiThreadAsync(() =>
        {
            _host?.OpenDatabaseFolder();
            return Task.CompletedTask;
        }));
        menu.Items.Add("Exit", null, async (_, _) => await ExitApplicationAsync());
        _notifyIcon.ContextMenuStrip = menu;
    }

    private void OnDashboardStateChanged(DashboardState state)
    {
        UpdatePauseMenu(state.IsTrackingPaused);
    }

    private void UpdatePauseMenu(bool isPaused)
    {
        var menu = _notifyIcon?.ContextMenuStrip;
        if (menu is null || menu.Items.Count < 2)
        {
            return;
        }

        if (menu.Items[1] is ToolStripMenuItem item)
        {
            item.Text = isPaused ? "Resume Tracking" : "Pause Tracking";
        }
    }

    private async Task RunOnUiThreadAsync(Func<Task> action)
    {
        if (_dispatcherQueue is null)
        {
            await action();
            return;
        }

        var completionSource = new TaskCompletionSource();
        _dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await action();
                completionSource.SetResult();
            }
            catch (Exception ex)
            {
                completionSource.SetException(ex);
            }
        });

        await completionSource.Task;
    }

    private async Task ExitApplicationAsync()
    {
        if (_isExitRequested)
        {
            return;
        }

        _isExitRequested = true;
        _window?.AllowClose();
        if (_host is not null)
        {
            _host.DashboardStateChanged -= OnDashboardStateChanged;
            await _host.DisposeAsync();
        }

        _notifyIcon?.Dispose();
        _notifyIcon = null;

        await RunOnUiThreadAsync(() =>
        {
            _window?.Close();
            Exit();
            return Task.CompletedTask;
        });
    }
}
