using BhMaps.Core.Settings;

namespace BhMaps.App.ViewModels;

/// <summary>A page that draws tiles at one of the three sizes (wireframe 8.2). The keyboard and the wheel reach
/// the size through this rather than through each page's own type, so <see cref="Behaviors.SizeShortcuts"/> is
/// written once for every page that has a size picker.</summary>
public interface ITileSized
{
    TileSize TileSize { get; set; }
}
