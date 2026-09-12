using System.Windows.Media;
using BhMaps.Core.Maps;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BhMaps.App.ViewModels;

/// <summary>One map in a Packs row's preview strip (addendum E): the pack's art for that map put together, drawn
/// at 96 x 54. No commands and no menu: the whole row is one click that opens the pack, and the strip is there to
/// say what is in it, not to be operated.</summary>
public sealed partial class PackPreviewTileViewModel : ObservableObject
{
    public PackPreviewTileViewModel(MapEntry map)
    {
        Map = map;
    }

    public MapEntry Map { get; }

    /// <summary>The tile's tooltip and its automation name. There is no caption: at 96 px wide a map name would
    /// be three letters and an ellipsis.</summary>
    public string DisplayName => Map.DisplayName;

    /// <summary>Null until the composite is ready. Always frozen, because it is drawn off the UI thread.</summary>
    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }
}
