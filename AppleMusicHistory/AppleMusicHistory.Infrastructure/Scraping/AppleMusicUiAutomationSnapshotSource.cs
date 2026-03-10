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
    private static readonly Regex ComposerPerformerRegex = new(@"By\s.*?\s\u2014", RegexOptions.Compiled);
    private readonly FileLogger? _logger;
    private readonly bool _composerAsArtist;
    private double? _previousSongProgress;

    public AppleMusicUiAutomationSnapshotSource(bool composerAsArtist = true, FileLogger? logger = null)
    {
        _composerAsArtist = composerAsArtist;
        _logger = logger;
    }

    public async Task<PlaybackSnapshot?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        try
        {
            var amProcesses = Process.GetProcessesByName("AppleMusic");
            if (amProcesses.Length == 0)
            {
                return null;
            }

            var windows = new List<AutomationElement>();
            await Task.Run(() =>
            {
                using var automation = new UIA3Automation();
                var processId = amProcesses[0].Id;
                windows.AddRange(automation.GetDesktop().FindAllChildren(c => c.ByProcessId(processId)));
                if (windows.Count == 0)
                {
                    var mainWindow = FlaUI.Core.Application.Attach(processId).GetMainWindow(automation, TimeSpan.FromSeconds(3));
                    if (mainWindow is not null)
                    {
                        windows.Add(mainWindow);
                    }
                }
            }, cancellationToken).ConfigureAwait(false);

            if (windows.Count == 0)
            {
                return null;
            }

            var isMiniPlayer = false;
            AutomationElement? songPanel = null;
            foreach (var window in windows)
            {
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
                return null;
            }

            var songFieldsPanel = isMiniPlayer ? songPanel : songPanel.FindFirstChild("LCD");
            var songFields = songFieldsPanel?.FindAllChildren(cf => cf.ByAutomationId("myScrollViewer")) ?? [];

            if (!isMiniPlayer && songFields.Length != 2)
            {
                return null;
            }

            if (songFields.Length < 2)
            {
                return null;
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

            var isPaused = DeterminePauseState(playPauseButton?.Name, songProgressPercent);
            var currentTimeElement = songFieldsPanel?.FindFirstChild(cf => cf.ByAutomationId("CurrentTime"));
            var remainingDurationElement = songFieldsPanel?.FindFirstChild(cf => cf.ByAutomationId("Duration"));

            var currentTime = ParseTimeString(currentTimeElement?.Name);
            var remainingDuration = ParseTimeString(remainingDurationElement?.Name);
            int? duration = currentTime.HasValue && remainingDuration.HasValue
                ? currentTime.Value + remainingDuration.Value
                : null;
            var audioBadge = songPanel.FindFirstDescendant(cf => cf.ByAutomationId("AudioBadgeButton"))?.Name;

            return PlaybackSnapshot.Create(
                songName,
                songArtist,
                songAlbum,
                songAlbumArtist,
                DateTimeOffset.UtcNow,
                isPaused,
                currentTime,
                duration,
                audioBadge,
                isMiniPlayer ? "Mini Player" : "Main Window");
        }
        catch (Exception ex)
        {
            if (_logger is not null)
            {
                await _logger.ErrorAsync($"Apple Music scrape failed: {ex.Message}", cancellationToken).ConfigureAwait(false);
            }

            return null;
        }
    }

    internal static PlaybackAudioVariant? ParseAudioBadge(string? badge)
        => PlaybackAudioVariantParser.ParseBadge(badge);

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
}
