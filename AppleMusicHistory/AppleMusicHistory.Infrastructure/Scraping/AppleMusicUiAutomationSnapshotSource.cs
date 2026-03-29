using System.Diagnostics;
using System.Text.RegularExpressions;
using AppleMusicHistory.Core.Abstractions;
using AppleMusicHistory.Core.Models;
using AppleMusicHistory.Infrastructure.Data;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace AppleMusicHistory.Infrastructure.Scraping;

public sealed class AppleMusicUiAutomationSnapshotSource : IAppleMusicSnapshotSource
{
    private readonly FileLogger? _logger;
    private readonly IAppleMusicUiProbe _probe;
    private readonly TimeSpan _readTimeout;
    private double? _previousSongProgress;

    public AppleMusicUiAutomationSnapshotSource(bool composerAsArtist = true, FileLogger? logger = null)
        : this(new FlaUiAppleMusicUiProbe(composerAsArtist), TimeSpan.FromSeconds(5), logger)
    {
    }

    internal AppleMusicUiAutomationSnapshotSource(IAppleMusicUiProbe probe, TimeSpan readTimeout, FileLogger? logger = null)
    {
        _probe = probe;
        _readTimeout = readTimeout;
        _logger = logger;
    }

    public async Task<AppleMusicSnapshotReadResult> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var process = _probe.FindProcess();
        if (process is null)
        {
            ResetProgress();
            return AppleMusicSnapshotReadResult.AppNotRunning();
        }

