using BhMaps.Core.LevelData;
using BhMaps.Core.Model;

namespace BhMaps.Core.Maps;

/// <summary>One set chip the UI shows: the name the set has in the level data and the label drawn on the chip.</summary>
public sealed record UiSet(string Name, string Label);

/// <summary>One map: a mapArt folder with the levels that point at it, ranked so one of them names the map.
/// <see cref="ThumbnailFiles"/> are the map-select pictures this map owns outright, in level order: a folder
/// owns every file its included levels name that no other folder's levels also name, so a folder whose levels
/// name two or three different pictures owns all of them. <see cref="Candidates"/> holds every file its levels
/// name, owned or not, <see cref="ThumbnailFile"/> is the first owned one for callers that want a single
/// name, and <see cref="LevelFor"/> answers which level a file is the picture of.
/// 3.2: the same record is also one card, a playable layout. <see cref="Layout"/> is the level the card is,
/// null in the fallback, and <see cref="Key"/> tells cards apart. A layout card of a folder with two or more
/// layouts carries its own name, sets, base level and thumbnail, and its own platform files in
/// <see cref="LayoutFiles"/>; <see cref="PlatformFiles"/> and <see cref="BackgroundSlots"/> stay the whole
/// folder's, because the art folder, the background and a pack's files are per folder.</summary>
public sealed record MapEntry(
    string FolderName,
    string DisplayName,
    LevelDesc BaseLevel,
    IReadOnlyList<LevelDesc> Levels,
    IReadOnlyList<string> Sets,
    IReadOnlyList<string> BackgroundSlots,
    IReadOnlyList<string> PlatformFiles,
    IReadOnlyList<string>? OwnedThumbnails = null,
    IReadOnlyList<string>? ThumbnailCandidates = null,
    IReadOnlyDictionary<string, LevelDesc>? ThumbnailLevels = null,
    string? Layout = null,
    IReadOnlyList<string>? LayoutPlatformFiles = null)
{
    /// <summary>3.2: the card's identity: the layout's level name, or the folder name in the fallback.</summary>
    public string Key => Layout ?? FolderName;

    /// <summary>3.2: the platform files this layout draws, which is the whole folder's list for a folder with
    /// one layout.</summary>
    public IReadOnlyList<string> LayoutFiles => LayoutPlatformFiles ?? PlatformFiles;

    /// <summary>The map-select pictures this map owns outright, in level order, empty when it owns none.</summary>
    public IReadOnlyList<string> ThumbnailFiles => OwnedThumbnails ?? [];

    /// <summary>Every map-select picture this map's included levels name, empty when they name none.</summary>
    public IReadOnlyList<string> Candidates => ThumbnailCandidates ?? [];

    /// <summary>The level the picture <paramref name="fileName"/> belongs to: a folder's files are one per
    /// level, so the small level's picture must be rendered from the small level and not from the map. Falls
    /// back to <see cref="BaseLevel"/> for a file no level of this map names, which is what the fallback
    /// catalog and a map built without level data have.</summary>
    public LevelDesc LevelFor(string fileName) =>
        ThumbnailLevels is not null && ThumbnailLevels.TryGetValue(fileName, out var level) ? level : BaseLevel;

    /// <summary>The first picture this map owns, or null when it owns none.</summary>
    public string? ThumbnailFile => ThumbnailFiles.Count > 0 ? ThumbnailFiles[0] : null;
}

/// <summary>Folds the game's level data onto its mapArt folders. Folders no included level points at are not
/// maps: they stay out of the catalog, and reset-all and pack operations reach them through the game tree.</summary>
public sealed class MapCatalog
{
    public static readonly string[] RankedSetNames = ["Ranked1v1", "Ranked2v2", "Tournament1v1", "Tournament2v2"];
    public static readonly string[] StandardSetNames = ["Standard1v1", "Standard2v2", "Tournament1v1", "Tournament2v2"];

    /// <summary>3.0: the set every level a game mode uses belongs to, minigame or not.</summary>
    public const string MinigameSetName = "GameModeAll";

    /// <summary>3.0: the word the chip and the sets sentence both use for <see cref="MinigameSetName"/>.</summary>
    public const string MinigameLabel = "Minigames";

    public static readonly string[] MiniGameDisplayNames =
        ["Catch Bombs", "Color Platforms", "Demon Island CTF", "Beachbrawl Arena"];

