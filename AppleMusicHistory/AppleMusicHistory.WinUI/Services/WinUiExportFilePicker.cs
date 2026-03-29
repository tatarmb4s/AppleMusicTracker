using System.Threading;
using System.Threading.Tasks;
using AppleMusicHistory.Host;
using AppleMusicHistory.WinUI.Helpers;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AppleMusicHistory.WinUI.Services;

internal sealed class WinUiExportFilePicker : IExportFilePicker
{
    private readonly Window _window;

    public WinUiExportFilePicker(Window window)
    {
        _window = window;
    }

    public async Task<string?> PickSavePathAsync(ExportKind exportKind, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = exportKind.GetDefaultFileName()
        };
        picker.FileTypeChoices.Add(exportKind.GetFileTypeLabel(), [exportKind.GetSuggestedFileType()]);

        InitializeWithWindow.Initialize(picker, WindowingHelper.GetWindowHandle(_window));

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }
}
