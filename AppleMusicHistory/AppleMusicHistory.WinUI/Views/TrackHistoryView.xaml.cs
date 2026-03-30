using AppleMusicHistory.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace AppleMusicHistory.WinUI.Views;

public sealed partial class TrackHistoryView : UserControl
{
    public TrackHistoryView()
    {
        InitializeComponent();
    }

    private void TrackHistoryRow_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is TrackHistoryRowViewModel row)
        {
            row.IsHovered = true;
        }
    }

    private void TrackHistoryRow_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is TrackHistoryRowViewModel row)
        {
            row.IsHovered = false;
        }
    }
}
