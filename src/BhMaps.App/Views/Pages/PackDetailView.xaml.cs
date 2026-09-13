using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using BhMaps.App.ViewModels.Pages;
using BhMaps.App.Views.Controls;

namespace BhMaps.App.Views.Pages;

public partial class PackDetailView : UserControl
{
    public PackDetailView()
    {
        InitializeComponent();
    }

    private PackDetailViewModel? Page => DataContext as PackDetailViewModel;

    /// <summary>Spec 5: a click on a tile opens the drawer. Handled on the container rather than through the
    /// ListBox's selection, so arrowing through the grid moves focus without opening anything.</summary>
    private void Tile_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PackTileViewModel tile } container
            && !IsInsideButton(e.OriginalSource, container))
        {
            Page?.Open(tile);
        }
    }

    /// <summary>The menu button sits over the picture, so the click that opens the menu must not also open the
    /// drawer under it.</summary>
    private static bool IsInsideButton(object? source, FrameworkElement container)
    {
        for (var d = source as DependencyObject; d is not null && d != container; d = VisualTreeHelper.GetParent(d))
        {
            if (d is ButtonBase)
            {
                return true;
            }
        }

        return false;
    }

    private void Tiles_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // The keyboard opens the menu itself rather than through ContextMenuService, so nothing raises
        // ContextMenuOpening and the lines are built here first: WPF puts up no popup for a menu with no lines.
        if (e.Key is Key.Apps or Key.F10 or Key.System
            && Keyboard.FocusedElement is FrameworkElement { DataContext: PackTileViewModel focused })
        {
            Page?.BuildTileMenu(focused);
        }

        TileMenus.OnPreviewKeyDown(sender, e);
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            Page?.OpenSelected();
            e.Handled = true;
        }
    }

    /// <summary>Spec 2.6 section 3: the lines are built here, not when the ticks change, so the ticked line names
    /// the count the user can see. Every tile has a menu now, so nothing is handled away.</summary>
    private void OnTileMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PackTileViewModel tile }
            && DataContext is PackDetailViewModel page)
        {
            page.BuildTileMenu(tile);
        }
    }

    /// <summary>The button opens the menu the same way the keyboard does, so it builds the lines itself: only a
    /// right click comes through ContextMenuOpening.</summary>
    private void OnTileMenuButton(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PackTileViewModel tile })
        {
            Page?.BuildTileMenu(tile);
        }

        TileMenus.OpenFor(sender);
    }
}
