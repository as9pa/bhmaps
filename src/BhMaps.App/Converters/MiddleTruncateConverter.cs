using System.Globalization;
using System.Windows.Data;
using BhMaps.Core.Settings;

namespace BhMaps.App.Converters;

/// <summary>Shortens a folder path to the character budget in ConverterParameter by dropping segments out of its
/// middle (3.0), so the drive and the last two folders are both on the row. The whole path belongs in the ToolTip
/// beside it. PathTailConverter is the other half of the pair: it keeps the end of a file's relative path, where
/// the start says nothing.</summary>
public sealed class MiddleTruncateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var path = value as string ?? "";
        return int.TryParse(parameter as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var budget)
            ? PathText.MiddleTruncate(path, budget)
            : path;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
