using System.Collections;
using System.Globalization;
using System.Windows.Data;

namespace BhMaps.App.Converters;

/// <summary>3.2: looks the first value up in the dictionary the second value is, for a row whose text lives on
/// the window rather than on the row. A key the dictionary does not hold, and an unresolved binding, read as
/// empty.</summary>
public sealed class LookupConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [{ } key, IDictionary map, ..] && map.Contains(key) ? map[key] ?? "" : "";

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
