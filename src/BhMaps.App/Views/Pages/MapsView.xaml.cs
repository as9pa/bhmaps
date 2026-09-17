using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using BhMaps.App.ViewModels;
using BhMaps.App.ViewModels.Pages;
using BhMaps.App.Views.Controls;

namespace BhMaps.App.Views.Pages;

public partial class MapsView : UserControl
{
    public MapsView()
    {
        InitializeComponent();
    }

    /// <summary>2.8: a click on the card opens the map panel, whatever is held down with it. The page handles it
    /// rather than the ListBox, so no modifier can select a second map.</summary>
    private void OnCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem item)
        {
            return;
        }

        // Focus by hand, because handling the event is what stops the ListBox from moving focus itself, and the
        // arrow keys have to carry on from the card that was clicked.
        item.Focus();
        e.Handled = true;
        if (item.DataContext is MapCardViewModel card && DataContext is MapsViewModel page)
        {
            // Spec 3.2 lists clicking the same card as one of the three ways to close the panel, beside Escape
            // and the X button. Clearing Selected is what closes it; OpenMap would only reopen the same panel.
            if (ReferenceEquals(page.Selected, card))
            {
                page.Selected = null;
                return;
            }

            page.OpenMapCommand.Execute(card.FolderName);
        }
    }

    /// <summary>Enter opens the focused card's panel, which is what Enter did while the card was a Button:
    /// without it the panel is mouse-only. Space is left to the ListBox and does nothing of its own (2.8).</summary>
    private void OnCardKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ListBoxItem item)
        {
            return;
        }

        if (e.Key == Key.Enter && item.DataContext is MapCardViewModel card && DataContext is MapsViewModel page)
        {
            // Open only, never close: Escape and the X button are what close the panel, and the card a keyboard
            // user is standing on is the one they just opened.
            page.OpenMapCommand.Execute(card.FolderName);
            e.Handled = true;
            return;
        }

        // Only the bare keys are the page's: Ctrl already navigates without selecting.
        if (Keyboard.Modifiers == ModifierKeys.None && MoveFocusTo(item, e.Key))
        {
            e.Handled = true;
        }
    }

    /// <summary>Spec 3.1: the navigation keys move focus between cards and leave the open card selected. The
    /// ListBox would make whichever card focus lands on the selection, which is the light border off the map the
    /// panel is open on, so the page moves focus itself before the ListBox sees the key. The arrows move the way
    /// Ctrl+arrow already does. False for any other key, which the ListBox then handles as usual.</summary>
    private static bool MoveFocusTo(ListBoxItem item, Key key)
    {
        FocusNavigationDirection? direction = key switch
        {
            Key.Left => FocusNavigationDirection.Left,
            Key.Right => FocusNavigationDirection.Right,
            Key.Up => FocusNavigationDirection.Up,
            Key.Down => FocusNavigationDirection.Down,
            _ => null,
        };

        if ((direction is null && key is not (Key.Home or Key.End or Key.PageUp or Key.PageDown))
            || ItemsControl.ItemsControlFromItemContainer(item) is not ListBox list)
        {
            return false;
        }

        // Moving the focus is not enough on its own: while the keyboard is the most recent input device, the
        // ListBox makes whichever card takes focus the selection, so the border would move anyway. Multiple is
        // the one mode where focus is only focus, and it is on for the length of the move, so the card the panel
        // is open on survives the round trip.
        var mode = list.SelectionMode;
        list.SelectionMode = SelectionMode.Multiple;
        try
        {
            // Geometric and contained by the grid, so a card at an edge keeps the focus rather than wrapping.
            if (direction is { } d)
            {
                item.MoveFocus(new TraversalRequest(d));
                return true;
            }

            // A page is the rows the viewport holds, and the columns are the items panel's own, so neither number
            // is a second copy of the zoom.
            var row = item.ActualHeight + item.Margin.Top + item.Margin.Bottom;
            var columns = VisualTreeHelper.GetParent(item) is UniformGrid grid && grid.Columns > 0 ? grid.Columns : 1;
            var page = columns * Math.Max(1, (int)(row > 0 ? list.ActualHeight / row : 1));
            var index = list.ItemContainerGenerator.IndexFromContainer(item);
            var target = key switch
            {
                Key.Home => 0,
                Key.End => list.Items.Count - 1,
                Key.PageUp => index - page,
                _ => index + page,
            };

            if (list.ItemContainerGenerator.ContainerFromIndex(Math.Clamp(target, 0, list.Items.Count - 1)) is ListBoxItem next)
            {
                next.Focus();
                next.BringIntoView();
            }

            return true;
        }
        finally
        {
            list.SelectionMode = mode;
        }
    }

    /// <summary>Spec 3.1: Escape in the search box clears what was typed and stops there, so the page's own
    /// Escape, which closes the panel, is left for an empty or unfocused box. The same command the Clear search
    /// button runs, so there is one way to empty the box.
    /// Spec section 13's menu key and Shift+F10 are handled from here too, because a UserControl has one
    /// PreviewKeyDown: TileMenus takes those two keys and leaves every other one alone, so the two do not
    /// collide over Escape.</summary>
    private void OnPageKeyDown(object sender, KeyEventArgs e)
    {
        // The keyboard opens the menu itself rather than through ContextMenuService, so nothing raises
        // ContextMenuOpening and the card's lines are built here first: a card whose menu was never built would
        // otherwise open an empty popup (spec 4.3).
        if (e.Key is Key.Apps or Key.F10 or Key.System
            && Keyboard.FocusedElement is FrameworkElement { DataContext: MapCardViewModel focused }
            && DataContext is MapsViewModel owner)
        {
            owner.BuildCardMenu(focused);
        }

        TileMenus.OnPreviewKeyDown(sender, e);
        if (e.Handled || e.Key != Key.Escape || DataContext is not MapsViewModel page || page.SearchText.Length == 0)
        {
            return;
        }

        // By Tag, not by name: the box sits inside the PageHeader user control's own name scope (MainWindow's
        // Ctrl+K finds it the same way).
        if (e.OriginalSource is TextBox box && Equals(box.Tag, "SearchBox"))
        {
            page.ClearSearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Spec 4.3: the card's lines are built when the menu opens, because there are as many cards as the
    /// game has maps. The selection is put back first: the ListBox selects the card it is pressed on whichever
    /// button it was, and a right click leaves the panel, and with it the light border, where they were.</summary>
    private void OnCardMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: MapCardViewModel card } && DataContext is MapsViewModel page)
        {
            page.RestoreSelectedCard();
            page.BuildCardMenu(card);
        }
    }

    /// <summary>The three-dot button on a panel tile. The button carries no menu of its own, so TileMenus walks
    /// up to the tile that does.</summary>
    private void OnTileMenuButton(object sender, RoutedEventArgs e) => TileMenus.OpenFor(sender);
}
