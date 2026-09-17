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
/// name, and <see cref="LevelFor"/> answers which level a file is the picture of.</summary>
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
    IReadOnlyDictionary<string, LevelDesc>? ThumbnailLevels = null)
{
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
    public static readonly string[] RankedSetNames = ["Ranked1v1", "Ranked2v2", "Tournament1v1"];
    public static readonly string[] StandardSetNames = ["Standard1v1", "Standard2v2", "Tournament1v1"];
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
        ["Tournament1v1"] = "Tournament",
        ["Standard1v1"] = "Standard 1v1",
        ["Standard2v2"] = "Standard 2v2",
    };

    private readonly Dictionary<string, MapEntry> byFolder;

    private MapCatalog(IReadOnlyList<MapEntry> maps, IReadOnlyList<string> uiSetNames, bool hasLevelData)
    {
        Maps = maps;
        UiSetNames = uiSetNames;
        UiSets = uiSetNames.Select(n => new UiSet(n, LabelFor(n))).ToList();
        HasLevelData = hasLevelData;
        byFolder = new Dictionary<string, MapEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in maps)
        {
            byFolder[map.FolderName] = map;
        }
    }

    /// <summary>Sorted by display name, ordinal ignore case.</summary>
    public IReadOnlyList<MapEntry> Maps { get; }

    /// <summary>The chip sets in the order the UI shows them. Empty in the fallback.</summary>
    public IReadOnlyList<string> UiSetNames { get; }

    /// <summary>The same sets as <see cref="UiSetNames"/>, each paired with its chip label.</summary>
    public IReadOnlyList<UiSet> UiSets { get; }

    /// <summary>False when the catalog came from folder names alone. Consumers must check it before trying to
    /// compose a preview.</summary>
    public bool HasLevelData { get; }

    public MapEntry? ByFolder(string folderName) =>
        byFolder.TryGetValue(folderName, out var map) ? map : null;

    /// <summary>The chip label for a set name; an unlabelled set is shown under its own name.</summary>
    public static string LabelFor(string setName) =>
        SetLabels.TryGetValue(setName, out var label) ? label : setName;

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

        return new MapCatalog(maps, PickUiSets(data.Sets), hasLevelData: true);
    }

    /// <summary>Spec 3.6 fallback: one map per game folder, folder name as display name, no sets.</summary>
    public static MapCatalog FromFolders(GameTree tree)
    {
        var maps = tree.Folders
            .Where(f => !IsHidden(f.Name))
            .Select(FolderEntry)
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MapCatalog(maps, Array.Empty<string>(), hasLevelData: false);
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
            levels
                .SelectMany(l => Assets(l.Platforms).Select(a => AssetPath.Resolve(l.AssetDir, a.AssetName)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            owned,
            candidates,
            ThumbnailLevels(levels, baseLevel, included));
    }

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

        return preferred.Where(present.Contains).ToList();
    }

    private static bool IsHidden(string folderName) =>
        HiddenFolderNames.Contains(folderName, StringComparer.OrdinalIgnoreCase);
}
