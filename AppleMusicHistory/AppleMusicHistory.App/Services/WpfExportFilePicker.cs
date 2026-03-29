using AppleMusicHistory.Host;

namespace AppleMusicHistory.App.Services;

internal sealed class WpfExportFilePicker : IExportFilePicker
{
    public Task<string?> PickSavePathAsync(ExportKind exportKind, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = $"{exportKind.GetFileTypeLabel()} (*{exportKind.GetSuggestedFileType()})|*{exportKind.GetSuggestedFileType()}",
            FileName = exportKind.GetDefaultFileName()
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }
}
