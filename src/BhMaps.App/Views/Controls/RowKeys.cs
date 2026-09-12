using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BhMaps.App.ViewModels.Pages;

namespace BhMaps.App.Views.Controls;

/// <summary>The keyboard a rows page answers (addendum B): the menu key and Shift+F10 on a focused tile, and
/// Escape in the search box, which clears what was typed and stops there. Up, Down, Left and Right are WPF's own
/// directional navigation, which the page turns on with KeyboardNavigation.DirectionalNavigation, and Enter on a
/// focused tile is the Button's own. Shared by both rows pages, so neither can answer a key differently.</summary>
public static class RowKeys
{
    public static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        TileMenus.OnPreviewKeyDown(sender, e);
        if (e.Handled || e.Key != Key.Escape)
        {
            return;
        }

        // By Tag, not by name: the box sits inside the PageHeader user control's own name scope.
        if (sender is FrameworkElement { DataContext: RowsPageViewModel page }
            && e.OriginalSource is TextBox box
            && Equals(box.Tag, "SearchBox")
            && page.SearchText.Length > 0)
        {
            page.ClearSearchCommand.Execute(null);
            e.Handled = true;
        }
    }
}
