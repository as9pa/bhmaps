using System.Globalization;
using System.Windows.Data;

namespace BhMaps.App.Converters;

/// <summary>The other side of a bool, for the second half of a two-button switch: one button reads the property
/// and the other reads its opposite, so the pair is one value rather than two. Anything that is not a bool, which
/// an unresolved binding is, reads as false both ways.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool on && !on;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool on && !on;
}
