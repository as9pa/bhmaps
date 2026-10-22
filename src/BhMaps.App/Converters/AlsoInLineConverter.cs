using System.Globalization;
using System.Windows.Data;
using BhMaps.App.ViewModels;

namespace BhMaps.App.Converters;

/// <summary>S3: a platform row's quiet line, its "also in" mark looked up by <see cref="LookupConverter"/> from the
/// first two values, then <see cref="PlatformPieceViewModel.AlsoInLine"/> with the third, the row's
/// SharedWithOtherMaps flag.</summary>
public sealed class AlsoInLineConverter : IMultiValueConverter
{
    private static readonly LookupConverter Lookup = new();

    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var mark = Lookup.Convert(values, targetType, parameter, culture) as string ?? "";
        return PlatformPieceViewModel.AlsoInLine(mark, values is [_, _, true, ..]);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