    /// <summary>Folders that are never maps: the shared background library, and theme folders holding seasonal
    /// art other maps borrow through "../". In the fallback nothing else tells them apart from a map.</summary>
    public static readonly string[] HiddenFolderNames = ["Backgrounds", "Halloween", "Snow", "Test"];

    private static readonly string[] SkippedPrefixes = ["Small ", "Big ", "Tutorial"];

    /// <summary>Chip labels for the sets the UI shows; every other set keeps its own name.</summary>
    private static readonly Dictionary<string, string> SetLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ranked1v1"] = "Ranked 1v1",
        ["Ranked2v2"] = "Ranked 2v2",
        ["Tournament1v1"] = "Tournament 1v1",
        ["Tournament2v2"] = "Tournament 2v2",
        ["Standard1v1"] = "Standard 1v1",
        ["Standard2v2"] = "Standard 2v2",
        [MinigameSetName] = MinigameLabel,
    };

    /// <summary>3.0: the words <see cref="SetsSentence"/> may use, in the order it says them. Several game sets
    /// share one word, because a player picks "Standard", not "Standard3v3" and "StandardBig" apart.</summary>
    private static readonly (string Word, string[] Sets)[] SetWords =
    [
        ("Ranked 1v1", ["Ranked1v1"]),
        ("Ranked 2v2", ["Ranked2v2"]),
        ("Tournament", ["Tournament1v1", "Tournament2v2"]),
        ("Standard", ["StandardAll", "Standard1v1", "Standard2v2", "Standard3v3", "StandardFFA", "StandardBig"]),
        ("Experimental", ["Experimental1v1"]),
        (MinigameLabel, [MinigameSetName]),
    ];

    /// <summary>3.0: the sets a player picks a map from, which is every set the sentence above names but the
    /// mode set. Exact names, not prefixes: the game's list also holds one-map rotation sets such as
    /// "Ranked3v3BrawlballOGMapOnly", and a map in one of those is still a map only a mode uses.</summary>
    private static readonly HashSet<string> PlayableSetNames = new(
        SetWords.Where(w => w.Word != MinigameLabel).SelectMany(w => w.Sets),
        StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, MapEntry> byFolder;
    private readonly Dictionary<string, MapEntry> byLayout;

    private MapCatalog(
        IReadOnlyList<MapEntry> maps, IReadOnlyList<MapEntry> layouts, IReadOnlyList<string> uiSetNames, bool hasLevelData)
    {
        Maps = maps;
        Layouts = layouts;
        UiSetNames = uiSetNames;
        UiSets = uiSetNames.Select(n => new UiSet(n, LabelFor(n))).ToList();
        HasLevelData = hasLevelData;
        byFolder = new Dictionary<string, MapEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in maps)
        {
            byFolder[map.FolderName] = map;
        }

        byLayout = new Dictionary<string, MapEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var layout in layouts)
        {
            byLayout[layout.Key] = layout;
        }
    }

    /// <summary>One entry per art folder, sorted by display name, ordinal ignore case. Everything that writes,
    /// resets or records art works on these, because art is organised per folder.</summary>
    public IReadOnlyList<MapEntry> Maps { get; }

    /// <summary>3.2: one entry per playable layout, the cards the Maps page shows, sorted like
    /// <see cref="Maps"/>. A folder with one layout is the same entry in both lists; the fallback's layouts are
    /// its folders.</summary>
    public IReadOnlyList<MapEntry> Layouts { get; }

    /// <summary>The chip sets in the order the UI shows them. Empty in the fallback.</summary>
    public IReadOnlyList<string> UiSetNames { get; }

    /// <summary>The same sets as <see cref="UiSetNames"/>, each paired with its chip label.</summary>
    public IReadOnlyList<UiSet> UiSets { get; }

    /// <summary>False when the catalog came from folder names alone. Consumers must check it before trying to
    /// compose a preview.</summary>
    public bool HasLevelData { get; }

    public MapEntry? ByFolder(string folderName) =>
        byFolder.TryGetValue(folderName, out var map) ? map : null;

    /// <summary>3.2: the card whose <see cref="MapEntry.Key"/> is <paramref name="key"/>, or null.</summary>
    public MapEntry? ByLayout(string key) =>
        byLayout.TryGetValue(key, out var layout) ? layout : null;

    /// <summary>3.2: every card of one art folder, in <see cref="Layouts"/> order: the cards a write to that
    /// folder changes.</summary>
    public IReadOnlyList<MapEntry> LayoutsOf(string folderName) =>
        Layouts.Where(l => l.FolderName.Equals(folderName, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>3.2: the card that stands for a whole folder where one row per folder is wanted: the layout the
    /// folder entry's base level is, or the folder's first card when no card is. Null for a folder with no card.
    /// </summary>
    public MapEntry? PrimaryLayoutOf(string folderName)
    {
        var layouts = LayoutsOf(folderName);
        if (layouts.Count == 0)
        {
            return null;
        }

        var baseLevel = ByFolder(folderName)?.BaseLevel.LevelName;
        return layouts.FirstOrDefault(l => l.Key.Equals(baseLevel, StringComparison.OrdinalIgnoreCase)) ?? layouts[0];
    }

    /// <summary>3.2: the other cards of <paramref name="card"/>'s folder that also draw
    /// <paramref name="relativePath"/>, in <see cref="Layouts"/> order. Empty for a card that is not a split
    /// layout (a folder entry, a folder with one layout, the fallback), because it lists the whole folder.</summary>
    public IReadOnlyList<MapEntry> AlsoIn(MapEntry card, string relativePath)
    {
        if (card.LayoutPlatformFiles is null)
        {
            return [];
        }

        return LayoutsOf(card.FolderName)
            .Where(l => !l.Key.Equals(card.Key, StringComparison.OrdinalIgnoreCase)
                && l.LayoutFiles.Contains(relativePath, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>3.2: the quiet mark on a file another layout also draws, "also in Small World's End", or empty
    /// when no other layout draws it.</summary>
    public string AlsoInText(MapEntry card, string relativePath)
    {
        var others = AlsoIn(card, relativePath);
        return others.Count == 0 ? "" : "also in " + string.Join(", ", others.Select(l => l.DisplayName));
    }

    /// <summary>3.2: the note the background editor shows for a folder with two or more layouts, which all draw
    /// the one background: "Shared by every layout of this map: Small World's End, World's End." Empty for a
    /// folder with one layout.</summary>
    public string SharedBackgroundNote(string folderName)
    {
        var layouts = LayoutsOf(folderName);
        return layouts.Count < 2
            ? ""
            : "Shared by every layout of this map: " + string.Join(", ", layouts.Select(l => l.DisplayName)) + ".";
    }

    /// <summary>The chip label for a set name; an unlabelled set is shown under its own name.</summary>
    public static string LabelFor(string setName) =>
        SetLabels.TryGetValue(setName, out var label) ? label : setName;

    /// <summary>3.0: the sets a player would recognise, in one fixed order, for the sentence under a map's name.
    /// The game's own list holds codes nobody outside it reads ("StandardAll", "TableTopAL"), so a set with no
    /// word of its own is left out rather than spelled at the reader, and a map left with nothing reads as the
    /// one thing that is still true of it. The raw list stays on the map for the tooltip.</summary>
    public static string SetsSentence(IEnumerable<string> sets)
    {
        var names = new HashSet<string>(sets, StringComparer.OrdinalIgnoreCase);
        var words = SetWords.Where(w => w.Sets.Any(names.Contains)).Select(w => w.Word).ToList();
        return words.Count > 0 ? string.Join(", ", words) : "Other modes";
    }

    /// <summary>3.0: true for a map only a game mode uses, such as Brawlball or Horde. Those maps sit under their
    /// own chip and stay out of All, because someone looking through the maps is not looking for them. A map a
    /// mode borrows but a player can also pick, which is most of the mode set, stays an ordinary map.</summary>
    public static bool IsMinigame(MapEntry map) =>
        map.Sets.Contains(MinigameSetName, StringComparer.OrdinalIgnoreCase)
        && !map.Sets.Any(PlayableSetNames.Contains);

    public static MapCatalog Build(LevelDataModel data)
    {
        // ToDictionary would throw on a duplicate LevelName in the game's own data; the first entry wins instead.
        var included = new Dictionary<string, LevelType>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in data.Types.Where(t => t.Included))
        {
            included.TryAdd(type.LevelName, type);
        }

        string Display(LevelDesc level) =>
            included.TryGetValue(level.LevelName, out var type) && type.DisplayName.Length > 0
                ? type.DisplayName
                : level.LevelName;

        var folders = data.Levels
            .Where(l => included.ContainsKey(l.LevelName) && l.AssetDir.Length > 0 && !IsHidden(l.AssetDir))
            .GroupBy(l => l.AssetDir, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var candidates = Candidates(folders, included);
        var owned = Owned(candidates);

        var maps = folders
            .Select(g => Entry(g.Key, g.ToList(), Display, data.Sets, owned[g.Key], candidates[g.Key], included))
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var layouts = maps
            .SelectMany(m => CardsOf(m, data.Sets, Display, included))
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MapCatalog(maps, layouts, PickUiSets(data.Sets), hasLevelData: true);
    }

    /// <summary>3.2: a folder's cards. A layout is a level in a set a player picks a map from; a folder with two
    /// or more of them gets one card each, so a set chip matches the layout the game really uses (Small World's
    /// End, not World's End). A folder with one layout, and a folder with none (a minigame arena, the tutorial),
    /// stays the one card it was. Levels that are not layouts, a mode's arena or a tutorial inside a map's folder,
    /// get no card of their own: they stay on the folder entry, as they always were.</summary>
    private static IEnumerable<MapEntry> CardsOf(
        MapEntry folder,
        IReadOnlyList<LevelSet> sets,
        Func<LevelDesc, string> display,
        IReadOnlyDictionary<string, LevelType> included)
    {
        List<string> SetsOf(LevelDesc level) =>
            sets.Where(s => s.LevelNames.Contains(level.LevelName, StringComparer.OrdinalIgnoreCase))
                .Select(s => s.Name)
                .ToList();

        var layouts = folder.Levels.Where(l => SetsOf(l).Any(PlayableSetNames.Contains)).ToList();
        if (layouts.Count < 2)
        {
            yield return folder;
            yield break;
        }

        foreach (var level in layouts)
        {
            var file = included[level.LevelName].ThumbnailFile;
            IReadOnlyList<string> own = string.IsNullOrEmpty(file) ? [] : [file];

            yield return folder with
            {
                DisplayName = display(level),
                BaseLevel = level,
                Levels = [level],
                Sets = SetsOf(level),
                OwnedThumbnails = own.Where(f => folder.ThumbnailFiles.Contains(f, StringComparer.OrdinalIgnoreCase))
                    .ToList(),
                ThumbnailCandidates = own,
                ThumbnailLevels = own.ToDictionary(f => f, _ => level, StringComparer.OrdinalIgnoreCase),
                Layout = level.LevelName,
                LayoutPlatformFiles = PlatformFilesOf([level]),
            };
        }
    }

    /// <summary>Spec 3.6 fallback: one map per game folder, folder name as display name, no sets.</summary>
    public static MapCatalog FromFolders(GameTree tree)
    {
        var maps = tree.Folders
            .Where(f => !IsHidden(f.Name))
            .Select(FolderEntry)
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MapCatalog(maps, maps, Array.Empty<string>(), hasLevelData: false);
    }

    private static MapEntry FolderEntry(GameFolder folder)
    {
        var baseLevel = new LevelDesc(folder.Name, folder.Name, new CameraBounds(0, 0, 0, 0), [], []);
        return new MapEntry(
            folder.Name,
            folder.Name,
            baseLevel,
            [baseLevel],
            Array.Empty<string>(),
            Array.Empty<string>(),
            folder.Files.Select(f => Path.Combine(folder.Name, f.Name)).ToList(),
            OwnedThumbnails: null);
    }

    /// <summary>The map-select pictures each folder owns, keyed by folder name, in the order its levels name
    /// them. A folder owns a candidate when it is the only folder naming it: a file two folders name belongs to
    /// neither, because writing it would change the other map's thumbnail. A folder naming several files of its
    /// own owns all of them, which is the case the ranked "Small X" levels make.</summary>
    private static Dictionary<string, IReadOnlyList<string>> Owned(
        Dictionary<string, IReadOnlyList<string>> candidates)
    {
        // Candidates are already distinct within a folder, so a count above one always means a second folder.
        var owners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in candidates.Values.SelectMany(files => files))
        {
            owners[file] = owners.GetValueOrDefault(file) + 1;
        }

        return candidates.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.Where(file => owners[file] == 1).ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every map-select picture each folder's included levels name, keyed by folder name. A folder whose
    /// levels name none gets an empty list.</summary>
    private static Dictionary<string, IReadOnlyList<string>> Candidates(
        IReadOnlyList<IGrouping<string, LevelDesc>> folders,
        IReadOnlyDictionary<string, LevelType> included) =>
        folders.ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<string>)g
                .Select(l => included[l.LevelName].ThumbnailFile)
                .Where(f => !string.IsNullOrEmpty(f))
                .Select(f => f!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            StringComparer.OrdinalIgnoreCase);

    private static MapEntry Entry(
        string folder,
        IReadOnlyList<LevelDesc> levels,
        Func<LevelDesc, string> display,
        IReadOnlyList<LevelSet> sets,
        IReadOnlyList<string> owned,
        IReadOnlyList<string> candidates,
        IReadOnlyDictionary<string, LevelType> included)
    {
        var levelNames = levels.Select(l => l.LevelName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var baseLevel = PickBase(folder, levels, display);

        return new MapEntry(
            folder,
            display(baseLevel),
            baseLevel,
            levels,
            sets.Where(s => s.LevelNames.Any(levelNames.Contains)).Select(s => s.Name).ToList(),
            levels
                .SelectMany(l => l.Backgrounds)
                .Select(b => b.AssetName)
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            PlatformFilesOf(levels),
            owned,
            candidates,
            ThumbnailLevels(levels, baseLevel, included),
            Layout: baseLevel.LevelName);
    }

    /// <summary>Every platform file the levels draw, as paths relative to mapArt, in level order.</summary>
    private static IReadOnlyList<string> PlatformFilesOf(IEnumerable<LevelDesc> levels) =>
        levels
            .SelectMany(l => Assets(l.Platforms).Select(a => AssetPath.Resolve(l.AssetDir, a.AssetName)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Which level each of the folder's map-select pictures is the picture of, keyed by file name. A
    /// folder holds a big and a small level that name a file each, and each file has to be rendered from its own
    /// level. When two levels name the same file the base level wins it, else the first in level order: the
    /// file can only be written once.</summary>
    private static IReadOnlyDictionary<string, LevelDesc> ThumbnailLevels(
        IReadOnlyList<LevelDesc> levels,
        LevelDesc baseLevel,
        IReadOnlyDictionary<string, LevelType> included)
    {
        var byFile = new Dictionary<string, LevelDesc>(StringComparer.OrdinalIgnoreCase);
        foreach (var level in levels)
        {
            var file = included.TryGetValue(level.LevelName, out var type) ? type.ThumbnailFile : null;
            if (string.IsNullOrEmpty(file))
            {
                continue;
            }

            if (!byFile.ContainsKey(file) || ReferenceEquals(level, baseLevel))
            {
                byFile[file] = level;
            }
        }

        return byFile;
    }

    private static LevelDesc PickBase(string folder, IReadOnlyList<LevelDesc> levels, Func<LevelDesc, string> display)
    {
        var named = levels.FirstOrDefault(l => l.LevelName.Equals(folder, StringComparison.OrdinalIgnoreCase));
        if (named is not null)
        {
            return named;
        }

        bool Eligible(LevelDesc l)
        {
            var name = display(l);
            return !SkippedPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                && !MiniGameDisplayNames.Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        var pool = levels.Where(Eligible).ToList();
        if (pool.Count == 0)
        {
            pool = levels.ToList();
        }

        return pool
            .OrderBy(l => display(l).Length)
            .ThenBy(display, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    /// <summary>Every asset of a platform tree a map draws itself, skipping themed nodes and their children the
    /// way <see cref="Imaging.MapCompositor" /> does: the file list matches what the compositor draws.</summary>
    private static IEnumerable<LevelAsset> Assets(IEnumerable<PlatformNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsThemed)
            {
                continue;
            }

            foreach (var asset in node.Assets)
            {
                yield return asset;
            }

            foreach (var asset in Assets(node.Children))
            {
                yield return asset;
            }
        }
    }

    private static IReadOnlyList<string> PickUiSets(IReadOnlyList<LevelSet> sets)
    {
        var present = sets.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var preferred = present.Contains("Ranked1v1") && present.Contains("Ranked2v2")
            ? RankedSetNames
            : StandardSetNames;

        // 3.0: the minigame chip comes last, after the sets a player picks a map from.
        string[] wanted = [.. preferred, MinigameSetName];
        return wanted.Where(present.Contains).ToList();
    }

    private static bool IsHidden(string folderName) =>
        HiddenFolderNames.Contains(folderName, StringComparer.OrdinalIgnoreCase);
}
