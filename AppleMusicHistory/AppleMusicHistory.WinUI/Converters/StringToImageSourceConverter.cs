using System;
using System.IO;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AppleMusicHistory.WinUI.Converters;

public sealed class StringToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        var pathOrUrl = value as string;
        if (string.IsNullOrWhiteSpace(pathOrUrl))
        {
            return null;
        }

        try
        {
            if (Path.IsPathRooted(pathOrUrl) && !File.Exists(pathOrUrl))
            {
                return null;
            }

            if (!Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri))
            {
                uri = new Uri(pathOrUrl, UriKind.RelativeOrAbsolute);
            }

            return new BitmapImage(uri);
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
