using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Scanning;

namespace BhMaps.Core.Operations;

/// <summary>One game file the Default pack has no copy of, and the map it belongs to. The path is relative to
/// mapArt, so it is both the place in the game and the place in the pack.</summary>
public sealed record MissingDefault(string GameRelativePath, MapEntry Map);

/// <summary>What a top-up added. <see cref="MapsAdded"/> names each map that got at least one file, once.</summary>
public sealed record AddMissingResult(IReadOnlyList<string> MapsAdded, int Copied, IReadOnlyList<FileFailure> Failures)
{
    public int Failed => Failures.Count;
}

/// <summary>The pack that holds the game's own art. Every reset measures against it.</summary>
public static class DefaultPack
{
    public const string Name = "Default";

    /// <summary>Where a capture is built before it becomes the Default pack. A sibling of it, so the swap at the
    /// end is a rename on the same volume rather than a second copy.</summary>
    private const string CapturingName = Name + ".capturing";

    public static Pack? Find(IReadOnlyList<Pack> packs) =>
        packs.FirstOrDefault(p => p.Name.Equals(Name, StringComparison.OrdinalIgnoreCase));

    public static bool Exists(string libraryPath) =>
        Directory.Exists(Path.Combine(PackScanner.PacksRoot(libraryPath), Name));

    /// <summary>Imports the whole game folder into packs\Default.capturing and, only once that import has run to
    /// the end, replaces packs\Default with it. The caller confirms first. A capture that is cancelled or fails
    /// takes only its own temporary folder with it, so the Default pack that was there is still the one on disk.</summary>
    public static ApplyResult Capture(string gamePath, string libraryPath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var packsRoot = PackScanner.PacksRoot(libraryPath);
        var packRoot = Path.Combine(packsRoot, Name);
        var capturingRoot = Path.Combine(packsRoot, CapturingName);

        // A temporary folder an earlier run left behind holds half a capture, so it is thrown away rather than
        // merged into.
        if (Directory.Exists(capturingRoot) && PackDeleter.Delete(libraryPath, CapturingName) is { } staleError)
        {
            return new ApplyResult(0, [new FileFailure(capturingRoot, staleError)]);
        }

        var plan = ImportRouter.Plan(gamePath, GameTreeScanner.Scan(gamePath));
        ApplyResult result;
        try
        {
            result = ImportRouter.Execute(plan, CapturingName, libraryPath, progress, ct);
        }
        catch
        {
            // Cancellation is rethrown for the busy boundary to read as a cancel; anything else is still the
            // caller's to report. Either way the half-copy goes and the old pack is untouched.
            PackDeleter.Delete(libraryPath, CapturingName);
            throw;
        }

        // Replace rather than merge: a stale folder left behind would read as game art forever. Deleted only
        // now, with the whole capture already on disk beside it.
        if (Directory.Exists(packRoot) && PackDeleter.Delete(libraryPath, Name) is { } deleteError)
        {
            PackDeleter.Delete(libraryPath, CapturingName);
            return new ApplyResult(0, [new FileFailure(packRoot, deleteError)]);
        }

        try
        {
            Directory.Move(capturingRoot, packRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The old pack is already gone and the capture is the only copy left, so it stays where it is for
            // the next capture to find rather than being deleted with the failure.
            return new ApplyResult(0, [new FileFailure(packRoot, ex.Message)]);
        }

        return result;
    }

    /// <summary>The art of the catalog's maps that the Default pack has no copy of at all: what a game update
    /// leaves behind when it ships a new map. The opposite of a capture, which replaces the pack with the game
    /// as it is now, applied art and all. A file the pack already holds is never a candidate, whatever its
    /// bytes, and <paramref name="isVanilla"/> is the caller's say on whether the game's copy is the game's own
    /// art rather than something applied over it.</summary>
    public static IReadOnlyList<MissingDefault> FindMissing(
        GameTree tree, MapCatalog catalog, Pack defaultPack, Func<string, bool> isVanilla)
    {
        var missing = new List<MissingDefault>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Consider(GameFolder? folder, string fileName, MapEntry map)
        {
            if (folder is null || !ImageFiles.IsImage(fileName) || folder.FindFile(fileName) is null)
            {
                return;
            }

            var relative = Path.Combine(folder.Name, fileName);

            // Two maps can name the same background slot, so a path is offered once however many maps want it.
            if (!seen.Add(relative))
            {
                return;
            }

            if (defaultPack.FindFolder(folder.Name)?.FindFile(fileName) is null && isVanilla(relative))
            {
                missing.Add(new MissingDefault(relative, map));
            }
        }

        foreach (var map in catalog.Maps)
        {
            var mapFolder = tree.FindFolder(map.FolderName);
            foreach (var file in mapFolder?.Files ?? [])
            {
                Consider(mapFolder, file.Name, map);
            }

            foreach (var slot in map.BackgroundSlots)
            {
                var relative = AssetPath.Background(slot);
                Consider(tree.FindFolder(AssetPath.FolderOf(relative)), Path.GetFileName(relative), map);
            }
        }

        return missing;
    }

    /// <summary>Copies each missing file from the game into packs\Default, adding to the pack and never writing
    /// over it: a file that turned up there since <see cref="FindMissing"/> ran is left as it is.</summary>
    public static AddMissingResult AddMissing(
        string gamePath,
        string libraryPath,
        IReadOnlyList<MissingDefault> missing,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var packRoot = Path.Combine(PackScanner.PacksRoot(libraryPath), Name);
        var copied = 0;
        var failures = new List<FileFailure>();
        var mapsAdded = new List<string>();
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in missing)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(item.Map.DisplayName);
            var target = Path.Combine(packRoot, item.GameRelativePath);
            if (File.Exists(target))
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                // overwrite: false, so even a file written between the check and here keeps its bytes.
                File.Copy(Path.Combine(gamePath, item.GameRelativePath), target, overwrite: false);
                copied++;
                if (named.Add(item.Map.DisplayName))
                {
                    mapsAdded.Add(item.Map.DisplayName);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new FileFailure(target, ex.Message));
            }
        }

        return new AddMissingResult(mapsAdded, copied, failures);
    }
}
