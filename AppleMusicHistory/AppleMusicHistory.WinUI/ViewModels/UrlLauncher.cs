using System.Diagnostics;

namespace AppleMusicHistory.WinUI.ViewModels;

internal static class UrlLauncher
{
    public static bool TryOpen(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
        return true;
    }
}
