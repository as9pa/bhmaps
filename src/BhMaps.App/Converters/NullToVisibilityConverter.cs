using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BhMaps.App.Converters;

/// <summary>Null becomes Collapsed and anything else Visible. ConverterParameter "Inverse" flips it.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var inverse = string.Equals(parameter as string, "Inverse", StringComparison.OrdinalIgnoreCase);
        var visible = value is not null;
        return visible ^ inverse ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
