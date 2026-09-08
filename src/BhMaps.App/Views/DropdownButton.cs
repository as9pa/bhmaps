using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace BhMaps.App.Views;

/// <summary>Opens a button's ContextMenu as a dropdown under the button. Shared by the folder cards and the detail rows.</summary>
public static class DropdownButton
{
    public static void Open(object sender)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }
}
