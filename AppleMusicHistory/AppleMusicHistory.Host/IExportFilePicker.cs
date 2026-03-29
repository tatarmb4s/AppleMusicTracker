namespace AppleMusicHistory.Host;

public interface IExportFilePicker
{
    Task<string?> PickSavePathAsync(ExportKind exportKind, CancellationToken cancellationToken);
}
