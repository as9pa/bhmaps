using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using BhMaps.Core.Model;

namespace BhMaps.App.Converters;

/// <summary>Spec 5.1 badge colors, taken from the theme: Applied accent, Empty missing, Unmanaged Text3.</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    // Resolved once, on first use rather than in a field initialiser, because App.xaml's dictionaries are not
    // merged yet when a converter declared in one of them is constructed. The fallbacks are the hardcoded colours
    // this converter carried before the tokens existed: a unit test runs with no Application at all.
    private static SolidColorBrush? _applied;
    private static SolidColorBrush? _empty;
    private static SolidColorBrush? _unmanaged;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        FolderState.Applied => _applied ??= Resource("AccentBrush", Color.FromRgb(0x2E, 0x7D, 0x32)),
        FolderState.Empty => _empty ??= Resource("MissingFgBrush", Color.FromRgb(0xEF, 0x8F, 0x00)),
        _ => _unmanaged ??= Resource("Text3Brush", Color.FromRgb(0x75, 0x75, 0x75)),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Resource(string key, Color fallback)
    {
        if (Application.Current?.TryFindResource(key) is SolidColorBrush brush)
        {
            return brush;
        }

        var plain = new SolidColorBrush(fallback);
        plain.Freeze();
        return plain;
    }
}
