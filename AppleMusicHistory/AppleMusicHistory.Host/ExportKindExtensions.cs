namespace AppleMusicHistory.Host;

public static class ExportKindExtensions
{
    public static bool IsJson(this ExportKind exportKind)
        => exportKind is ExportKind.SessionsJson or ExportKind.TracksJson;

    public static string GetDefaultFileName(this ExportKind exportKind)
    {
        var prefix = exportKind is ExportKind.TracksCsv or ExportKind.TracksJson
            ? "apple-music-tracks"
            : "apple-music-history";
        var extension = exportKind.IsJson() ? "json" : "csv";
        return $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}";
    }

    public static string GetFileTypeLabel(this ExportKind exportKind)
        => exportKind.IsJson() ? "JSON file" : "CSV file";

    public static string GetSuggestedFileType(this ExportKind exportKind)
        => exportKind.IsJson() ? ".json" : ".csv";
}
