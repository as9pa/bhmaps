using System.Windows.Input;

namespace BhMaps.App.ViewModels;

/// <summary>What a line of a tile menu is. An ordinary line runs a command; a Header is the muted first line that
/// names what the menu is about (spec 4.1); a Separator is a rule. The style in Controls.xaml draws all three.</summary>
public enum TileMenuKind
{
    Item,
    Header,
    Separator,
}

/// <summary>One line of a tile's menu: the words spec section 11 and spec 4.1 give it, the command it runs, and,
/// for a flyout, the lines behind it. A record, so a tile rebuilding its menu costs allocations and no
/// bookkeeping. Children null rather than empty on an ordinary line, because an empty ItemsSource still draws a
/// submenu arrow.</summary>
public sealed record TileMenuCommand(
    string Text,
    ICommand? Command,
    IReadOnlyList<TileMenuCommand>? Children = null,
    bool IsEnabled = true,
    string? ToolTip = null,
    TileMenuKind Kind = TileMenuKind.Item)
{
    /// <summary>The muted first line naming the file, the map or the count (spec 4.1, spec 4.3).</summary>
    public static TileMenuCommand Header(string text) =>
        new(text, null, IsEnabled: false, Kind: TileMenuKind.Header);

    public static TileMenuCommand Separator() =>
        new("", null, IsEnabled: false, Kind: TileMenuKind.Separator);

    /// <summary>A line that opens a submenu rather than running. Disabled when it has no lines, because a flyout
    /// that opens on nothing reads as the menu being broken.</summary>
    public static TileMenuCommand Flyout(string text, IReadOnlyList<TileMenuCommand> children) =>
        new(text, null, children.Count > 0 ? children : null, children.Count > 0);
}
