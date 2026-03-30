using System;
using System.IO;
using AppleMusicHistory.Core.Models;

namespace AppleMusicHistory.WinUI.ViewModels;

internal static class AudioBadgeAssetResolver
{
    public static string? ResolveAssetUri(PlaybackAudioVariant? variant)
    {
        var fileName = variant switch
        {
            PlaybackAudioVariant.DolbyAudio => "dolbyLogo.png",
            PlaybackAudioVariant.DolbyAtmos => "dolbyLogo.png",
            PlaybackAudioVariant.Lossless => "losless.png",
            PlaybackAudioVariant.HiResLossless => "loslessHighRes.png",
            _ => null
        };

        return fileName is null
            ? null
            : new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "AudioBadges", fileName)).AbsoluteUri;
    }
}
