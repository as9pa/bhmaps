using System.Windows;
using System.Windows.Media;
using BhMaps.App.ViewModels;
using BhMaps.App.ViewModels.Pages;

namespace BhMaps.App.Views.Controls;

/// <summary>Tells a rows page that one of its rows has come on screen, so the row reads its pictures then and
/// not before (addendum B, Performance). An attached property rather than a Loaded handler in each page's
/// code-behind, because the row template is shared by both rows pages and lives in the theme.</summary>
public static class RowRealiser
{
    public static readonly DependencyProperty RealiseProperty =
        DependencyProperty.RegisterAttached(
            "Realise", typeof(bool), typeof(RowRealiser), new PropertyMetadata(false, OnRealiseChanged));

    public static void SetRealise(DependencyObject element, bool value) => element.SetValue(RealiseProperty, value);

    public static bool GetRealise(DependencyObject element) => (bool)element.GetValue(RealiseProperty);

    private static void OnRealiseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.Loaded -= OnLoaded;
        if (e.NewValue is true)
        {
            element.Loaded += OnLoaded;
        }
    }

    /// <summary>Loaded fires again every time a recycled container comes back on screen; the row itself ignores
    /// every call after the first, so scrolling up and down costs one read of each file and no more.</summary>
    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MapRowViewModel row } element && FindPage(element) is { } page)
        {
            page.RealiseRow(row);
        }
    }

    private static RowsPageViewModel? FindPage(DependencyObject start)
    {
        for (DependencyObject? d = start; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement { DataContext: RowsPageViewModel page })
            {
                return page;
            }
        }

        return null;
    }
}
