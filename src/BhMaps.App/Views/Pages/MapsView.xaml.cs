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
    /// the whole set with that one card.</summary>
    private void OnCardKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && sender is ListBoxItem item)
        {
            item.IsSelected = !item.IsSelected;
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