        try
        {
            var readOutcome = await Task.Run(
                    () => _probe.ReadPlayback(process.ProcessId, cancellationToken),
                    cancellationToken)
                .WaitAsync(_readTimeout, cancellationToken)
                .ConfigureAwait(false);

            return await MapOutcomeAsync(readOutcome, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException ex)
        {
            ResetProgress();
            await LogFailureAsync($"Apple Music scrape timed out: {ex.Message}", cancellationToken).ConfigureAwait(false);
            return AppleMusicSnapshotReadResult.Recovering("Timed out while reading Apple Music UI.");
        }
        catch (Exception ex)
        {
            return await ClassifyFailureAsync(process.ProcessId, ex, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static PlaybackAudioVariant? ParseAudioBadge(string? badge)
        => PlaybackAudioVariantParser.ParseBadge(badge);

    private async Task<AppleMusicSnapshotReadResult> MapOutcomeAsync(
        AppleMusicProbeReadOutcome outcome,
        CancellationToken cancellationToken)
    {
        switch (outcome.State)
        {
            case AppleMusicProbeReadState.Available:
                var snapshot = outcome.SnapshotData
                    ?? throw new InvalidOperationException("Probe returned Available without snapshot data.");
                return AppleMusicSnapshotReadResult.Available(CreateSnapshot(snapshot), outcome.DiagnosticMessage);

            case AppleMusicProbeReadState.AppNotRunning:
                ResetProgress();
                return AppleMusicSnapshotReadResult.AppNotRunning(outcome.DiagnosticMessage);

            case AppleMusicProbeReadState.NoTrackDetected:
                ResetProgress();
                return AppleMusicSnapshotReadResult.NoTrackDetected(outcome.DiagnosticMessage);

            case AppleMusicProbeReadState.Recovering:
                ResetProgress();
                await LogFailureAsync(
                    $"Apple Music scrape recovering: {outcome.DiagnosticMessage ?? "Unknown UI Automation failure."}",
                    cancellationToken).ConfigureAwait(false);
                return AppleMusicSnapshotReadResult.Recovering(outcome.DiagnosticMessage);

            default:
                throw new ArgumentOutOfRangeException(nameof(outcome.State), outcome.State, null);
        }
    }

    private async Task<AppleMusicSnapshotReadResult> ClassifyFailureAsync(
        int processId,
        Exception ex,
        CancellationToken cancellationToken)
    {
        ResetProgress();

        var isRunning = _probe.IsProcessRunning(processId);
        var result = isRunning
            ? AppleMusicSnapshotReadResult.Recovering(ex.Message)
            : AppleMusicSnapshotReadResult.AppNotRunning(ex.Message);

        await LogFailureAsync(
            $"Apple Music scrape failed ({result.State}): {ex.Message}",
            cancellationToken).ConfigureAwait(false);

        return result;
    }

    private PlaybackSnapshot CreateSnapshot(AppleMusicProbeSnapshotData snapshot)
    {
        var isPaused = DeterminePauseState(snapshot.PlayPauseButtonName, snapshot.SongProgressPercent);
        return PlaybackSnapshot.Create(
            snapshot.Title,
            snapshot.Artist,
            snapshot.Album,
            snapshot.Subtitle,
            snapshot.ObservedAtUtc,
            isPaused,
            snapshot.CurrentPositionSeconds,
            snapshot.DurationSeconds,
            snapshot.ObservedAudioBadgeRaw,
            snapshot.SourceDescription);
    }

    private bool DeterminePauseState(string? buttonName, double songProgressPercent)
    {
        if (buttonName is "Play" or "Pause")
        {
            return buttonName == "Play";
        }

        var isPaused = _previousSongProgress.HasValue && Math.Abs(songProgressPercent - _previousSongProgress.Value) < 0.0001;
        _previousSongProgress = songProgressPercent;
        return isPaused;
    }

    private void ResetProgress() => _previousSongProgress = null;

    private Task LogFailureAsync(string message, CancellationToken cancellationToken)
        => _logger?.ErrorAsync(message, cancellationToken) ?? Task.CompletedTask;

    private static int? ParseTimeString(string? time)
    {
        if (string.IsNullOrWhiteSpace(time) || !Regex.IsMatch(time, @"^-?\d{1,3}:\d{2}$"))
        {
            return null;
        }

        var cleanTime = time.TrimStart('-');
        var parts = cleanTime.Split(':');
        if (parts.Length != 2)
        {
            return null;
        }

        return int.TryParse(parts[0], out var minutes) && int.TryParse(parts[1], out var seconds)
            ? minutes * 60 + seconds
            : null;
    }

    private static string? DeduplicatedString(string value)
    {
        if (value.Length < 4)
        {
            return null;
        }

        var firstHalf = value[..((value.Length + 1) / 2 - 1)];
        var secondHalf = value[((value.Length + 1) / 2)..];
        if (firstHalf == secondHalf)
        {
            return DeduplicatedString(firstHalf) ?? firstHalf;
        }

        return null;
    }

    private sealed class FlaUiAppleMusicUiProbe : IAppleMusicUiProbe
    {
        private static readonly Regex ComposerPerformerRegex = new(@"By\s.*?\s\u2014", RegexOptions.Compiled);
        private readonly bool _composerAsArtist;

        public FlaUiAppleMusicUiProbe(bool composerAsArtist)
        {
            _composerAsArtist = composerAsArtist;
        }

        public AppleMusicProcessInfo? FindProcess()
        {
            var process = Process.GetProcessesByName("AppleMusic")
                .OrderByDescending(p => p.MainWindowHandle != IntPtr.Zero)
                .FirstOrDefault();

            return process is null ? null : new AppleMusicProcessInfo(process.Id);
        }

        public AppleMusicProbeReadOutcome ReadPlayback(int processId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var automation = new UIA3Automation();
            var windows = automation.GetDesktop().FindAllChildren(c => c.ByProcessId(processId)).ToList();
            if (windows.Count == 0)
            {
                if (!IsProcessRunning(processId))
                {
                    return new AppleMusicProbeReadOutcome(
                        AppleMusicProbeReadState.AppNotRunning,
                        DiagnosticMessage: $"Process with an Id of {processId} is not running.");
                }

                AutomationElement? mainWindow;
                try
                {
                    mainWindow = FlaUI.Core.Application.Attach(processId).GetMainWindow(automation, TimeSpan.FromSeconds(3));
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    return IsProcessRunning(processId)
                        ? new AppleMusicProbeReadOutcome(
                            AppleMusicProbeReadState.Recovering,
                            DiagnosticMessage: $"Apple Music main window lookup failed: {ex.Message}")
                        : new AppleMusicProbeReadOutcome(
                            AppleMusicProbeReadState.AppNotRunning,
                            DiagnosticMessage: $"Process with an Id of {processId} is not running.");
                }

                if (mainWindow is not null)
                {
                    windows.Add(mainWindow);
                }
            }

            if (windows.Count == 0)
            {
                if (!IsProcessRunning(processId))
                {
                    return new AppleMusicProbeReadOutcome(
                        AppleMusicProbeReadState.AppNotRunning,
                        DiagnosticMessage: $"Process with an Id of {processId} is not running.");
                }

                return new AppleMusicProbeReadOutcome(
                    AppleMusicProbeReadState.Recovering,
                    DiagnosticMessage: "No Apple Music windows found.");
            }

            var isMiniPlayer = false;
            AutomationElement? songPanel = null;
            foreach (var window in windows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                isMiniPlayer = string.Equals(window.Name, "Mini Player", StringComparison.Ordinal);
                if (isMiniPlayer)
                {
                    songPanel = window.FindFirstDescendant(cf => cf.ByClassName("InputSiteWindowClass"));
                    if (songPanel is not null)
                    {
                        break;
                    }
                }
                else
                {
                    songPanel = window.FindFirstDescendant(cf => cf.ByAutomationId("TransportBar")) ?? songPanel;
                }
            }

            if (songPanel is null)
            {
                return new AppleMusicProbeReadOutcome(
                    AppleMusicProbeReadState.Recovering,
                    DiagnosticMessage: "Apple Music song panel is not initialised or missing.");
            }

            var songFieldsPanel = isMiniPlayer ? songPanel : songPanel.FindFirstChild("LCD");
            if (songFieldsPanel is null)
            {
                return new AppleMusicProbeReadOutcome(
                    AppleMusicProbeReadState.Recovering,
                    DiagnosticMessage: "Apple Music track fields are temporarily unavailable.");
            }

            var songFields = songFieldsPanel.FindAllChildren(cf => cf.ByAutomationId("myScrollViewer")) ?? [];
            if (!isMiniPlayer && songFields.Length != 2)
            {
                return new AppleMusicProbeReadOutcome(
                    AppleMusicProbeReadState.NoTrackDetected,
                    DiagnosticMessage: "No active Apple Music track detected.");
            }

            if (songFields.Length < 2)
            {
                return new AppleMusicProbeReadOutcome(
                    AppleMusicProbeReadState.NoTrackDetected,
                    DiagnosticMessage: "No active Apple Music track detected.");
            }

            var songNameElement = songFields[0];
            var songAlbumArtistElement = songFields[1];
            if (songNameElement.BoundingRectangle.Bottom > songAlbumArtistElement.BoundingRectangle.Bottom)
            {
                songNameElement = songFields[1];
                songAlbumArtistElement = songFields[0];
            }

            var songName = songNameElement.Name;
            var songAlbumArtist = songAlbumArtistElement.Name;
            if (isMiniPlayer)
            {
                songName = DeduplicatedString(songName) ?? songName;
                songAlbumArtist = DeduplicatedString(songAlbumArtist) ?? songAlbumArtist;
            }

            var (songArtist, songAlbum) = ParseSongAlbumArtist(songAlbumArtist, _composerAsArtist);
            var playPauseButton = songPanel.FindFirstChild("TransportControl_PlayPauseStop");
            var slider = (isMiniPlayer
                    ? songPanel.FindFirstChild("Scrubber")
                    : songPanel.FindFirstChild("LCD")?.FindFirstChild("LCDScrubber"))?.Patterns.RangeValue.Pattern;
            var songProgressPercent = slider is null || slider.Maximum <= 0 ? 0 : slider.Value / slider.Maximum;

            var currentTimeElement = songFieldsPanel.FindFirstChild(cf => cf.ByAutomationId("CurrentTime"));
            var remainingDurationElement = songFieldsPanel.FindFirstChild(cf => cf.ByAutomationId("Duration"));
            var currentTime = ParseTimeString(currentTimeElement?.Name);
            var remainingDuration = ParseTimeString(remainingDurationElement?.Name);
            int? duration = currentTime.HasValue && remainingDuration.HasValue
                ? currentTime.Value + remainingDuration.Value
                : null;
            var audioBadge = songPanel.FindFirstDescendant(cf => cf.ByAutomationId("AudioBadgeButton"))?.Name;

            return new AppleMusicProbeReadOutcome(
                AppleMusicProbeReadState.Available,
                new AppleMusicProbeSnapshotData(
                    songName,
                    songArtist,
                    songAlbum,
                    songAlbumArtist,
                    DateTimeOffset.UtcNow,
                    playPauseButton?.Name,
                    songProgressPercent,
                    currentTime,
                    duration,
                    audioBadge,
                    isMiniPlayer ? "Mini Player" : "Main Window"));
        }

        public bool IsProcessRunning(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static (string Artist, string Album) ParseSongAlbumArtist(string songAlbumArtist, bool composerAsArtist)
        {
            var composerPerformerMatch = ComposerPerformerRegex.Matches(songAlbumArtist);
            if (composerPerformerMatch.Count > 0)
            {
                var parts = songAlbumArtist.Split(" \u2014 ");
                var composer = parts[0].Replace("By ", string.Empty, StringComparison.Ordinal);
                var performer = parts.Length > 1 ? parts[1] : composer;
                var album = parts.Length > 2 ? parts[2] : performer;
                return (composerAsArtist ? composer : performer, album);
            }

            var songSplit = songAlbumArtist.Split(" \u2014 ");
            if (songSplit.Length > 1)
            {
                return (songSplit[0], songSplit[1]);
            }

            return (songAlbumArtist, songAlbumArtist);
        }
    }
}
