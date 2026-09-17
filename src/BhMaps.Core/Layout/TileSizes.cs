using BhMaps.Core.Settings;

namespace BhMaps.Core.Layout;

/// <summary>What each <see cref="TileSize"/> is worth in pixels. The numbers live here rather than on a page,
/// so the Maps grid and the pack grid cannot drift apart, and so a target width is a target: the justified
/// layout rounds it up or down to whatever makes the row come out even.</summary>
public static class TileSizes
{
    /// <summary>The gutter between tiles, and between rows of them. One number for every page (wireframe 8.1).</summary>
    public const double Gap = 12;

    /// <summary>A card on a grid page: Maps and the pack detail.</summary>
    public static double CardWidth(TileSize size) => size switch
    {
        TileSize.Large => 300,
        TileSize.Small => 120,
        _ => 200,
    };

    /// <summary>A tile in a row strip: Backgrounds and Platforms. Narrower than a card, because a row's height
    /// is what the size is really choosing there.</summary>
    public static double RowTileWidth(TileSize size) => size switch
    {
        TileSize.Large => 224,
        TileSize.Small => 96,
        _ => 150,
    };
}
