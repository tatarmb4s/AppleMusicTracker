namespace AppleMusicHistory.Infrastructure.Data;

public static class AppPaths
{
    public const string ApplicationName = "AppleMusicTracker";

    public static string AppDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ApplicationName);

    public static string DatabasePath => Path.Combine(AppDataDirectory, "history.sqlite");

    public static string LogsDirectory => Path.Combine(AppDataDirectory, "logs");

    public static string SettingsPath => Path.Combine(AppDataDirectory, "settings.json");

    public static string StartupShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "AppleMusicTracker.lnk");
}
