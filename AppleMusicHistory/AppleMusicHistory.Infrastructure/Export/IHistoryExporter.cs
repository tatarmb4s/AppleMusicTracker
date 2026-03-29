namespace AppleMusicHistory.Infrastructure.Export;

public interface IHistoryExporter
{
    Task ExportCsvAsync(string filePath, CancellationToken cancellationToken);

    Task ExportJsonAsync(string filePath, CancellationToken cancellationToken);

    Task ExportTracksCsvAsync(string filePath, CancellationToken cancellationToken);

    Task ExportTracksJsonAsync(string filePath, CancellationToken cancellationToken);
}
