using BhMaps.Core.Maps;
using BhMaps.Core.Model;

namespace BhMaps.Core.Packs;

/// <summary>What one copy, move or remove did. Paths are library-relative ("packs\&lt;pack&gt;\&lt;folder&gt;\&lt;file&gt;"),
/// which is the form UndoSession.CaptureLibrary takes.</summary>
public sealed record PackCopyResult(
    IReadOnlyList<string> Written,
    IReadOnlyList<string> Removed,
    bool Skipped,
    IReadOnlyList<FileFailure> Failures)
{
    public static PackCopyResult Nothing { get; } = new([], [], false, []);

    public static PackCopyResult WasSkipped { get; } = new([], [], true, []);
}

/// <summary>What an import is about to do: these maps and these loose files, out of one pack into another.</summary>
public sealed record PackImportPlan(
    Pack Source, Pack Target, IReadOnlyList<MapEntry> Maps, IReadOnlyList<string> LooseFiles, bool Replace);

/// <summary>What an import did. Skipped holds display names and file names, for the done line.</summary>
public sealed record PackImportResult(
    IReadOnlyList<string> Written,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<FileFailure> Failures);

/// <summary>Spec 2.6 section 4.1: moves a map or a picture between packs, on paths alone. Everything it returns
/// is library-relative, which is what an undo session captures, so the shell can name what a call will touch
/// before it runs.</summary>
public static class PackCopier
{
    public const string PacksFolderName = "packs";

    public const string BackgroundsFolder = "Backgrounds";

    public const string BackgroundExtension = ".jpg";

    private static readonly string[] RecordFileNames = [PlatformEditRecord.FileName, BackgroundEditRecord.FileName];

    /// <summary>"packs\&lt;pack&gt;\&lt;relative&gt;": the form UndoSession.CaptureLibrary takes.</summary>
    public static string LibraryRelative(Pack pack, string relativePath) =>
        Path.Combine(PacksFolderName, pack.Name, relativePath);

