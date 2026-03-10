namespace AppleMusicHistory.Infrastructure.Data;

public sealed class FileLogger
{
    private readonly string _logFilePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileLogger()
    {
        Directory.CreateDirectory(AppPaths.LogsDirectory);
        _logFilePath = Path.Combine(AppPaths.LogsDirectory, $"{DateTime.UtcNow:yyyy-MM-dd}.log");
    }

    public Task InfoAsync(string message, CancellationToken cancellationToken = default) => WriteAsync("INFO", message, cancellationToken);

    public Task ErrorAsync(string message, CancellationToken cancellationToken = default) => WriteAsync("ERROR", message, cancellationToken);

    private async Task WriteAsync(string level, string message, CancellationToken cancellationToken)
    {
        var line = $"[{DateTimeOffset.UtcNow:O}] [{level}] {message}{Environment.NewLine}";
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(_logFilePath, line, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
