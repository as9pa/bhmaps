using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BhMaps.App.ViewModels;
using BhMaps.App.ViewModels.Pages;

namespace BhMaps.App.Views.Pages;

public partial class MapsView : UserControl
{
    public MapsView()
    {
        InitializeComponent();
    }

    /// <summary>Spec 3.1: a plain click on the card body opens the map panel and leaves the ticks alone; a Ctrl
    /// or Shift click is the ListBox's, and so is a click on the tick box itself.</summary>
    private void OnCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem item || Keyboard.Modifiers != ModifierKeys.None || IsInsideTick(e.OriginalSource))
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

    /// <summary>Space toggles the focused card (spec 3.1). Extended selection would otherwise make Space replace
    /// the whole set with that one card. Enter opens the card's panel, which is what Enter did while the card was
    /// a Button: without it the panel is mouse-only.</summary>
    private void OnCardKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ListBoxItem item)
        {
            return;
        }

        if (e.Key == Key.Space)
        {
            item.IsSelected = !item.IsSelected;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && item.DataContext is MapCardViewModel card && DataContext is MapsViewModel page)
        {
            // Open only, never close: Escape and the X button are what close the panel, and the card a keyboard
            // user is standing on is the one they just opened.
            page.OpenMapCommand.Execute(card.FolderName);
            e.Handled = true;
        }
    }

    /// <summary>Spec 3.1: Escape in the search box clears what was typed and stops there, so the page's own
    /// Escape order (the panel, then the ticks) is left for an empty or unfocused box. The same command the
    /// Clear search button runs, so there is one way to empty the box.</summary>
    private void OnPageKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not MapsViewModel page || page.SearchText.Length == 0)
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

    private static bool IsInsideTick(object? source)
    {
        for (var d = source as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is CheckBox)
            {
                return true;
            }
        }

        return false;
    }
}
