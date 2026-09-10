using System.Globalization;
using System.Windows.Data;
using BhMaps.App.ViewModels.Pages;

namespace BhMaps.App.Converters;

/// <summary>True when a sidebar nav row's page is the shell's current page. Reference equality, because the six
/// pages are built once and live as long as the shell. Typed on both sides, so an unresolved binding, which
/// arrives as DependencyProperty.UnsetValue rather than null, reads as false instead of matching every row.</summary>
public sealed class IsCurrentPageConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is PageViewModel page && ReferenceEquals(page, values[1]);

    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
