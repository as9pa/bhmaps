using System.Globalization;
using System.Windows.Data;
using BhMaps.App.ViewModels.Pages;

namespace BhMaps.App.Converters;

/// <summary>3.7.3: the pack page's SelectedItem binding. The grid also holds the Add image card, which arrow keys
/// can land on; it is not a picture, so it never reaches SelectedTile, and the binding logs no error for it.</summary>
public sealed class PackTileSelectionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null or PackTileViewModel ? value : Binding.DoNothing;
}
