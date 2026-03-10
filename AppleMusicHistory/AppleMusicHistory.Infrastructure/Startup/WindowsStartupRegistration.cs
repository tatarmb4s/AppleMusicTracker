using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AppleMusicHistory.Infrastructure.Startup;

[SupportedOSPlatform("windows")]
public sealed class WindowsStartupRegistration
{
    private readonly string _shortcutPath;

    public WindowsStartupRegistration(string shortcutPath)
    {
        _shortcutPath = shortcutPath;
    }

    public bool IsEnabled() => File.Exists(_shortcutPath);

    public void SetEnabled(bool enabled, string targetPath)
    {
        if (enabled)
        {
            CreateShortcut(targetPath);
            return;
        }

        if (File.Exists(_shortcutPath))
        {
            File.Delete(_shortcutPath);
        }
    }

    private void CreateShortcut(string targetPath)
    {
        var shellType = Type.GetTypeFromCLSID(new Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8"))
            ?? throw new InvalidOperationException("Windows Script Host shell is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(_shortcutPath);
            try
            {
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
                shortcut.IconLocation = $"{targetPath},0";
                shortcut.Save();
            }
            finally
            {
                Marshal.FinalReleaseComObject(shortcut);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
