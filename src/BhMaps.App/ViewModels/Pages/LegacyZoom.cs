using BhMaps.Core.Settings;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>The bridge between the 2.8 zoom sliders the pack page and the two rows pages still carry and the
/// tile size the settings file holds now. Part 2 gives those pages the size picker the Maps page has, and takes
/// this away with the sliders; until then a slider that lands anywhere in a size's band saves that size, and a
/// page opens on the middle of the band it saved.</summary>
internal static class LegacyZoom
{
    public static int ToGridZoom(TileSize size) => size switch
    {
        TileSize.Large => 3,
        TileSize.Small => 9,
        _ => 6,
    };

    public static TileSize FromGridZoom(int zoom) =>
        zoom <= 4 ? TileSize.Large : zoom <= 7 ? TileSize.Medium : TileSize.Small;

    /// <summary>A rows page's zoom is a thumbnail height, so it runs the other way round from a grid's.</summary>
    public static int ToRowZoom(TileSize size) => size switch
    {
        TileSize.Large => 5,
        TileSize.Small => 2,
        _ => 4,
    };

    public static TileSize FromRowZoom(int zoom) =>
        zoom <= 3 ? TileSize.Small : zoom == 4 ? TileSize.Medium : TileSize.Large;
}