    /// <summary>The first map, in catalog order, whose levels name this background slot. The rule pack detail
    /// has used since 2.2 (decision C-D4), here so one implementation of it serves the page and the copier.</summary>
    public static MapEntry? MapForSlot(MapCatalog catalog, string slot) =>
        catalog.Maps.FirstOrDefault(m => m.BackgroundSlots.Any(s => s.Equals(slot, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Spec 2: the map's folder in the pack, every file in it, plus every Backgrounds jpg whose slot
    /// this map owns. Pack-relative, in folder-then-backgrounds order.</summary>
    public static IReadOnlyList<string> MapFiles(Pack pack, MapEntry map, MapCatalog catalog)
    {
        var files = new List<string>();
        foreach (var file in pack.FindFolder(map.FolderName)?.Files ?? Array.Empty<GameFile>())
        {
            files.Add(Path.Combine(map.FolderName, file.Name));
        }

        foreach (var file in pack.FindFolder(BackgroundsFolder)?.Files ?? Array.Empty<GameFile>())
        {
            if (Path.GetExtension(file.Name).Equals(BackgroundExtension, StringComparison.OrdinalIgnoreCase)
                && MapForSlot(catalog, file.Name) is { } owner
                && owner.FolderName.Equals(map.FolderName, StringComparison.OrdinalIgnoreCase))
            {
                files.Add(Path.Combine(BackgroundsFolder, file.Name));
            }
        }

        return files;
    }

    /// <summary>Every library-relative path an operation on these pack-relative files touches in this pack, the
    /// two record files included: a copy merges entries into the target's records and a move takes them out of
    /// the source's, so both files are captured whether or not this call ends up rewriting them.</summary>
    public static IReadOnlyList<string> Touched(Pack pack, IEnumerable<string> relativePaths) =>
    [
        .. relativePaths.Select(r => LibraryRelative(pack, r)),
        .. RecordFileNames.Select(name => LibraryRelative(pack, name)),
    ];

    /// <summary>Spec 4.1: the map's files into the target under the same relative paths, with its record entries
    /// merged into the target's records. Skipped, with nothing written, when the target already holds any of
    /// them and replace is false.</summary>
    public static PackCopyResult CopyMap(Pack source, Pack target, MapEntry map, MapCatalog catalog, bool replace)
    {
        var files = MapFiles(source, map, catalog);
        if (files.Count == 0)
        {
            return PackCopyResult.Nothing;
        }

        var occupied = files.Any(r => File.Exists(Path.Combine(target.FullPath, r)));
        if (occupied && !replace)
        {
            return PackCopyResult.WasSkipped;
        }

        var written = new List<string>();
        var removed = new List<string>();
        var failures = new List<FileFailure>();
        if (occupied)
        {
            // The target's own copy goes first, so a map whose folder held files this one does not leaves none
            // of them behind pretending to belong to the copy.
            var cleared = RemoveMap(target, map, catalog);
            removed.AddRange(cleared.Removed);
            failures.AddRange(cleared.Failures);
        }

        foreach (var relativePath in files)
        {
            CopyInto(source.FullPath, target.FullPath, relativePath, written, failures);
        }

        MergeRecords(source, target, map, files, written);
        return new PackCopyResult(
            [.. written.Select(r => LibraryRelative(target, r))], removed, false, failures);
    }

    /// <summary>Spec 4.1: one Backgrounds jpg and its background record entry.</summary>
    public static PackCopyResult CopyFile(Pack source, Pack target, string relativePath, bool replace)
    {
        if (!File.Exists(Path.Combine(source.FullPath, relativePath)))
        {
            return PackCopyResult.Nothing;
        }

        if (File.Exists(Path.Combine(target.FullPath, relativePath)) && !replace)
        {
            return PackCopyResult.WasSkipped;
        }

        var written = new List<string>();
        var failures = new List<FileFailure>();
        CopyInto(source.FullPath, target.FullPath, relativePath, written, failures);
        if (BackgroundEditRecord.Load(source.FullPath).Entry(relativePath) is { } entry)
        {
            var record = BackgroundEditRecord.Load(target.FullPath);
            record.Set(relativePath, entry);
            record.Save(target.FullPath);
            written.Add(BackgroundEditRecord.FileName);
        }

        return new PackCopyResult([.. written.Select(r => LibraryRelative(target, r))], [], false, failures);
    }

    /// <summary>Spec 3 item 7: the map's files in the pack and its entries in both records. An empty map folder
    /// left behind is deleted; Backgrounds is not, because it holds other maps' slots.</summary>
    public static PackCopyResult RemoveMap(Pack pack, MapEntry map, MapCatalog catalog)
    {
        var removed = new List<string>();
        var failures = new List<FileFailure>();
        foreach (var relativePath in MapFiles(pack, map, catalog))
        {
            var full = Path.Combine(pack.FullPath, relativePath);
            try
            {
                if (File.Exists(full))
                {
                    File.Delete(full);
                }

                removed.Add(LibraryRelative(pack, relativePath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new FileFailure(full, ex.Message));
            }
        }

        var folder = Path.Combine(pack.FullPath, map.FolderName);
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An empty folder we cannot delete is untidy, not a failure of the removal.
        }

        var platforms = PlatformEditRecord.Load(pack.FullPath);
        if (platforms.RemoveMap(map.FolderName))
        {
            platforms.Save(pack.FullPath);
            removed.Add(LibraryRelative(pack, PlatformEditRecord.FileName));
        }

        var backgrounds = BackgroundEditRecord.Load(pack.FullPath);
        var dropped = false;
        foreach (var slot in map.BackgroundSlots)
        {
            dropped |= backgrounds.Remove(Path.Combine(BackgroundsFolder, slot));
        }

        if (dropped)
        {
            backgrounds.Save(pack.FullPath);
            removed.Add(LibraryRelative(pack, BackgroundEditRecord.FileName));
        }

        return new PackCopyResult([], removed, false, failures);
    }

    /// <summary>Spec 4.1: CopyMap, then the source's copy of the same files. A skipped copy moves nothing.</summary>
    public static PackCopyResult MoveMap(Pack source, Pack target, MapEntry map, MapCatalog catalog, bool replace)
    {
        var copied = CopyMap(source, target, map, catalog, replace);
        if (copied.Skipped || copied.Written.Count == 0)
        {
            return copied;
        }

        var cleared = RemoveMap(source, map, catalog);
        return copied with
        {
            Removed = [.. copied.Removed, .. cleared.Removed],
            Failures = [.. copied.Failures, .. cleared.Failures],
        };
    }

    /// <summary>Spec 4.1: CopyFile, then the source's file and its entry.</summary>
    public static PackCopyResult MoveFile(Pack source, Pack target, string relativePath, bool replace)
    {
        var copied = CopyFile(source, target, relativePath, replace);
        if (copied.Skipped || copied.Written.Count == 0)
        {
            return copied;
        }

        var removed = new List<string>();
        var failures = new List<FileFailure>(copied.Failures);
        var full = Path.Combine(source.FullPath, relativePath);
        try
        {
            File.Delete(full);
            removed.Add(LibraryRelative(source, relativePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add(new FileFailure(full, ex.Message));
        }

        var record = BackgroundEditRecord.Load(source.FullPath);
        if (record.Remove(relativePath))
        {
            record.Save(source.FullPath);
            removed.Add(LibraryRelative(source, BackgroundEditRecord.FileName));
        }

        return copied with { Removed = removed, Failures = failures };
    }

    /// <summary>Spec 4.1: "{name} copy", then "{name} copy 2", counting up past the folder names already under
    /// packs\, compared the way Windows compares them.</summary>
    public static string FreeCopyName(string libraryPath, string name)
    {
        var packsRoot = Path.Combine(libraryPath, PacksFolderName);
        var taken = Directory.Exists(packsRoot)
            ? new DirectoryInfo(packsRoot).EnumerateDirectories().Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidate = $"{name} copy";
        for (var n = 2; taken.Contains(candidate); n++)
        {
            candidate = $"{name} copy {n}";
        }

        return candidate;
    }

    /// <summary>Spec 3.1: the whole pack folder copied to a free name, which is returned. A copy that fails part
    /// way takes its own half-written folder with it and the exception goes to the caller.</summary>
    public static string DuplicatePack(string libraryPath, string name)
    {
        var packsRoot = Path.Combine(libraryPath, PacksFolderName);
        var copy = FreeCopyName(libraryPath, name);
        var from = Path.Combine(packsRoot, name);
        var to = Path.Combine(packsRoot, copy);
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
            }

            Directory.CreateDirectory(to);
            foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            {
                File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: false);
            }
        }
        catch
        {
            try
            {
                Directory.Delete(to, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                // The folder we could not finish is also one we could not clear; the throw below is the report.
            }

            throw;
        }

        return copy;
    }

    /// <summary>Spec 5.1: each map, then each loose file, in plan order. Cancellation stops between items and
    /// what was copied stays.</summary>
    public static PackImportResult Import(
        PackImportPlan plan, MapCatalog catalog, IProgress<string>? progress, CancellationToken ct)
    {
        var written = new List<string>();
        var removed = new List<string>();
        var skipped = new List<string>();
        var failures = new List<FileFailure>();
        var total = plan.Maps.Count + plan.LooseFiles.Count;
        var index = 0;
        foreach (var map in plan.Maps)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Importing {++index} of {total}: {map.DisplayName}");
            var result = CopyMap(plan.Source, plan.Target, map, catalog, plan.Replace);
            Collect(result, map.DisplayName, written, removed, skipped, failures);
        }

        foreach (var relativePath in plan.LooseFiles)
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(relativePath);
            progress?.Report($"Importing {++index} of {total}: {name}");
            var result = CopyFile(plan.Source, plan.Target, relativePath, plan.Replace);
            Collect(result, name, written, removed, skipped, failures);
        }

        return new PackImportResult(written, removed, skipped, failures);
    }

    private static void Collect(
        PackCopyResult result,
        string name,
        List<string> written,
        List<string> removed,
        List<string> skipped,
        List<FileFailure> failures)
    {
        if (result.Skipped)
        {
            skipped.Add(name);
            return;
        }

        written.AddRange(result.Written);
        removed.AddRange(result.Removed);
        failures.AddRange(result.Failures);
    }

    /// <summary>The map's entry in each record, copied over whatever the target held for it.</summary>
    private static void MergeRecords(
        Pack source, Pack target, MapEntry map, IReadOnlyList<string> files, List<string> written)
    {
        if (PlatformEditRecord.Load(source.FullPath).Map(map.FolderName) is { } entry)
        {
            var record = PlatformEditRecord.Load(target.FullPath);
            record.SetMap(map.FolderName, entry.SavedAt, entry.Pieces);
            record.Save(target.FullPath);
            written.Add(PlatformEditRecord.FileName);
        }

        var backgrounds = BackgroundEditRecord.Load(source.FullPath);
        var slots = files
            .Where(r => r.StartsWith(BackgroundsFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Select(r => (Relative: r, Entry: backgrounds.Entry(r)))
            .Where(x => x.Entry is not null)
            .ToList();
        if (slots.Count == 0)
        {
            return;
        }

        var targetRecord = BackgroundEditRecord.Load(target.FullPath);
        foreach (var slot in slots)
        {
            targetRecord.Set(slot.Relative, slot.Entry!);
        }

        targetRecord.Save(target.FullPath);
        written.Add(BackgroundEditRecord.FileName);
    }

    private static void CopyInto(
        string sourceRoot, string targetRoot, string relativePath, List<string> written, List<FileFailure> failures)
    {
        var to = Path.Combine(targetRoot, relativePath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(Path.Combine(sourceRoot, relativePath), to, overwrite: true);
            written.Add(relativePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add(new FileFailure(to, ex.Message));
        }
    }
}
