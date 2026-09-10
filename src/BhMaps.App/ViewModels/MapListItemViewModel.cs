using BhMaps.Core.Maps;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BhMaps.App.ViewModels;

/// <summary>One row of the sidebar map list (spec 7.1): the map's name, its summary state tag, and whether it is
/// one of the maps a multi-map action applies to.</summary>
public partial class MapListItemViewModel : ObservableObject
{
    public MapListItemViewModel(MapEntry map, MapStatus? status)
    {
        FolderName = map.FolderName;
        DisplayName = map.DisplayName;
        StateText = status?.Text ?? "";

        // Missing is the only coloured state (D8), which is exactly when the row wears the coloured tag.
        IsMissing = status?.IsColoured ?? false;
    }

    public string FolderName { get; }

    /// <summary>The map's in-game name, or its folder name in the no-level-data fallback (spec 3.6).</summary>
    public string DisplayName { get; }

    /// <summary>The tag's text: "Default", the pack names, "Custom" or "Missing". Empty when the scan produced
    /// no status for this map, and then no tag is drawn.</summary>
    public string StateText { get; }

    public bool IsMissing { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
