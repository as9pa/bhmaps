using BhMaps.Core.Maps;
using BhMaps.Core.Model;

namespace BhMaps.Core.Operations;

/// <summary>Copies pack files into the game folder, overwriting. Never deletes anything.</summary>
public static class PackApplier
{
    public static ApplyResult ApplyPack(Pack pack, string gamePath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var copied = 0;
        var failures = new List<FileFailure>();
        foreach (var folder in pack.Folders)
        {
            CopyFolder(folder, gamePath, progress, ct, ref copied, failures);
        }

        return new ApplyResult(copied, failures);
    }

    public static ApplyResult ApplyFolder(Pack pack, string folderName, string gamePath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var folder = pack.FindFolder(folderName);
        if (folder is null)
        {
            return new ApplyResult(0, [new FileFailure(Path.Combine(gamePath, folderName), $"Pack '{pack.Name}' has no folder named '{folderName}'.")]);
        }

        var copied = 0;
        var failures = new List<FileFailure>();
        CopyFolder(folder, gamePath, progress, ct, ref copied, failures);
        return new ApplyResult(copied, failures);
    }

    public static ApplyResult ApplyFile(Pack pack, string folderName, string fileName, string gamePath)
    {
        var folder = pack.FindFolder(folderName);
        var file = folder?.FindFile(fileName);
        if (folder is null || file is null)
        {
            return new ApplyResult(0, [new FileFailure(Path.Combine(gamePath, folderName, fileName), $"Pack '{pack.Name}' has no file '{folderName}\\{fileName}'.")]);
        }

        var copied = 0;
        var failures = new List<FileFailure>();
        CopyOne(file, Path.Combine(gamePath, folder.Name), ref copied, failures);
        return new ApplyResult(copied, failures);
    }

    /// <summary>Spec 3.3: the pack, aimed at named maps. For each map it copies the pack's files for that map's
    /// folder and the pack's Backgrounds files whose names are that map's slots, and nothing else in the pack. A
    /// map the pack has nothing for is skipped rather than failed: a pack covers the maps it covers.</summary>
    public static ApplyResult ApplyToMaps(
        Pack pack,
        IReadOnlyList<MapEntry> maps,
        string gamePath,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var copied = 0;
        var failures = new List<FileFailure>();
        var backgrounds = pack.FindFolder(BackgroundsFolder);
        foreach (var map in maps)
        {
            ct.ThrowIfCancellationRequested();
            if (pack.FindFolder(map.FolderName) is { } folder)
            {
                CopyFolder(folder, gamePath, progress, ct, ref copied, failures);
            }

            foreach (var file in SlotFiles(backgrounds, map))
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"{BackgroundsFolder}\\{file.Name}");
                CopyOne(file, Path.Combine(gamePath, BackgroundsFolder), ref copied, failures);
            }
        }

        return new ApplyResult(copied, failures);
    }

    /// <summary>The relative paths <see cref="ApplyToMaps"/> would write, for the undo snapshot (spec 6.6).</summary>
    public static IReadOnlyList<string> ApplyToMapsPaths(Pack pack, IReadOnlyList<MapEntry> maps)
    {
        var backgrounds = pack.FindFolder(BackgroundsFolder);
        return maps
            .SelectMany(map =>
                (pack.FindFolder(map.FolderName)?.Files.Select(f => Path.Combine(map.FolderName, f.Name))
                 ?? Array.Empty<string>())
                .Concat(SlotFiles(backgrounds, map).Select(f => Path.Combine(BackgroundsFolder, f.Name))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The relative paths a reset of one map would write, for the undo snapshot (spec 6.6): the files
    /// that folder holds in the game now, the Default pack's files for it, and the slots that pack can actually
    /// restore. MapReset skips a slot the pack has not got, so nothing outside this list is touched. The map
    /// panel and the selection bar both call this: two copies of an undo list is how one of them ends up short,
    /// and what a short list leaves overwritten cannot be put back.</summary>
    public static IReadOnlyList<string> ResetMapPaths(GameTree tree, MapEntry map, Pack defaultPack)
    {
        var backgrounds = defaultPack.FindFolder(BackgroundsFolder);
        var slots = map.BackgroundSlots.Where(slot => backgrounds?.FindFile(slot) is not null).ToList();
        var current = tree.FindFolder(map.FolderName)?.Files.Select(f => Path.Combine(map.FolderName, f.Name))
            ?? Array.Empty<string>();
        return current
            .Concat(PlatformSetApplier.TargetPaths(defaultPack, map.FolderName))
            .Concat(BackgroundApplier.TargetPaths(slots))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The pack's pictures for one map's slots, in slot order, each at most once.</summary>
    private static IEnumerable<GameFile> SlotFiles(GameFolder? backgrounds, MapEntry map) =>
        backgrounds is null
            ? []
            : map.BackgroundSlots
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(backgrounds.FindFile)
                .OfType<GameFile>();

    private const string BackgroundsFolder = "Backgrounds";

    private static void CopyFolder(GameFolder folder, string gamePath, IProgress<string>? progress, CancellationToken ct, ref int copied, List<FileFailure> failures)
    {
        var targetDir = Path.Combine(gamePath, folder.Name);
        foreach (var file in folder.Files)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"{folder.Name}\\{file.Name}");
            CopyOne(file, targetDir, ref copied, failures);
        }
    }

    private static void CopyOne(GameFile file, string targetDir, ref int copied, List<FileFailure> failures)
    {
        var target = Path.Combine(targetDir, file.Name);
        try
        {
            Directory.CreateDirectory(targetDir);
            File.Copy(file.FullPath, target, overwrite: true);
            copied++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add(new FileFailure(target, ex.Message));
        }
    }
}
