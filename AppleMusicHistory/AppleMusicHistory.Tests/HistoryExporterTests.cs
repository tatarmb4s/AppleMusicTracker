using AppleMusicHistory.Core.Models;
using AppleMusicHistory.Infrastructure.Data;
using AppleMusicHistory.Infrastructure.Export;
using Microsoft.Data.Sqlite;

namespace AppleMusicHistory.Tests;

public sealed class HistoryExporterTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite");
    private readonly string _exportPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.tmp");

    [Fact]
    public async Task Export_IncludesAudioVariantFields()
    {
        var repository = new SqliteHistoryRepository(_databasePath);
        await repository.InitializeAsync(CancellationToken.None);

        var appRunId = await repository.StartAppRunAsync(
            new AppRunInfo(DateTimeOffset.UtcNow, "test", "machine", "user", ".NET", "Windows"),
            CancellationToken.None);

        var observedAt = DateTimeOffset.Parse("2026-03-09T12:00:00Z");
        var fingerprint = TrackFingerprint.From("Song", "Artist", "Album");
        var track = await repository.UpsertTrackAsync(
            new TrackUpsert(fingerprint, "Song", "Artist", "Album", "Artist — Album", observedAt, 180, CatalogAudioVariantsJson: "[\"Lossless\",\"Dolby Atmos\"]"),
            CancellationToken.None);

        var session = await repository.StartSessionAsync(
            new StartSessionRequest(track.TrackId, appRunId, observedAt, 0, 0, observedAt, SessionState.Playing, "Dolby Audio", PlaybackAudioVariant.DolbyAudio),
            CancellationToken.None);
        await repository.CloseSessionAsync(
            new SessionClosure(session.SessionId, observedAt.AddSeconds(10), 10, 10, observedAt.AddSeconds(10), SessionEndReason.TrackChanged),
            CancellationToken.None);

        var exporter = new HistoryExporter(repository);
        await exporter.ExportCsvAsync(_exportPath, CancellationToken.None);
        var csv = await File.ReadAllTextAsync(_exportPath, CancellationToken.None);

        Assert.Contains("CatalogAudioVariantsJson", csv);
        Assert.Contains("LastObservedAudioBadgeRaw", csv);
        Assert.Contains("LastObservedAudioVariant", csv);
        Assert.Contains("Dolby Audio", csv);
        Assert.Contains("DolbyAudio", csv);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _databasePath, _exportPath })
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }
}
