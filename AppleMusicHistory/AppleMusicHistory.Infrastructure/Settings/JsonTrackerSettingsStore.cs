using System.Text.Json;

namespace AppleMusicHistory.Infrastructure.Settings;

public sealed class JsonTrackerSettingsStore
{
    private readonly string _settingsPath;

    public JsonTrackerSettingsStore(string settingsPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        _settingsPath = settingsPath;
    }

    public async Task<TrackerSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            return new TrackerSettings();
        }

        await using var stream = File.OpenRead(_settingsPath);
        return await JsonSerializer.DeserializeAsync<TrackerSettings>(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? new TrackerSettings();
    }

    public async Task SaveAsync(TrackerSettings settings, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(_settingsPath, json, cancellationToken).ConfigureAwait(false);
    }
}
