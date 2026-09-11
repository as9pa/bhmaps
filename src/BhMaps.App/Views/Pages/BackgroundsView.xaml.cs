using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BhMaps.App.Views.Controls;

namespace BhMaps.App.Views.Pages;

public partial class BackgroundsView : UserControl
{
    public BackgroundsView()
    {
        InitializeComponent();
    }

    /// <summary>Spec section 13's menu key and Shift+F10, on whatever has focus. TileMenus takes those two keys
    /// and leaves every other one alone.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e) => TileMenus.OnPreviewKeyDown(sender, e);

    /// <summary>The menu button drawn on a tile. The button carries no menu of its own, so TileMenus walks up to
    /// the tile that does.</summary>
    private void OnTileMenuButton(object sender, RoutedEventArgs e) => TileMenus.OpenFor(sender);
}
