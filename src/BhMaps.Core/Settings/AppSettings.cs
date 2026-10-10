namespace BhMaps.Core.Settings;

public sealed record AppSettings(
    string GamePath,
    string LibraryPath,
    bool FirstRunDone,
    TileSize MapsTileSize = TileSize.Medium,
    TileSize BackgroundsTileSize = TileSize.Medium,
    TileSize PackTileSize = TileSize.Medium,
    bool WelcomeDone = false,
    TileSize PlatformsTileSize = TileSize.Medium,
    IReadOnlyDictionary<string, DateTimeOffset>? PackLastApplied = null,
    bool BackgroundsShowPictures = false,
    bool PlatformPreviewIsolate = false,
    bool CheckForUpdates = true,

    /// <summary>Spec 7.2: when the last start-up check ran, so the next one waits 24 h. Null means never.</summary>
    DateTimeOffset? LastUpdateCheck = null,

    /// <summary>Spec 7.3: the tag of a release the user waved away on the top bar. A later release has a
    /// different tag, so the line comes back on its own.</summary>
    string? DismissedUpdate = null,

    /// <summary>2.8: the packs kept out of the Backgrounds and Platforms lists. Pack names, and a view preference
    /// of this machine: nothing is written into the pack, so the same library on another machine shows every
    /// pack. Read through <see cref="HiddenPacks"/>, which is never null.</summary>
    IReadOnlyList<string>? HiddenPackNames = null,

    /// <summary>3.1: the packs whose "N files change nothing in game" note was dismissed, and the count it was
    /// dismissed at. A different count is a different note, so the line comes back on its own the way
    /// <see cref="DismissedUpdate" /> does. Read through <see cref="DismissedTransparent" />, which is never
    /// null.</summary>
    IReadOnlyDictionary<string, int>? DismissedTransparentNotes = null,

    /// <summary>3.2 P1 and P2: the Both / Platforms / Backgrounds switch. One setting shared by Packs, a pack's page
    /// and Maps, so flipping it on one page flips it on all three.</summary>
    PreviewMode PreviewMode = PreviewMode.Both,

    /// <summary>3.9.3: the one-time rename of an old "My Backgrounds" pack to Custom Pack has run (or found nothing
    /// to rename), so a pack the user names My Backgrounds later is left alone.</summary>
    bool MyBackgroundsMigrated = false)
{
    public const string DefaultGamePath = @"C:\Program Files (x86)\Steam\steamapps\common\Brawlhalla\mapArt";

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

    /// <summary>2.8: the hidden packs, never null, so a settings file that never carried the key reads the same
    /// as one that carried an empty array.</summary>
    public IReadOnlyCollection<string> HiddenPacks => HiddenPackNames ?? EmptyHidden;

    private static readonly IReadOnlyList<string> EmptyHidden = [];

    /// <summary>Whether this pack is kept out of the lists. Names are compared the way the file system compares
    /// them, so a pack hidden as "Dark" is still hidden after the folder is renamed to "dark".</summary>
    public bool IsHidden(string packName) =>
        HiddenPacks.Contains(packName, StringComparer.OrdinalIgnoreCase);

    /// <summary>3.1: the dismissed transparent-file notes, never null, so a settings file that never carried the
    /// key reads the same as one that carried an empty object.</summary>
    public IReadOnlyDictionary<string, int> DismissedTransparent => DismissedTransparentNotes ?? EmptyDismissed;

    private static readonly IReadOnlyDictionary<string, int> EmptyDismissed =
        new Dictionary<string, int>();

    /// <summary>Whether this pack's note was dismissed at exactly this count. A note dismissed at three files is
    /// back as soon as the pack holds four, which is what makes this safe to keep forever.</summary>
    public bool IsTransparentNoteDismissed(string packName, int count) =>
        DismissedTransparent.TryGetValue(packName, out var dismissed) && dismissed == count;

    /// <summary>3.6 P1: a copy with every per-pack setting of <paramref name="oldName"/> moved to
    /// <paramref name="newName"/>: the hidden list, the last-applied stamp and the dismissed transparent note.
    /// Names are matched ignoring case; a setting the file never carried stays absent.</summary>
    public AppSettings WithPackRenamed(string oldName, string newName) =>
        this with
        {
            HiddenPackNames = HiddenPackNames is null
                ? null
                : [.. HiddenPackNames
                    .Select(n => n.Equals(oldName, StringComparison.OrdinalIgnoreCase) ? newName : n)
                    .Distinct(StringComparer.OrdinalIgnoreCase)],
            PackLastApplied = RenameKey(PackLastApplied, oldName, newName),
            DismissedTransparentNotes = RenameKey(DismissedTransparentNotes, oldName, newName),
        };

    private static IReadOnlyDictionary<string, T>? RenameKey<T>(
        IReadOnlyDictionary<string, T>? source, string oldName, string newName)
    {
        if (source is null)
        {
            return null;
        }

        var renamed = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in source.Where(p => !p.Key.Equals(oldName, StringComparison.OrdinalIgnoreCase)))
        {
            renamed[key] = value;
        }

        // The renamed pack's own value wins over anything left behind under the new name.
        foreach (var (_, value) in source.Where(p => p.Key.Equals(oldName, StringComparison.OrdinalIgnoreCase)))
        {
            renamed[newName] = value;
        }

        return renamed;
    }

    /// <summary>Properties from settings.json this version does not know. Written back untouched.</summary>
    public System.Text.Json.Nodes.JsonObject? Unknown { get; init; }
}
