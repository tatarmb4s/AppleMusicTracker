namespace AppleMusicHistory.Infrastructure.Startup;

public interface IStartupRegistration
{
    bool IsEnabled();

    void SetEnabled(bool enabled, string targetPath);
}
