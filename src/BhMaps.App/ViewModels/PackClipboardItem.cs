using BhMaps.App.ViewModels.Pages;
using BhMaps.Core.Model;

namespace BhMaps.App.ViewModels;

/// <summary>What Ctrl+C or Ctrl+X took: the pack it came out of, the tile, and whether it was cut.</summary>
public sealed record PackClipboardItem(Pack Source, PackTileViewModel Tile, bool Cut);
