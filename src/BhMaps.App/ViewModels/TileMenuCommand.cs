using System.Windows.Input;

namespace BhMaps.App.ViewModels;

/// <summary>One line of a tile's menu: the words spec section 11 gives it and the command it runs. A record, so a
/// tile rebuilding its menu when the ticked count changes costs five allocations and no bookkeeping.</summary>
public sealed record TileMenuCommand(string Text, ICommand Command);
