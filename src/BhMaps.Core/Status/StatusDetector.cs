using BhMaps.Core.Hashing;
using BhMaps.Core.Model;

namespace BhMaps.Core.Status;

/// <summary>Decides, per game folder and per file, which packs are currently applied. Pure: reads hashes, changes nothing.</summary>
public static class StatusDetector
{
    public static StatusReport Detect(GameTree gameTree, IReadOnlyList<Pack> packs, HashCache hashCache)
    {
        var folders = new Dictionary<string, FolderStatus>(StringComparer.OrdinalIgnoreCase);
        var files = new Dictionary<string, FileStatus>(StringComparer.OrdinalIgnoreCase);

        foreach (var gameFolder in gameTree.Folders)
        {
            var gameHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var packsPerFile = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in gameFolder.Files)
            {
                // Through the indexer rather than ToDictionary: a case-sensitive directory can hold
                // two names differing only in case, and last one wins beats throwing.
                gameHashes[file.Name] = hashCache.GetOrCompute(file);
                packsPerFile[file.Name] = new List<string>();
            }

            var applied = new List<string>();

            foreach (var pack in packs)
            {
                var packFolder = pack.FindFolder(gameFolder.Name);
                if (packFolder is null || packFolder.Files.Count == 0)
                {
                    continue;
                }

                var allMatch = true;
                foreach (var packFile in packFolder.Files)
                {
                    var packHash = hashCache.GetOrCompute(packFile);
                    if (gameHashes.TryGetValue(packFile.Name, out var gameHash) && gameHash == packHash)
                    {
                        packsPerFile[packFile.Name].Add(pack.Name);
                    }
                    else
                    {
                        allMatch = false;
                    }
                }

                if (allMatch)
                {
                    applied.Add(pack.Name);
                }
            }

            var state = gameFolder.Files.Count == 0 ? FolderState.Empty
                : applied.Count > 0 ? FolderState.Applied
                : FolderState.Unmanaged;
            folders[gameFolder.Name] = new FolderStatus(gameFolder.Name, state, applied);

            foreach (var file in gameFolder.Files)
            {
                files[StatusReport.Key(gameFolder.Name, file.Name)] =
                    new FileStatus(gameFolder.Name, file.Name, packsPerFile[file.Name]);
            }
        }

        return new StatusReport(folders, files);
    }
}
