using System.Globalization;
using System.Windows.Data;

namespace BhMaps.App.Converters;

/// <summary>Shortens a path to the character budget in ConverterParameter, keeping the end and putting a leading
/// ellipsis in front of it: the file and the folder it sits in are what tells two source paths apart, and they are
/// at the end. The whole path belongs in the ToolTip beside it.</summary>
public sealed class PathTailConverter : IValueConverter
{
    private const char Ellipsis = '…';

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var path = value as string ?? "";
        if (!int.TryParse(parameter as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var budget)
            || budget < 2
            || path.Length <= budget)
        {
            return path;
        }

        // The ellipsis is one of the budgeted characters, so the result is never longer than asked for.
        return Ellipsis + path[^(budget - 1)..];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
