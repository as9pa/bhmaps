using BhMaps.Core.Hashing;
using BhMaps.Core.LevelData;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;

namespace BhMaps.Core.Maps;

/// <summary>Decides, per map and per file, whether the game shows default art, a pack's art, something custom,
/// or nothing at all. Pure: reads hashes, changes nothing.</summary>
public static class MapStatusDetector
{
    public static IReadOnlyDictionary<string, MapStatus> Detect(
        MapCatalog catalog, GameTree tree, IReadOnlyList<Pack> packs, HashCache hashes)
    {
        var defaultPack = DefaultPack.Find(packs);
        var statuses = new Dictionary<string, MapStatus>(StringComparer.OrdinalIgnoreCase);

        foreach (var map in catalog.Maps)
        {
            statuses[map.FolderName] = Summarise(map.FolderName, FileStatuses(map, tree, packs, defaultPack, hashes));
        }

        return statuses;
    }

    /// <summary>Every file of the map worth measuring: the game folder's own files, then the Default pack's files
    /// for that folder so one the game lacks reads as Missing, then the map's background slots.</summary>
    private static IReadOnlyList<MapFileStatus> FileStatuses(
        MapEntry map, GameTree tree, IReadOnlyList<Pack> packs, Pack? defaultPack, HashCache hashes)
    {
        var files = new List<MapFileStatus>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string folderName, string fileName)
        {
            // A case-sensitive folder can hold two names differing only in case; one entry per file is enough.
            if (!seen.Add(Path.Combine(folderName, fileName)))
            {
                return;
            }

            var status = FileStatus(folderName, fileName, tree, packs, defaultPack, hashes);
            if (status is not null)
            {
                files.Add(status);
            }
        }

        foreach (var file in tree.FindFolder(map.FolderName)?.Files ?? Array.Empty<GameFile>())
        {
            Add(map.FolderName, file.Name);
        }

        foreach (var file in defaultPack?.FindFolder(map.FolderName)?.Files ?? Array.Empty<GameFile>())
        {
            Add(map.FolderName, file.Name);
        }

        foreach (var slot in map.BackgroundSlots)
        {
            // Backgrounds\<slot>, except for a slot borrowed from a theme folder through "../".
            var relative = AssetPath.Background(slot);
            Add(AssetPath.FolderOf(relative), Path.GetFileName(relative));
        }

        return files;
    }

    /// <summary>Null when neither the game nor the Default pack has the file: there is nothing to report.</summary>
    private static MapFileStatus? FileStatus(
        string folderName,
        string fileName,
        GameTree tree,
        IReadOnlyList<Pack> packs,
        Pack? defaultPack,
        HashCache hashes)
    {
        var relative = Path.Combine(folderName, fileName);
        var gameFile = tree.FindFolder(folderName)?.FindFile(fileName);
        if (gameFile is null)
        {
            return defaultPack?.FindFolder(folderName)?.FindFile(fileName) is null
                ? null
                : new MapFileStatus(relative, MapFileState.Missing, Array.Empty<string>());
        }

        var gameHash = hashes.GetOrCompute(gameFile);
        var matched = new List<string>();
        var isDefault = false;

        foreach (var pack in packs)
        {
            var packFile = pack.FindFolder(folderName)?.FindFile(fileName);
            if (packFile is null || hashes.GetOrCompute(packFile) != gameHash)
            {
                continue;
            }

            if (pack.Name.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase))
            {
                isDefault = true;
            }
            else
            {
                matched.Add(pack.Name);
            }
        }

        // Matching Default and a pack counts as the pack: the pack is what the user chose to apply.
        if (matched.Count > 0)
        {
            return new MapFileStatus(relative, MapFileState.Pack, matched);
        }

        return new MapFileStatus(
            relative, isDefault ? MapFileState.Default : MapFileState.Custom, Array.Empty<string>());
    }

    /// <summary>Missing beats Custom beats pack names beats Default (decision D8).</summary>
    private static MapStatus Summarise(string folderName, IReadOnlyList<MapFileStatus> files)
    {
        var packNames = new List<string>();
        foreach (var name in files.SelectMany(f => f.PackNames))
        {
            if (!packNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                packNames.Add(name);
            }
        }

        // Nothing to measure is not default art: the map has no files on disk and no default to compare them to.
        var state = files.Any(f => f.State == MapFileState.Missing) ? MapState.Missing
            : files.Count == 0 || files.Any(f => f.State == MapFileState.Custom) ? MapState.Custom
            : packNames.Count > 0 ? MapState.Packs
            : MapState.Default;

        return new MapStatus(folderName, state, packNames, files);
    }
}
