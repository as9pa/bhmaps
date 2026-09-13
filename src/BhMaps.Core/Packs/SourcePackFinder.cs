using BhMaps.Core.Maps;
using BhMaps.Core.Model;

namespace BhMaps.Core.Packs;

/// <summary>Which pack an editor opened without one takes its remembered values from: the first pack the map's
/// own file statuses name that also kept a record for what is about to be shown (spec 5.1).</summary>
public static class SourcePackFinder
{
    /// <summary>Spec 5.1 step 2: the first pack, in the order the map's platform file statuses list them, whose
    /// platform record has an entry set for the map. Null when none.</summary>
    public static Pack? ForPlatforms(MapEntry map, MapStatus? status, IReadOnlyList<Pack> packs)
    {
        foreach (var pack in Candidates(map.PlatformFiles, status, packs))
        {
            if (PlatformEditRecord.Load(pack.FullPath).Map(map.FolderName) is not null)
            {
                return pack;
            }
        }

        return null;
    }

    /// <summary>Spec 8: the first pack listed for the slot's status whose background record has an entry for it.</summary>
    public static Pack? ForBackground(string slotRelativePath, MapStatus? status, IReadOnlyList<Pack> packs)
    {
        foreach (var pack in Candidates([slotRelativePath], status, packs))
        {
            if (BackgroundEditRecord.Load(pack.FullPath).Entry(slotRelativePath) is not null)
            {
                return pack;
            }
        }

        return null;
    }

    /// <summary>The packs the status names for those files, in file order then pack order, each one once. Only
    /// the Pack state names packs; every other state has an empty list.</summary>
    private static IEnumerable<Pack> Candidates(
        IReadOnlyList<string> relativePaths, MapStatus? status, IReadOnlyList<Pack> packs)
    {
        if (status is null)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relativePath in relativePaths)
        {
            var file = status.Files.FirstOrDefault(
                f => f.State == MapFileState.Pack
                    && f.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));
            foreach (var name in file?.PackNames ?? [])
            {
                if (seen.Add(name)
                    && packs.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } pack)
                {
                    yield return pack;
                }
            }
        }
    }
}
