namespace AppleMusicHistory.Host;

public interface ITrackerRuntime : IAsyncDisposable
{
    event Action<RuntimeStatus>? StatusChanged;

    void Start();

    Task SetTrackingPausedAsync(bool isPaused);
}
