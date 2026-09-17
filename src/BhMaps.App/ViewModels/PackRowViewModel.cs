using System.Collections.ObjectModel;
using System.Windows.Media;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BhMaps.App.ViewModels;

/// <summary>One row of the Packs list (addendum E): the pack's face as well as its name. A lead thumbnail of its
/// first map put together, the counts, a strip of the maps it touches, and the dots menu that took three of the
/// row's four buttons (q7). An ObservableObject now, because the pictures land after the row is built.</summary>
public sealed partial class PackRowViewModel : ObservableObject
{
    /// <summary>Addendum E's lead thumbnail, in device independent units because the markup sets Width and
    /// Height from them.</summary>
    public const double LeadWidth = 128;

    public const double LeadHeight = 72;

    /// <summary>Addendum E's strip tile.</summary>
    public const double PreviewWidth = 96;

    public const double PreviewHeight = 54;

    /// <summary>The size both are composed at, in pixels. Twice the lead and exactly 16:9, so the lead and the
    /// strip's first tile are one render and one decode rather than two, and both are still sharp on a high DPI
    /// screen.</summary>
    public const int ComposeWidth = 256;

    public const int ComposeHeight = 144;

    /// <summary>How many strip tiles a row builds at most. At 96 px plus the 8 px gap a 1920 px window fits about
    /// twelve, so sixteen covers the widest window with room to spare, and a pack that touches 67 maps costs
    /// sixteen elements per row rather than 67. <see cref="MoreText" /> counts the maps that were given no tile
    /// as well as the ones the strip had no room for, so the number the row shows is the true remainder.</summary>
    public const int MaxPreviews = 16;

    /// <summary>The folder a pack keeps its background images in. Every other folder is a map.</summary>
    private const string BackgroundsFolder = "Backgrounds";

    /// <summary>The second sentence for a pack whose bytes are in no map.</summary>
    private const string NotInGameText = "Not in game.";

    /// <summary>The second sentence for the Default pack, which is the floor every Reset to default lands on.</summary>
    private const string DefaultWhereText = "What Reset puts back.";

    /// <summary>The last sentence while the eye is off (2.8).</summary>
    private const string HiddenText = "Hidden from lists.";

    private bool _realised;

    /// <summary>The counts on their own, which is the half of the line that never changes.</summary>
    private readonly string _counts;

    /// <summary>How many catalog map folders currently hold this pack's bytes (3.0). Handed to the row by the
    /// page, because a row reads no applied record of its own.</summary>
    private int _appliedMaps;

    public PackRowViewModel(Pack pack, IReadOnlyList<MapEntry> maps)
    {
        Pack = pack;
        Maps = maps;
        (MapCount, BackgroundCount) = Counts(pack);
        _counts = HoldsText(MapCount, BackgroundCount);
        Previews = [.. maps.Take(MaxPreviews).Select(map => new PackPreviewTileViewModel(map))];
        MenuItems = [];
    }

    public Pack Pack { get; }

    public string Name => Pack.Name;

    /// <summary>The game's own art, which every Reset to default restores from. It has no Delete line and no eye:
    /// the lists cannot be asked to do without it.</summary>
    public bool IsDefault => Name.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>The catalog maps the pack has files for, in catalog order. The strip's order, and the first of
    /// them is the lead.</summary>
    public IReadOnlyList<MapEntry> Maps { get; }

    /// <summary>The pack's folders other than Backgrounds.</summary>
    public int MapCount { get; }

    /// <summary>Files in the pack's Backgrounds folder.</summary>
    public int BackgroundCount { get; }

    /// <summary>2.8: whether the pack is kept out of the Backgrounds and Platforms lists. A view preference the
    /// page reads out of the settings and hands the row; the eye on the row is what flips it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateLine))]
    public partial bool IsHidden { get; set; }

    /// <summary>The first line under the name (3.0): what the pack holds, as in "25 maps, 27 backgrounds.".
    /// Two lines rather than one sentence, because a narrow column broke the joined form mid-sentence.</summary>
    public string CountsLine => _counts;

    /// <summary>The second line under the name (3.0): where the pack's art actually is, then "Hidden from lists."
    /// when the eye is off (2.8). The time of the last Apply all is gone from it; where the art sits answers the
    /// same question and keeps answering it after a reset.</summary>
    public string StateLine
    {
        get
        {
            var text = WhereText(Name, _appliedMaps);
            return IsHidden ? $"{text} {HiddenText}" : text;
        }
    }

