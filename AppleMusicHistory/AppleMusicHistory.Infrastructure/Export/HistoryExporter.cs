using System.Text;
using System.Text.Json;
using AppleMusicHistory.Core.Abstractions;

namespace AppleMusicHistory.Infrastructure.Export;

public sealed class HistoryExporter
{
    private readonly IHistoryRepository _repository;

    public HistoryExporter(IHistoryRepository repository)
    {
        _repository = repository;
    }

    public async Task ExportCsvAsync(string filePath, CancellationToken cancellationToken)
    {
        var rows = await _repository.ExportSessionsAsync(null, null, cancellationToken).ConfigureAwait(false);
        var builder = new StringBuilder();
        builder.AppendLine("SessionId,Fingerprint,Title,Artist,Album,Subtitle,StartedAtUtc,EndedAtUtc,FirstPositionSeconds,LastPositionSeconds,MaxPositionSeconds,HeardSeconds,PauseCount,ResumeCount,ReplayIndex,State,EndReason,LastObservedUtc,SongUrl,ArtistUrl,ArtworkUrl,CatalogAudioVariantsJson,LastObservedAudioBadgeRaw,LastObservedAudioVariant");

        foreach (var row in rows)
        {
            var cells = new[]
            {
                row.SessionId.ToString(),
                row.Fingerprint,
                row.Title,
                row.Artist,
                row.Album,
                row.Subtitle,
                row.StartedAtUtc.ToString("O"),
                row.EndedAtUtc?.ToString("O") ?? string.Empty,
                row.FirstPositionSeconds.ToString(),
                row.LastPositionSeconds.ToString(),
                row.MaxPositionSeconds.ToString(),
                row.HeardSeconds.ToString("F2"),
                row.PauseCount.ToString(),
                row.ResumeCount.ToString(),
                row.ReplayIndex.ToString(),
                row.State.ToString(),
                row.EndReason?.ToString() ?? string.Empty,
                row.LastObservedUtc.ToString("O"),
                row.SongUrl ?? string.Empty,
                row.ArtistUrl ?? string.Empty,
                row.ArtworkUrl ?? string.Empty,
                row.CatalogAudioVariantsJson ?? string.Empty,
                row.LastObservedAudioBadgeRaw ?? string.Empty,
                row.LastObservedAudioVariant?.ToString() ?? string.Empty
            };

            builder.AppendLine(string.Join(",", cells.Select(EscapeCsv)));
        }

        await File.WriteAllTextAsync(filePath, builder.ToString(), cancellationToken).ConfigureAwait(false);
    }

    public async Task ExportJsonAsync(string filePath, CancellationToken cancellationToken)
    {
        var sessions = await _repository.ExportSessionsAsync(null, null, cancellationToken).ConfigureAwait(false);
        var payload = new List<object>(sessions.Count);
        foreach (var session in sessions)
        {
            var events = await _repository.GetSessionEventsAsync(session.SessionId, cancellationToken).ConfigureAwait(false);
            payload.Add(new
            {
                session,
                events
            });
        }

        await File.WriteAllTextAsync(
            filePath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken).ConfigureAwait(false);
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
