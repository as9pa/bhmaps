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

    /// <summary>Loaded fires again every time a recycled container comes back on screen; both row types ignore
    /// every call after the first, so scrolling up and down costs one read of each file and no more.</summary>
    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        switch (element.DataContext)
        {
            case MapRowViewModel row when Find<RowsPageViewModel>(element) is { } page:
                page.RealiseRow(row);
                break;
            case PackRowViewModel pack when Find<PacksViewModel>(element) is { } packs:
                packs.RealiseRow(pack);
                break;
        }
    }

    /// <summary>The nearest ancestor whose DataContext is that page. The rows page and the Packs page are both
    /// bound as the view's DataContext, so the walk ends at the UserControl.</summary>
    private static T? Find<T>(DependencyObject start)
        where T : class
    {
        for (DependencyObject? d = start; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement element && element.DataContext is T found)
            {
                return found;
            }
        }

        return null;
    }
}