    /// <summary>The strip, capped at <see cref="MaxPreviews" />. Filled in when the row comes on screen.</summary>
    public ObservableCollection<PackPreviewTileViewModel> Previews { get; }

    /// <summary>The dots menu (addendum E, q7). Set by the page straight after construction, because each line
    /// carries this row as its command parameter and the row does not exist until its constructor has run.</summary>
    public IReadOnlyList<TileMenuCommand> MenuItems { get; private set; }

    /// <summary>The pack's first map, put together. Null until the row is realised, and null afterwards for a
    /// pack with nothing to draw, which leaves the tile colour showing.</summary>
    [ObservableProperty]
    public partial ImageSource? Lead { get; set; }

    /// <summary>How many of the strip's tiles the row had no width for. Written by StripPanel through a
    /// OneWayToSource binding in the row template.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MoreText))]
    [NotifyPropertyChangedFor(nameof(MoreName))]
    [NotifyPropertyChangedFor(nameof(ShowMore))]
    public partial int Overflow { get; set; }

    /// <summary>Addendum E's "+58", as 3.0 words it: every map the strip is not showing, whether it was cut by
    /// the width or never given a tile. A link now, so what it does is on it.</summary>
    public string MoreText => $"+{Remaining} more";

    /// <summary>The link's name for the screen reader, which cannot see the strip the "+58" is counting.</summary>
    public string MoreName => $"{Remaining} more in {Name}";

    public bool ShowMore => Remaining > 0;

    private int Remaining => Math.Max(0, Maps.Count - Math.Max(0, Previews.Count - Overflow));

    /// <summary>The menu again, with a property change, because a line's words can change while the row is on
    /// screen: hiding a pack turns Hide from lists into Show in lists (2.8).</summary>
    public void SetMenu(IReadOnlyList<TileMenuCommand> items)
    {
        MenuItems = items;
        OnPropertyChanged(nameof(MenuItems));
    }

    /// <summary>True the first time it is called and false ever after, so the row reads its files once. The
    /// row template's Loaded fires again every time the list scrolls it back into view.</summary>
    public bool BeginRealise()
    {
        if (_realised)
        {
            return false;
        }

        _realised = true;
        return true;
    }

    /// <summary>The count the page read out of the applied record, zero for a pack with nothing in the game.</summary>
    public void SetAppliedMaps(int count)
    {
        _appliedMaps = count;
        RefreshLines();
    }

    /// <summary>Reads the state line again, for a caller that changed something the line names.</summary>
    public void RefreshLines() => OnPropertyChanged(nameof(StateLine));

    /// <summary>What a pack holds: the folders that are maps and the files in Backgrounds. Shared with the pack's
    /// own page, whose subtitle carries the same sentence (3.0).</summary>
    public static (int Maps, int Backgrounds) Counts(Pack pack) => (
        pack.Folders.Count(f => !f.Name.Equals(BackgroundsFolder, StringComparison.OrdinalIgnoreCase)),
        pack.FindFolder(BackgroundsFolder)?.Files.Count ?? 0);

    /// <summary>The two sentences joined, for the pack's own page, whose subtitle has the width for one line.
    /// The row shows the same two as <see cref="CountsLine" /> and <see cref="StateLine" /> (3.0).</summary>
    public static string Describe(string packName, int mapCount, int backgroundCount, int appliedMaps) =>
        $"{HoldsText(mapCount, backgroundCount)} {WhereText(packName, appliedMaps)}";

    /// <summary>"1 map" but "0 maps" and "3 maps". Shared with the page's confirm text, which counts files.</summary>
    public static string Plural(int count, string noun) => count == 1 ? $"{count} {noun}" : $"{count} {noun}s";

    /// <summary>The first sentence, as in "25 maps, 27 backgrounds."</summary>
    private static string HoldsText(int mapCount, int backgroundCount) =>
        $"{Plural(mapCount, "map")}, {Plural(backgroundCount, "background")}.";

    /// <summary>The second sentence: where the pack's art is. The Default pack is never applied as such, so
    /// instead of a count it says what it is for.</summary>
    private static string WhereText(string packName, int appliedMaps) =>
        packName.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase)
            ? DefaultWhereText
            : appliedMaps == 0 ? NotInGameText : $"On {Plural(appliedMaps, "map")}.";
}
