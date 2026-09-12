namespace BhMaps.Core.Settings;

public sealed record AppSettings(
    string GamePath,
    string LibraryPath,
    bool FirstRunDone,
    int MapsZoom = 6,
    int BackgroundsZoom = 2,
    int PackZoom = 5,
    bool WelcomeDone = false,
    int PlatformsZoom = 3,
    IReadOnlyDictionary<string, DateTimeOffset>? PackLastApplied = null)
{
    public const string DefaultGamePath = @"C:\Program Files (x86)\Steam\steamapps\common\Brawlhalla\mapArt";

    /// <summary>Columns a grid can be zoomed to (spec 3.1). One range for every page, so a value hand-edited or
    /// carried over from another page is never clamped away by a narrower one.</summary>
    public const int MinZoom = 2;
    public const int MaxZoom = 10;

    /// <summary>Steps a rows page can be zoomed to (addendum B, q1 answered dense). A row's zoom is a thumbnail
    /// height, not a column count: 1 is 48 px and 5 is 128 px. Backgrounds starts at 2 (56 px, about twelve rows
    /// on a 1080p window) and Platforms at 3 (72 px, because platform shapes need more height than a picture
    /// does). The grid pages keep MinZoom to MaxZoom. The height is stored under backgroundsRowZoom; the old
    /// backgroundsZoom (a 2.0 tile size, then a 2.1 column count) is dropped on load, never read as a height.</summary>
    public const int MinRowZoom = 1;
    public const int MaxRowZoom = 5;

    /// <summary>Spec 7.7: the library defaults to BhMaps inside Documents. Computed rather than a literal, so it
    /// follows a redirected Documents folder and never names one machine's user. Nothing creates the folder here;
    /// the first write into it does.</summary>
    public static string DefaultLibraryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BhMaps");

    public static AppSettings Default { get; } = new(DefaultGamePath, DefaultLibraryPath, FirstRunDone: false);

    /// <summary>Spec 8: when each pack was last applied, so the packs sort by the one in use. Read through
    /// <see cref="LastApplied"/>, which is never null, so no caller has to know a settings file that never
    /// carried a stamp from one that carried an empty object.</summary>
    public IReadOnlyDictionary<string, DateTimeOffset> LastApplied => PackLastApplied ?? EmptyStamps;

    private static readonly IReadOnlyDictionary<string, DateTimeOffset> EmptyStamps =
        new Dictionary<string, DateTimeOffset>();

    /// <summary>Properties from settings.json this version does not know. Written back untouched.</summary>
    public System.Text.Json.Nodes.JsonObject? Unknown { get; init; }
}
