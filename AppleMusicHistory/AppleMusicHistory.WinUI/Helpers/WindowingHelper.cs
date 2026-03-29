using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using Windows.Graphics;
using Windows.UI;

namespace AppleMusicHistory.WinUI.Helpers;

internal static class WindowingHelper
{
    private const int SwHide = 0;
    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    public static nint GetWindowHandle(Window window) => WindowNative.GetWindowHandle(window);

    public static AppWindow GetAppWindow(Window window)
        => AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(GetWindowHandle(window)));

    public static void Hide(Window window)
    {
        ShowWindow(GetWindowHandle(window), SwHide);
    }

    public static void Show(Window window)
    {
        ShowWindow(GetWindowHandle(window), SwRestore);
        window.Activate();
    }

    public static void ConfigureWindow(Window window, string title)
    {
        var appWindow = GetAppWindow(window);
        appWindow.Title = title;
        appWindow.Resize(new SizeInt32(1400, 860));

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMinimizable = true;
            presenter.IsMaximizable = true;
            presenter.IsResizable = true;
        }

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = appWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(32, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(52, 255, 255, 255);
            titleBar.ButtonForegroundColor = Colors.White;
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(160, 255, 255, 255);
            titleBar.ButtonHoverForegroundColor = Colors.White;
            titleBar.ButtonPressedForegroundColor = Colors.White;
        }
    }
}
