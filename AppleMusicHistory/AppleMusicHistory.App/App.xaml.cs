using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using AppleMusicHistory.App.ViewModels;
using AppleMusicHistory.App.Views;
using AppleMusicHistory.Core.Models;
using AppleMusicHistory.Infrastructure.Data;
using AppleMusicHistory.Infrastructure.Export;
using AppleMusicHistory.Infrastructure.Scraping;
using AppleMusicHistory.Infrastructure.Settings;
using AppleMusicHistory.Infrastructure.Startup;
using Application = System.Windows.Application;

namespace AppleMusicHistory.App;

public partial class App : Application
{
    private NotifyIcon? _notifyIcon;
    private StatusWindow? _statusWindow;
    private StatusViewModel? _viewModel;
    private JsonTrackerSettingsStore? _settingsStore;
    private TrackerSettings? _settings;
    private WindowsStartupRegistration? _startupRegistration;
    private TrackerRuntime? _runtime;
    private HistoryExporter? _exporter;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logger = new FileLogger();
        _settingsStore = new JsonTrackerSettingsStore(AppPaths.SettingsPath);
        _settings = await _settingsStore.LoadAsync(CancellationToken.None).ConfigureAwait(true);

        var repository = new SqliteHistoryRepository(_settings.Options.DatabasePath);
        await repository.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
        await repository.RecoverOpenSessionsAsync(DateTimeOffset.UtcNow, SessionEndReason.RecoveredAfterCrash, CancellationToken.None).ConfigureAwait(true);

        var appRunId = await repository.StartAppRunAsync(
            new AppRunInfo(
                DateTimeOffset.UtcNow,
                GetVersion(),
                Environment.MachineName,
                Environment.UserName,
                Environment.Version.ToString(),
                Environment.OSVersion.ToString()),
            CancellationToken.None).ConfigureAwait(true);

        _runtime = new TrackerRuntime(
            new AppleMusicUiAutomationSnapshotSource(logger: logger),
            repository,
            new AppleMusicWebMetadataEnricher(logger: logger),
            _settingsStore,
            _settings,
            logger,
            appRunId);
        _runtime.StatusChanged += OnRuntimeStatusChanged;
        _runtime.Start();

        _exporter = new HistoryExporter(repository);
        _startupRegistration = new WindowsStartupRegistration(AppPaths.StartupShortcutPath);
        ConfigureStartupShortcut();

        _viewModel = new StatusViewModel
        {
            DatabasePath = _settings.Options.DatabasePath,
            IsTrackingPaused = _settings.TrackingPaused,
            LaunchAtStartup = _settings.Options.LaunchAtStartup,
            MetadataEnrichmentEnabled = _settings.Options.MetadataEnrichmentEnabled
        };

        _statusWindow = new StatusWindow(_viewModel, this);
        CreateTrayIcon();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _runtime?.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
        if (_runtime is null || _viewModel is null)
        {
            return;
        }

        await _runtime.SetTrackingPausedAsync(isPaused).ConfigureAwait(true);
        _viewModel.IsTrackingPaused = isPaused;
        UpdatePauseMenu();
    }

    internal void UpdateLaunchAtStartup(bool enabled)
    {
        if (_settings is null || _settingsStore is null)
        {
            return;
        }

        _settings = _settings with { Options = _settings.Options with { LaunchAtStartup = enabled } };
        _settingsStore.SaveAsync(_settings, CancellationToken.None).GetAwaiter().GetResult();
        ConfigureStartupShortcut();
    }

    internal async Task ExportAsync(bool asJson)
    {
        if (_exporter is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = asJson ? "JSON file (*.json)|*.json" : "CSV file (*.csv)|*.csv",
            FileName = $"apple-music-history-{DateTime.Now:yyyyMMdd-HHmmss}.{(asJson ? "json" : "csv")}"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (asJson)
        {
            await _exporter.ExportJsonAsync(dialog.FileName, CancellationToken.None).ConfigureAwait(true);
        }
        else
        {
            await _exporter.ExportCsvAsync(dialog.FileName, CancellationToken.None).ConfigureAwait(true);
        }
    }

    internal void OpenDatabaseFolder()
    {
        var directory = Path.GetDirectoryName(_settings?.Options.DatabasePath ?? AppPaths.DatabasePath) ?? AppPaths.AppDataDirectory;
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo
        {
            FileName = directory,
            UseShellExecute = true
        });
    }

    private void OnRuntimeStatusChanged(RuntimeStatus status)
    {
        Dispatcher.Invoke(() =>
        {
            if (_viewModel is null)
            {
                return;
            }

            _viewModel.IsTrackingPaused = status.IsTrackingPaused;
            _viewModel.AppleMusicState = status.CurrentSnapshot is null
                ? "Apple Music not detected"
                : status.CurrentSnapshot.IsPaused ? "Apple Music paused" : "Apple Music playing";
            _viewModel.CurrentTrack = status.CurrentSnapshot is null
                ? "No current track"
                : $"{status.CurrentSnapshot.Title} | {status.CurrentSnapshot.Artist} | {status.CurrentSnapshot.Album}";
            _viewModel.ActiveSession = status.ActiveSession is null
                ? "No active session"
                : $"Session #{status.ActiveSession.SessionId} | Replay {status.ActiveSession.ReplayIndex} | Last pos {status.ActiveSession.LastPositionSeconds}s";
            _viewModel.Statistics = $"Tracks: {status.Statistics.TrackCount} | Sessions: {status.Statistics.SessionCount} | Open: {status.Statistics.OpenSessionCount}";
            _viewModel.LastObserved = status.Statistics.LastObservedUtc?.ToLocalTime().ToString("G") ?? "Never";
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

    private void ConfigureStartupShortcut()
    {
        if (_startupRegistration is null || _settings is null)
        {
            return;
        }

        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            _startupRegistration.SetEnabled(_settings.Options.LaunchAtStartup, exePath);
        }
    }

    private static string GetVersion()
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return "dev";
        }

        return FileVersionInfo.GetVersionInfo(exePath).FileVersion ?? "dev";
    }
}
