using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BhMaps.App.ViewModels.Pages;

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
        if (sender is FrameworkElement { DataContext: PackTileViewModel tile })
        {
            Page?.Open(tile);
        }
    }

    private void Tiles_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Page?.OpenSelected();
            e.Handled = true;
        }
    }
}
