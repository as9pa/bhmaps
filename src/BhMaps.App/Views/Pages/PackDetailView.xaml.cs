using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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

        // 3.7.3: the pictures, then the Add image card. Two collections rather than one, so everything on the page
        // that walks Items (select all, apply, copy, delete) only ever meets pictures.
        DataContextChanged += (_, _) => Tiles.ItemsSource = Page is { } page
            ? new CompositeCollection
            {
                new CollectionContainer { Collection = page.Items },
                new CollectionContainer { Collection = page.AddTiles },
            }
            : null;
    }

    private PackDetailViewModel? Page => DataContext as PackDetailViewModel;

    /// <summary>3.7.3: files can be dropped on a pack the app writes to, and nowhere else.</summary>
    private void Page_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = Page is { CanAddImage: true } && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>3.7.3: the drop opens Add pictures with the files listed; the window keeps the images.</summary>
    private async void Page_Drop(object sender, DragEventArgs e)
    {
        if (Page is { CanAddImage: true } page
            && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            e.Handled = true;
            await page.AddImagesAsync(files);
        }
    }

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

    /// <summary>Spec 2.6 4.3: the tile under the pointer is what Ctrl+C, Ctrl+X and the menu act on when there
    /// is one. Written on the page rather than read from the visual tree when the key arrives, because a key
    /// binding has no pointer position.</summary>
    private void Tile_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PackTileViewModel tile } && Page is { } page)
        {
            page.KeyTarget = tile;
        }
    }

    private void Tile_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PackTileViewModel tile } && Page is { } page
            && ReferenceEquals(page.KeyTarget, tile))
        {
            page.KeyTarget = null;
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

        // 3.7.3: on the Add image card, Enter and Space open the window the card's click does.
        if (e.Key is Key.Enter or Key.Space
            && Keyboard.FocusedElement is FrameworkElement { DataContext: AddImageTile }
            && Page is { } page)
        {
            page.AddImageCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            Page?.OpenSelected();
            e.Handled = true;
        }
    }

    /// <summary>3.7.3: arrow keys can land on the Add image card, and the ListBox selects what they land on. The
    /// card is not a picture, so the selection goes back to the page's tile; the converter has already kept the
    /// card out of SelectedTile.</summary>
    private void Tiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Tiles.SelectedItem is AddImageTile)
        {
            Tiles.SelectedItem = Page?.SelectedTile;
        }
    }

    /// <summary>Spec 2.6 section 3: the lines are built here, when the menu opens, so they name what the last
    /// scan found. Every tile has a menu now, so nothing is handled away.</summary>
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
