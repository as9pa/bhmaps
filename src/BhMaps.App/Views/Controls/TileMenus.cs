using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace BhMaps.App.Views.Controls;

/// <summary>The two ways a tile's menu opens that are not a right click: the keyboard (spec section 13) and the
/// menu button drawn on the tile. Both find the ContextMenu on the element that carries it rather than binding a
/// popup to a view model, because opening a popup is a view concern.</summary>
public static class TileMenus
{
    /// <summary>Shift+F10 and the menu key, on whatever has focus. Windows delivers F10 as a system key, so the
    /// real key is read out of SystemKey when that is what arrived. Handled is set so WPF's own handling of the
    /// same two keys cannot open the menu a second time.</summary>
    public static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var wanted = key == Key.Apps
            || (key == Key.F10 && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);
        if (!wanted || Keyboard.FocusedElement is not FrameworkElement focused)
        {
            return;
        }

        if (Open(focused))
        {
            e.Handled = true;
        }
    }

    /// <summary>The tile's menu button. The button itself carries no menu, so the search walks up to the tile.</summary>
    public static void OpenFor(object sender)
    {
        if (sender is FrameworkElement element)
        {
            Open(element);
        }
    }

    /// <summary>False when nothing from here up owns a menu, which is not an error: a focused search box has none.</summary>
    private static bool Open(FrameworkElement start)
    {
        var element = start;
        while (element is not null && element.ContextMenu is null)
        {
            element = VisualTreeHelper.GetParent(element) as FrameworkElement;
        }

        if (element?.ContextMenu is not { } menu)
        {
            return false;
        }

        menu.PlacementTarget = element;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
        return true;
    }

    /// <summary>Set on a button inside a tile template: clicking it opens the menu of the first element above it
    /// that owns one. An attached property rather than a Click handler, because the row templates live in
    /// Controls.xaml, which is a resource dictionary and has no code-behind to hold one.</summary>
    public static readonly DependencyProperty OpensMenuProperty =
        DependencyProperty.RegisterAttached(
            "OpensMenu", typeof(bool), typeof(TileMenus), new PropertyMetadata(false, OnOpensMenuChanged));

    public static void SetOpensMenu(DependencyObject element, bool value) =>
        element.SetValue(OpensMenuProperty, value);

    public static bool GetOpensMenu(DependencyObject element) => (bool)element.GetValue(OpensMenuProperty);

    private static void OnOpensMenuChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase button)
        {
            return;
        }

        button.Click -= OnMenuButtonClick;
        if (e.NewValue is true)
        {
            button.Click += OnMenuButtonClick;
        }
    }

    /// <summary>Handled, so the click does not also reach the tile button under it and apply the picture.</summary>
    private static void OnMenuButtonClick(object sender, RoutedEventArgs e)
    {
        OpenFor(sender);
        e.Handled = true;
    }
}
