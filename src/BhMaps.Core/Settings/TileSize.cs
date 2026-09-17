namespace BhMaps.Core.Settings;

/// <summary>3.0: how big a page draws its tiles. Three named sizes rather than the old 2 to 10 slider, because
/// the slider asked the owner to pick a number for something they judge by eye, and every step between two
/// sizes was a step nobody had a reason to choose. The widths themselves live in
/// <see cref="BhMaps.Core.Layout.TileSizes"/>, so a grid page and a rows page can read the same size differently.</summary>
public enum TileSize
{
    Large,
    Medium,
    Small,
}
