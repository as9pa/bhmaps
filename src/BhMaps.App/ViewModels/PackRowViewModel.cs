using System.Collections.ObjectModel;
using System.Windows.Media;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Status;
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

    private bool _realised;

    /// <summary>The counts on their own, which is the half of the line that never changes.</summary>
    private readonly string _counts;

    /// <summary>When the pack was last applied (spec 8), null for a pack never applied. Handed to the row by the
    /// page, because a row reads no settings of its own.</summary>
    private DateTimeOffset? _lastApplied;

    public PackRowViewModel(Pack pack, IReadOnlyList<MapEntry> maps)
    {
        Pack = pack;
        Maps = maps;
        MapCount = pack.Folders.Count(f => !f.Name.Equals(BackgroundsFolder, StringComparison.OrdinalIgnoreCase));
        BackgroundCount = pack.FindFolder(BackgroundsFolder)?.Files.Count ?? 0;
        _counts = $"{Plural(MapCount, "map")}, {Plural(BackgroundCount, "background")}";
        Previews = [.. maps.Take(MaxPreviews).Select(map => new PackPreviewTileViewModel(map))];
        MenuItems = [];
    }

    public Pack Pack { get; }

    public string Name => Pack.Name;

    /// <summary>The catalog maps the pack has files for, in catalog order. The strip's order, and the first of
    /// them is the lead.</summary>
    public IReadOnlyList<MapEntry> Maps { get; }

    /// <summary>The pack's folders other than Backgrounds.</summary>
    public int MapCount { get; }

    /// <summary>Files in the pack's Backgrounds folder.</summary>
    public int BackgroundCount { get; }

    /// <summary>The line under the name, as in "1 map, 3 backgrounds", with ", applied 2 min ago" after it once
    /// the pack has been applied (spec 8).</summary>
    public string CountsText =>
        _lastApplied is { } stamp
            ? $"{_counts}, applied {RelativeTime.Describe(stamp, DateTimeOffset.Now)}"
            : _counts;

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
    [NotifyPropertyChangedFor(nameof(ShowMore))]
    public partial int Overflow { get; set; }

    /// <summary>Addendum E's "+58": every map the strip is not showing, whether it was cut by the width or never
    /// given a tile.</summary>
    public string MoreText => $"+{Remaining}";

    public bool ShowMore => Remaining > 0;

    private int Remaining => Math.Max(0, Maps.Count - Math.Max(0, Previews.Count - Overflow));

    public void SetMenu(IReadOnlyList<TileMenuCommand> items) => MenuItems = items;

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

    /// <summary>The stamp the page read out of the settings, or null for a pack never applied.</summary>
    public void SetLastApplied(DateTimeOffset? stamp)
    {
        _lastApplied = stamp;
        RefreshCountsText();
    }

    /// <summary>Reads the words again from the same stamp: "just now" becomes "2 min ago" while the page is open.</summary>
    public void RefreshCountsText() => OnPropertyChanged(nameof(CountsText));

    /// <summary>"1 map" but "0 maps" and "3 maps". Shared with the page's confirm text, which counts files.</summary>
    public static string Plural(int count, string noun) => count == 1 ? $"{count} {noun}" : $"{count} {noun}s";
}
