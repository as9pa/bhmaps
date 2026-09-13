using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Packs;

namespace BhMaps.Core.Operations;

/// <summary>Spec 7: a reset to default puts the game files back, so the edits the packs remembered for that map
/// no longer describe anything on disk. This clears them from every pack the map currently matches.</summary>
public static class RecordReset
{
    /// <summary>The packs the map's files currently match (state Pack), distinct, in status order.</summary>
    public static IReadOnlyList<Pack> MatchedPacks(MapEntry map, MapStatus? status, IReadOnlyList<Pack> packs)
    {
        // A status measured for another map says nothing about this one, and clearing on it would throw away a
        // record the reset never touched.
        if (status is null || !status.FolderName.Equals(map.FolderName, StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<Pack>();
        }

        var matched = new List<Pack>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in status.Files.Where(f => f.State == MapFileState.Pack).SelectMany(f => f.PackNames))
        {
            if (seen.Add(name)
                && packs.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } pack)
            {
                matched.Add(pack);
            }
        }

        return matched;
    }

    /// <summary>Record paths relative to libraryPath for the undo capture: both record files of every matched pack.</summary>
    public static IReadOnlyList<string> UndoPaths(IReadOnlyList<Pack> matched, string libraryPath)
    {
        var paths = new List<string>();
        foreach (var pack in matched)
        {
            Add(PlatformEditRecord.PathFor(pack.FullPath));
            Add(BackgroundEditRecord.PathFor(pack.FullPath));
        }

        return paths;

        void Add(string fullPath)
        {
            var relative = Path.GetRelativePath(libraryPath, fullPath);
            // A pack outside the library has no path the capture can express under the session's library side.
            if (!Path.IsPathRooted(relative) && !relative.StartsWith("..", StringComparison.Ordinal))
            {
                paths.Add(relative);
            }
        }
    }

    /// <summary>Removes the map's platform entry set and its background slots from every matched pack's records.
    /// Saves only records that changed. Never throws for a missing record.</summary>
    public static void Clear(MapEntry map, IReadOnlyList<Pack> matched)
    {
        foreach (var pack in matched)
        {
            // A pack that kept no record loads as an empty one, changes nothing, and so is never written: the
            // reset must not leave record files behind in packs that had none.
            var platforms = PlatformEditRecord.Load(pack.FullPath);
            if (platforms.RemoveMap(map.FolderName))
            {
                Save(() => platforms.Save(pack.FullPath));
            }

            var backgrounds = BackgroundEditRecord.Load(pack.FullPath);
            var changed = false;
            foreach (var slot in map.BackgroundSlots)
            {
                changed |= backgrounds.Remove(AssetPath.Background(slot));
            }

            if (changed)
            {
                Save(() => backgrounds.Save(pack.FullPath));
            }
        }
    }

    private static void Save(Action save)
    {
        try
        {
            save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A record we cannot rewrite is stale, not a failed reset: the game files are already back to default.
        }
    }
}
