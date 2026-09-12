using System.Globalization;
using System.Windows.Data;

namespace BhMaps.App.Converters;

/// <summary>True when the width it is given is at least the pixel count in ConverterParameter. Bound to an
/// element's ActualWidth, it lets a template pick a layout off the room it actually has rather than off the zoom
/// step, which is only a guess at the width once the window can be any size.</summary>
public sealed class WidthAtLeastConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double width
        && double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var least)
        && width >= least;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
