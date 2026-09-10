using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using BhMaps.Core.Model;

namespace BhMaps.App.Converters;

/// <summary>Spec 5.1 badge colors: Applied green, Empty amber, Unmanaged gray.</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush AppliedBrush = Frozen(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly SolidColorBrush EmptyBrush = Frozen(Color.FromRgb(0xEF, 0x8F, 0x00));
    private static readonly SolidColorBrush UnmanagedBrush = Frozen(Color.FromRgb(0x75, 0x75, 0x75));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        FolderState.Applied => AppliedBrush,
        FolderState.Empty => EmptyBrush,
        _ => UnmanagedBrush,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
