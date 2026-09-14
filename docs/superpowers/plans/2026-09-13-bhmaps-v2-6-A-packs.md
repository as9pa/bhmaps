# BhMaps 2.6 Part A Implementation Plan: pack tile menus, copy and move, Import from pack, Duplicate

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every tile on a pack detail page has a menu, a map or a picture moves and copies between packs by menu or by Ctrl+C / Ctrl+X / Ctrl+V, a whole pack imports into another through one dialog, and a pack duplicates from the Packs row menu, with Undo behind everything that removes or moves.

**Architecture:** A new Core static class `PackCopier` owns every file and record operation on paths alone, and names the library-relative paths each operation will touch so the shell can capture them for undo before the work starts. The shell gains one library-side write boundary, `RunLibraryWriteAsync`, which is the game-write boundary with the launcher and the game side taken out: an undo session holding library paths, the busy flag, a done line with Undo, a rescan. The pack detail page and the Packs page call it; the pack chooser and the import dialog are the existing chooser window's style and mechanics.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, xunit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-09-13-bhmaps-v2-6-design.md` sections 2, 3, 4, 5 and 6 (binding). Sections 7 (auto-update) and 8 (thumbnails) are other plans and nothing here depends on them.

**Branch:** `feature/bhmaps-v2.6`.

## Conflicts with spec

1. **There is no undoable library-only done line.** Spec 4.2 and 4.3 want "the done line as the existing DoneUndoable path shows it" for a move, a remove, an import and a duplicate, but `MainViewModel.SetLibraryDone` (`MainViewModel.cs:1042`) sets `DoneUndoable = false`, and the only path that sets it true is `RunWriteCoreAsync` (`MainViewModel.cs:1106`), which runs through `_launcher.RunWriteAsync` (the game-running policy) and refuses while `GameFolderMissing`. **Resolution:** Task 3 adds `MainViewModel.RunLibraryWriteAsync`, a sibling of `RunWriteCoreAsync` with no launcher and no game side. `UndoCommand` keeps `CanExecute = CanWrite`, so Undo of a library write is still off while the game folder is missing; that is existing behaviour and is left alone.

2. **The done line carries no button but Undo.** Spec 4.3 wants a plain copy to offer `Open {target}`. **Resolution:** Task 3 adds `DoneActionText` and `DoneActionCommand` to the shell and one `PlainButton` beside Undo in `PageHeader.xaml`.

3. **`Apply to {map}` collides with the 2.5 picture line of the same name.** Spec 3 line 2 is `Apply to {map}` running `ApplySetAsync` (platform art), while `BuildTileMenu` already emits `Apply to {owner.DisplayName}` running `ApplyPictureAsync` (the background). Spec 3 then lists the picture lines that stay and the owner line is not among them. **Resolution:** on a map tile the `Apply to {map}` line is the `ApplySetAsync` one and the owner picture line goes; that pack's background for that map is still one click away through `Apply to a map...`. `ApplySetAsync` writes platform art only, so `Apply to {map}` does not write the pack's background: that is the spec's own wording and is left as written.

4. **"The Default pack's menu omits Remove as today" is not today's rule.** `PacksViewModel.BuildMenu` (`PacksViewModel.cs:195`) gives every row Export, Open folder and Remove, Default included. **Resolution:** keep the current behaviour exactly (Remove stays on every row) and add only the two new lines. Spec 4.1's Default restriction is applied where it is real: the new pack detail tile lines `Move to pack...` and `Remove from {pack}` are not built when the open pack is Default.

5. **Spec 3 says the file tile menu is "unchanged" while spec 4.1 says Remove is never offered on Default.** **Resolution:** the existing `Remove from {pack}` line on a file tile stays on every pack including Default (unchanged, as spec 3 says outright); the Default restriction applies to the lines 2.6 adds.

6. **`Views/Dialogs/` does not exist.** Every window in this app is `src\BhMaps.App\Views\<Name>Window.xaml` with its view model in `src\BhMaps.App\ViewModels\`. **Resolution:** the import dialog is `src\BhMaps.App\Views\ImportFromPackWindow.xaml` with `src\BhMaps.App\ViewModels\ImportFromPackViewModel.cs`.

7. **Pack detail has no status line for `Nothing copied yet`.** **Resolution:** Task 6 adds `PackDetailViewModel.ClipboardHint` and a muted `TextBlock` in the page header actions, cleared by a `DispatcherTimer` after two seconds.

8. **Undo of a Duplicate leaves an empty folder.** `UndoSession.CaptureLibrary` does record absent paths (`_library_absent.txt`, `UndoStore.cs:65`) and `RestoreSides` deletes them (`UndoStore.cs:272`), so "path absent" is already captured and no new capture API is needed; what is missing is that deleting the files leaves `packs\{name} copy\Backgrounds\` and `packs\{name} copy\` standing. **Resolution:** Task 3 extends `UndoStore` with one private `PruneEmptyFolders` step over the library-absent paths, with tests. That is the smallest extension and it serves move and import undo too.

## Global Constraints

- .NET 10, WPF, TreatWarningsAsErrors on. Format with `dotnet format BhMaps.slnx` only. Build and test with `--artifacts-path <ART>`.
- Tests only in `tests\BhMaps.Core.Tests` (xunit 2.9.3), on temp folders through `TempDir` and `FakeGameTree`. No network in tests. No test touches the real game folder, the real library or the real `%APPDATA%\BhMaps`.
- New `.cs` and `.xaml` files CRLF. Docs CRLF, UTF-8 without BOM. Copy in the UI is sentence case, no em-dashes, no emoji.
- WPF: `FocusVisualStyle="{StaticResource DialogFocusRing}"` as a local attribute on every focusable control added; no bare `x:Static` const int into a double; CommunityToolkit.Mvvm `[ObservableProperty]` setters run `OnXChanged` during construction, so order the constructor accordingly.
- Every library write outside a plain copy goes through an undo session.
- Never run the app or tests against the real game folder, the real library or `%APPDATA%\BhMaps`.
- One commit per task, message from a file with `git commit -F`, trailers as the dispatcher gives them.
- Names consumed from 2.5: `UndoSession.CaptureLibrary(string libraryPath, IEnumerable<string> relativePaths)`, `UndoStore.Restore(UndoSession, string gamePath, string libraryPath)`, `MainViewModel.RunBusyAsync(label, work, prefixProgress = true)`, `MainViewModel.RescanAsync(IReadOnlyList<string>? writtenFolders = null)`, `MainViewModel.SetLibraryDone(string)`, `MainViewModel.ApplySetAsync(Pack, IReadOnlyList<MapEntry>, bool)`, `MainViewModel.ChooseMapAsync`, `PackScanner.PacksRoot(string libraryPath)`, `DefaultPack.Name`.

---

## Task 1: Core PackCopier, one map and one file

Spec 2 (what a map's files in a pack are), 4.1 first three bullets and the `RemoveMap` bullet.

**Files:**
- Create: `src\BhMaps.Core\Packs\PackCopier.cs`
- Test: `tests\BhMaps.Core.Tests\PackCopierTests.cs`

**Interfaces:**

Consumes: `Pack` (`src\BhMaps.Core\Model\Pack.cs:4`, `FindFolder`, `FullPath`, `Name`), `GameFolder` and `GameFile` (`src\BhMaps.Core\Model\GameTree.cs:4,7`), `MapEntry` (`src\BhMaps.Core\Maps\MapCatalog.cs:12`, `FolderName`, `BackgroundSlots`), `MapCatalog.Maps`, `PlatformEditRecord` (`src\BhMaps.Core\Packs\PlatformEditRecord.cs:51`, `Load`, `Save`, `Map`, `SetMap`, `RemoveMap`, `FileName`), `BackgroundEditRecord` (`src\BhMaps.Core\Packs\BackgroundEditRecord.cs:38`, `Load`, `Save`, `Entry`, `Set`, `Remove`, `FileName`), `ApplyResult` and `FileFailure` (`src\BhMaps.Core\Model\Results.cs:4,6`).

Produces:

```csharp
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

public static class PackCopier
{
    public const string PacksFolderName = "packs";
    public const string BackgroundsFolder = "Backgrounds";
    public const string BackgroundExtension = ".jpg";

    public static string LibraryRelative(Pack pack, string relativePath);
    public static MapEntry? MapForSlot(MapCatalog catalog, string slot);
    public static IReadOnlyList<string> MapFiles(Pack pack, MapEntry map, MapCatalog catalog);
    public static IReadOnlyList<string> Touched(Pack pack, IEnumerable<string> relativePaths);
    public static PackCopyResult CopyMap(Pack source, Pack target, MapEntry map, MapCatalog catalog, bool replace);
    public static PackCopyResult CopyFile(Pack source, Pack target, string relativePath, bool replace);
    public static PackCopyResult RemoveMap(Pack pack, MapEntry map, MapCatalog catalog);
}
```

- [ ] **Step 1: Tests.** Create `tests\BhMaps.Core.Tests\PackCopierTests.cs` in `PackExporterTests`' style: a private `Arrange` that builds two packs under `<tmp>\lib\packs` with `FakeGameTree` and reads them back with `PackScanner.ScanAll(lib)`, and a catalog built by hand.

```csharp
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Packs;
using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class PackCopierTests
{
    /// <summary>One map, one slot. The catalog is built by hand because the copier reads nothing else off it.</summary>
    private static MapCatalog Catalog() =>
        MapCatalogTests.CatalogOf(
            new MapEntry("BloodMoon", "Blood Moon", LevelXml.Desc("BloodMoon"), [], [], ["BG_BloodMoon.jpg"], []));

    private static (string Library, Pack Source, Pack Target) Arrange(TempDir tmp, bool targetHasMap)
    {
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(Path.Combine(lib, "packs", "flower"))
            .File("BloodMoon", "a.png", "flower-a")
            .File("BloodMoon", "b.png", "flower-b")
            .File("Backgrounds", "BG_BloodMoon.jpg", "flower-bg")
            .File("Backgrounds", "BG_Other.jpg", "flower-other");
        var target = new FakeGameTree(Path.Combine(lib, "packs", "stone"));
        if (targetHasMap)
        {
            target.File("BloodMoon", "a.png", "stone-a");
        }
        else
        {
            target.Folder("Backgrounds");
        }

        var packs = PackScanner.ScanAll(lib);
        return (lib,
            packs.Single(p => p.Name == "flower"),
            packs.Single(p => p.Name == "stone"));
    }

    [Fact]
    public void MapFiles_lists_the_folder_and_the_slot_the_map_owns()
    {
        using var tmp = new TempDir();
        var (_, source, _) = Arrange(tmp, targetHasMap: false);

        var files = PackCopier.MapFiles(source, Catalog().Maps[0], Catalog());

        Assert.Equal(
            [Path.Combine("BloodMoon", "a.png"), Path.Combine("BloodMoon", "b.png"), Path.Combine("Backgrounds", "BG_BloodMoon.jpg")],
            files);
    }

    [Fact]
    public void CopyMap_writes_the_files_and_names_them_library_relative()
    {
        using var tmp = new TempDir();
        var (lib, source, target) = Arrange(tmp, targetHasMap: false);

        var result = PackCopier.CopyMap(source, target, Catalog().Maps[0], Catalog(), replace: false);

        Assert.False(result.Skipped);
        Assert.Empty(result.Failures);
        Assert.Empty(result.Removed);
        Assert.Contains(Path.Combine("packs", "stone", "BloodMoon", "a.png"), result.Written);
        Assert.Equal("flower-a", File.ReadAllText(Path.Combine(lib, "packs", "stone", "BloodMoon", "a.png")));
        Assert.Equal("flower-bg", File.ReadAllText(Path.Combine(lib, "packs", "stone", "Backgrounds", "BG_BloodMoon.jpg")));
        Assert.False(File.Exists(Path.Combine(lib, "packs", "stone", "Backgrounds", "BG_Other.jpg")));
    }

    [Fact]
    public void CopyMap_skips_a_map_the_target_already_has()
    {
        using var tmp = new TempDir();
        var (lib, source, target) = Arrange(tmp, targetHasMap: true);

        var result = PackCopier.CopyMap(source, target, Catalog().Maps[0], Catalog(), replace: false);

        Assert.True(result.Skipped);
        Assert.Empty(result.Written);
        Assert.Equal("stone-a", File.ReadAllText(Path.Combine(lib, "packs", "stone", "BloodMoon", "a.png")));
    }

    [Fact]
    public void CopyMap_with_replace_removes_the_targets_copy_first()
    {
        using var tmp = new TempDir();
        var (lib, source, target) = Arrange(tmp, targetHasMap: true);
        File.WriteAllText(Path.Combine(lib, "packs", "stone", "BloodMoon", "stale.png"), "stone-stale");
        target = PackScanner.ScanAll(lib).Single(p => p.Name == "stone");

        var result = PackCopier.CopyMap(source, target, Catalog().Maps[0], Catalog(), replace: true);

        Assert.False(result.Skipped);
        Assert.Contains(Path.Combine("packs", "stone", "BloodMoon", "stale.png"), result.Removed);
        Assert.False(File.Exists(Path.Combine(lib, "packs", "stone", "BloodMoon", "stale.png")));
        Assert.Equal("flower-a", File.ReadAllText(Path.Combine(lib, "packs", "stone", "BloodMoon", "a.png")));
    }

    [Fact]
    public void CopyMap_merges_the_record_entries_into_the_target()
    {
        using var tmp = new TempDir();
        var (lib, source, target) = Arrange(tmp, targetHasMap: false);
        var platforms = new PlatformEditRecord();
        platforms.SetMap(
            "BloodMoon",
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, PlatformPieceEntry> { [Path.Combine("BloodMoon", "a.png")] = new() { Hue = 30 } });
        platforms.Save(source.FullPath);
        var backgrounds = new BackgroundEditRecord();
        backgrounds.Set(Path.Combine("Backgrounds", "BG_BloodMoon.jpg"), new BackgroundSlotEntry { Picture = "x.jpg", Darken = 0.5 });
        backgrounds.Save(source.FullPath);
        source = PackScanner.ScanAll(lib).Single(p => p.Name == "flower");

        var result = PackCopier.CopyMap(source, target, Catalog().Maps[0], Catalog(), replace: false);

        var copiedPlatforms = PlatformEditRecord.Load(target.FullPath);
        var copiedBackgrounds = BackgroundEditRecord.Load(target.FullPath);
        Assert.Equal(30, copiedPlatforms.Entry("BloodMoon", Path.Combine("BloodMoon", "a.png"))!.Hue);
        Assert.Equal(0.5, copiedBackgrounds.Entry(Path.Combine("Backgrounds", "BG_BloodMoon.jpg"))!.Darken);
        Assert.Contains(Path.Combine("packs", "stone", PlatformEditRecord.FileName), result.Written);
        Assert.Contains(Path.Combine("packs", "stone", BackgroundEditRecord.FileName), result.Written);
    }

    [Fact]
    public void CopyFile_copies_one_background_and_its_entry()
    {
        using var tmp = new TempDir();
        var (lib, source, target) = Arrange(tmp, targetHasMap: false);

        var result = PackCopier.CopyFile(source, target, Path.Combine("Backgrounds", "BG_Other.jpg"), replace: false);

        Assert.False(result.Skipped);
        Assert.Equal("flower-other", File.ReadAllText(Path.Combine(lib, "packs", "stone", "Backgrounds", "BG_Other.jpg")));
        Assert.Single(result.Written.Where(w => w.EndsWith("BG_Other.jpg", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void CopyFile_skips_a_file_the_target_already_has()
    {
        using var tmp = new TempDir();
        var (lib, source, target) = Arrange(tmp, targetHasMap: false);
        File.WriteAllText(Path.Combine(lib, "packs", "stone", "Backgrounds", "BG_Other.jpg"), "stone-other");

        var result = PackCopier.CopyFile(source, target, Path.Combine("Backgrounds", "BG_Other.jpg"), replace: false);

        Assert.True(result.Skipped);
        Assert.Equal("stone-other", File.ReadAllText(Path.Combine(lib, "packs", "stone", "Backgrounds", "BG_Other.jpg")));
    }

    [Fact]
    public void RemoveMap_deletes_the_files_and_the_entries()
    {
        using var tmp = new TempDir();
        var (lib, source, _) = Arrange(tmp, targetHasMap: false);
        var platforms = new PlatformEditRecord();
        platforms.SetMap("BloodMoon", DateTimeOffset.UnixEpoch, new Dictionary<string, PlatformPieceEntry>());
        platforms.Save(source.FullPath);

        var result = PackCopier.RemoveMap(source, Catalog().Maps[0], Catalog());

        Assert.Empty(result.Failures);
        Assert.Contains(Path.Combine("packs", "flower", "BloodMoon", "a.png"), result.Removed);
        Assert.False(Directory.Exists(Path.Combine(lib, "packs", "flower", "BloodMoon")));
        Assert.Equal("flower-other", File.ReadAllText(Path.Combine(lib, "packs", "flower", "Backgrounds", "BG_Other.jpg")));
        Assert.Null(PlatformEditRecord.Load(source.FullPath).Map("BloodMoon"));
    }
}
```

`MapCatalogTests.CatalogOf` and `LevelXml.Desc` may not exist in that shape. Check `tests\BhMaps.Core.Tests\MapCatalogTests.cs` and `Helpers\LevelXml.cs` first and use whatever those files already offer to build a `MapCatalog` with one `MapEntry`; if there is no such helper, add `internal static MapCatalog CatalogOf(params MapEntry[] maps)` to `MapCatalogTests` using the catalog's existing public construction path, and nothing else.

- [ ] **Step 2: Run, expect failure. Implement `PackCopier.cs`.**

```csharp
using BhMaps.Core.Maps;
using BhMaps.Core.Model;

namespace BhMaps.Core.Packs;

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
```

`PackCopyResult` goes in the same file, above the class, exactly as the Interfaces block gives it.

- [ ] **Step 3: Run all Core tests. Format, build. Commit** "Packs: PackCopier copies and removes one map or one picture".

---

## Task 2: Core move, duplicate and import

Spec 4.1 (`MoveMap`, `MoveFile`, `DuplicatePack`), spec 5.1 (`PackImportPlan`, `PackImportResult`, `Import`).

**Files:**
- Modify: `src\BhMaps.Core\Packs\PackCopier.cs` (append to the class)
- Test: `tests\BhMaps.Core.Tests\PackCopierTests.cs` (append)

**Interfaces:**

Consumes: everything Task 1 produced; `PackNameValidator` (`src\BhMaps.Core\Operations\PackNameValidator.cs`) is **not** used, because "{name} copy" is generated, not typed.

Produces:

```csharp
public sealed record PackImportPlan(
    Pack Source, Pack Target, IReadOnlyList<MapEntry> Maps, IReadOnlyList<string> LooseFiles, bool Replace);

/// <summary>What an import did. Skipped holds display names and file names, for the done line.</summary>
public sealed record PackImportResult(
    IReadOnlyList<string> Written,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<FileFailure> Failures);

// on PackCopier
public static PackCopyResult MoveMap(Pack source, Pack target, MapEntry map, MapCatalog catalog, bool replace);
public static PackCopyResult MoveFile(Pack source, Pack target, string relativePath, bool replace);
public static string FreeCopyName(string libraryPath, string name);
public static string DuplicatePack(string libraryPath, string name);
public static PackImportResult Import(
    PackImportPlan plan, MapCatalog catalog, IProgress<string>? progress, CancellationToken ct);
```

- [ ] **Step 1: Tests.** Append to `PackCopierTests`:

```csharp
    [Fact]
    public void MoveMap_copies_then_clears_the_source()
    {
        using var tmp = new TempDir();
        var (lib, source, target) = Arrange(tmp, targetHasMap: false);

        var result = PackCopier.MoveMap(source, target, Catalog().Maps[0], Catalog(), replace: false);

        Assert.False(result.Skipped);
        Assert.Equal("flower-a", File.ReadAllText(Path.Combine(lib, "packs", "stone", "BloodMoon", "a.png")));
        Assert.False(Directory.Exists(Path.Combine(lib, "packs", "flower", "BloodMoon")));
        Assert.False(File.Exists(Path.Combine(lib, "packs", "flower", "Backgrounds", "BG_BloodMoon.jpg")));
        Assert.Contains(Path.Combine("packs", "flower", "BloodMoon", "a.png"), result.Removed);
    }

    [Fact]
    public void MoveMap_that_was_skipped_leaves_the_source_alone()
    {
        using var tmp = new TempDir();
        var (lib, source, target) = Arrange(tmp, targetHasMap: true);

        var result = PackCopier.MoveMap(source, target, Catalog().Maps[0], Catalog(), replace: false);

        Assert.True(result.Skipped);
        Assert.True(File.Exists(Path.Combine(lib, "packs", "flower", "BloodMoon", "a.png")));
    }

    [Fact]
    public void FreeCopyName_counts_up_past_the_names_that_are_taken()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        Directory.CreateDirectory(Path.Combine(lib, "packs", "flower"));
        Directory.CreateDirectory(Path.Combine(lib, "packs", "Flower copy"));
        Directory.CreateDirectory(Path.Combine(lib, "packs", "flower copy 2"));

        Assert.Equal("flower copy 3", PackCopier.FreeCopyName(lib, "flower"));
    }

    [Fact]
    public void DuplicatePack_copies_the_whole_folder_to_the_free_name()
    {
        using var tmp = new TempDir();
        var (lib, source, _) = Arrange(tmp, targetHasMap: false);
        File.WriteAllText(Path.Combine(source.FullPath, PlatformEditRecord.FileName), "{\"version\":1,\"maps\":{}}");

        var copy = PackCopier.DuplicatePack(lib, "flower");

        Assert.Equal("flower copy", copy);
        Assert.Equal("flower-a", File.ReadAllText(Path.Combine(lib, "packs", "flower copy", "BloodMoon", "a.png")));
        Assert.True(File.Exists(Path.Combine(lib, "packs", "flower copy", PlatformEditRecord.FileName)));
    }

    [Fact]
    public void Import_copies_every_map_and_reports_the_ones_it_skipped()
    {
        using var tmp = new TempDir();
        var (lib, source, target) = Arrange(tmp, targetHasMap: true);
        var progress = new SyncProgress();
        var plan = new PackImportPlan(
            source, target, [Catalog().Maps[0]], [Path.Combine("Backgrounds", "BG_Other.jpg")], Replace: false);

        var result = PackCopier.Import(plan, Catalog(), progress, CancellationToken.None);

        Assert.Equal(["Blood Moon"], result.Skipped);
        Assert.Equal("stone-a", File.ReadAllText(Path.Combine(lib, "packs", "stone", "BloodMoon", "a.png")));
        Assert.Equal("flower-other", File.ReadAllText(Path.Combine(lib, "packs", "stone", "Backgrounds", "BG_Other.jpg")));
        Assert.Contains("Importing 1 of 2: Blood Moon", progress.Messages);
    }

    [Fact]
    public void Import_with_replace_overwrites_what_the_target_had()
    {
        using var tmp = new TempDir();
        var (lib, source, target) = Arrange(tmp, targetHasMap: true);
        var plan = new PackImportPlan(source, target, [Catalog().Maps[0]], [], Replace: true);

        var result = PackCopier.Import(plan, Catalog(), null, CancellationToken.None);

        Assert.Empty(result.Skipped);
        Assert.Equal("flower-a", File.ReadAllText(Path.Combine(lib, "packs", "stone", "BloodMoon", "a.png")));
    }

    [Fact]
    public void Import_stops_between_items_when_cancelled()
    {
        using var tmp = new TempDir();
        var (_, source, target) = Arrange(tmp, targetHasMap: false);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var plan = new PackImportPlan(source, target, [Catalog().Maps[0]], [], Replace: false);

        Assert.Throws<OperationCanceledException>(
            () => PackCopier.Import(plan, Catalog(), null, cts.Token));
    }
```

Check `tests\BhMaps.Core.Tests\Helpers\SyncProgress.cs` for the real name of its collected-messages property and use that.

- [ ] **Step 2: Run, expect failure. Implement.** Append to `PackCopier`:

```csharp
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
```

- [ ] **Step 3: Run all Core tests. Format, build. Commit** "Packs: PackCopier moves, duplicates and imports".

---

## Task 3: The library write boundary, its done action, and empty-folder pruning on undo

Spec 4.2 and conflicts 1, 2 and 8 above.

**Files:**
- Modify: `src\BhMaps.Core\Operations\UndoStore.cs` (`RestoreSides` :228-298)
- Modify: `src\BhMaps.App\ViewModels\MainViewModel.cs` (after `SetLibraryDone` :1042)
- Modify: `src\BhMaps.App\Views\Controls\PageHeader.xaml` (:74-84)
- Test: `tests\BhMaps.Core.Tests\UndoStoreTests.cs`

**Interfaces:**

Consumes: `UndoSession.CaptureLibrary` (`UndoStore.cs:65`), `UndoStore.Restore(session, gamePath, libraryPath)` (`UndoStore.cs:220`), `MainViewModel.RunBusyAsync` (`MainViewModel.cs:986`), `MainViewModel.RescanAsync` (`MainViewModel.cs:851`).

Produces:

```csharp
// MainViewModel
public async Task<bool> RunLibraryWriteAsync(
    string label,
    IReadOnlyList<string> libraryUndoPaths,
    Func<IProgress<string>, CancellationToken, Task> work,
    string doneText,
    IReadOnlyList<string>? writtenFolders = null);

public void SetLibraryDone(string doneText, string actionText, IRelayCommand action);

[ObservableProperty] public partial string DoneActionText { get; set; }
[ObservableProperty] public partial IRelayCommand? DoneActionCommand { get; set; }
public bool HasDoneAction => DoneActionText.Length > 0 && DoneActionCommand is not null;
```

- [ ] **Step 1: Tests.** In `UndoStoreTests`, following the file's existing arrange style:

```csharp
    [Fact]
    public void Restore_deletes_a_library_file_that_was_absent_and_the_folder_it_left_empty()
    {
        using var tmp = new TempDir();
        var library = Path.Combine(tmp.Path, "lib");
        var game = Path.Combine(tmp.Path, "game");
        Directory.CreateDirectory(game);
        var store = new UndoStore(Path.Combine(tmp.Path, "appdata"));
        var relative = Path.Combine("packs", "flower copy", "BloodMoon", "a.png");
        var session = store.Begin();
        session.CaptureLibrary(library, [relative]);
        Directory.CreateDirectory(Path.Combine(library, "packs", "flower copy", "BloodMoon"));
        File.WriteAllText(Path.Combine(library, relative), "copied");

        var result = store.Restore(session, game, library);

        Assert.Empty(result.Failures);
        Assert.False(File.Exists(Path.Combine(library, relative)));
        Assert.False(Directory.Exists(Path.Combine(library, "packs", "flower copy")));
        Assert.True(Directory.Exists(Path.Combine(library, "packs")));
    }

    [Fact]
    public void Restore_keeps_a_folder_that_still_holds_a_file()
    {
        using var tmp = new TempDir();
        var library = Path.Combine(tmp.Path, "lib");
        var game = Path.Combine(tmp.Path, "game");
        Directory.CreateDirectory(game);
        var store = new UndoStore(Path.Combine(tmp.Path, "appdata"));
        var relative = Path.Combine("packs", "stone", "BloodMoon", "a.png");
        var session = store.Begin();
        session.CaptureLibrary(library, [relative]);
        Directory.CreateDirectory(Path.Combine(library, "packs", "stone", "BloodMoon"));
        File.WriteAllText(Path.Combine(library, relative), "copied");
        File.WriteAllText(Path.Combine(library, "packs", "stone", "BloodMoon", "kept.png"), "kept");

        store.Restore(session, game, library);

        Assert.True(Directory.Exists(Path.Combine(library, "packs", "stone", "BloodMoon")));
    }
```

- [ ] **Step 2: Run, expect failure. Implement the prune in `UndoStore.RestoreSides`.** Inside the `if (libraryPath is not null)` block, after the `foreach (var relativePath in libraryAbsent)` loop:

```csharp
            // A library file that was absent when the session began was made by the write this restore is
            // undoing, and so were the folders it needed. Pruning them upwards is what makes undo of a duplicate
            // or an import leave the library the shape it was, rather than a pack folder with nothing in it.
            PruneEmptyFolders(libraryPath, libraryAbsent);
```

and, beside `Clear()`:

```csharp
    /// <summary>Deletes the folders of these relative paths, and their parents, while they are empty and still
    /// inside root. Never touches root itself. A folder we cannot delete stops that path and nothing else.</summary>
    private static void PruneEmptyFolders(string root, IReadOnlyList<string> relativePaths)
    {
        var stop = Path.GetFullPath(root);
        foreach (var relativePath in relativePaths)
        {
            var directory = System.IO.Path.GetDirectoryName(Path.GetFullPath(Path.Combine(root, relativePath)));
            while (directory is not null
                && !directory.Equals(stop, StringComparison.OrdinalIgnoreCase)
                && directory.StartsWith(stop + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(directory))
            {
                try
                {
                    if (Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        break;
                    }

                    Directory.Delete(directory);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    break;
                }

                directory = System.IO.Path.GetDirectoryName(directory);
            }
        }
    }
```

Use the same `System.IO.Path` qualification the file already uses where `Path` is shadowed by `UndoSession.Path`; inside `UndoStore` plain `Path` is fine, so match whichever the surrounding lines use.

- [ ] **Step 3: Implement `RunLibraryWriteAsync` and the done action.** In `MainViewModel`, after `SetLibraryDone`:

```csharp
    /// <summary>The line a library-only operation leaves when it offers something other than Undo: spec 2.6 4.3's
    /// "Open {target}" beside a plain copy.</summary>
    public void SetLibraryDone(string doneText, string actionText, IRelayCommand action)
    {
        DoneText = doneText;
        DoneUndoable = false;
        DoneActionText = actionText;
        DoneActionCommand = action;
    }

    /// <summary>Spec 2.6 4.2: one write that touches the library and not the game. The busy boundary, an undo
    /// session holding the library paths the work is about to write or remove, a done line with Undo beside it,
    /// and a rescan. No launcher and no game side: nothing lands in the game folder, so the game may keep
    /// running and a missing game folder does not stop it.</summary>
    public async Task<bool> RunLibraryWriteAsync(
        string label,
        IReadOnlyList<string> libraryUndoPaths,
        Func<IProgress<string>, CancellationToken, Task> work,
        string doneText,
        IReadOnlyList<string>? writtenFolders = null)
    {
        var libraryPath = Services.LibraryPath;
        var undoable = libraryUndoPaths.Count > 0;
        var ok = await RunBusyAsync(
            label,
            async (progress, ct) =>
            {
                if (undoable)
                {
                    // The capture is the first step of the work, as it is on the game side: file copying behind a
                    // progress line, inside the boundary that turns an IO failure into the usual dialog.
                    progress.Report("Saving undo");
                    await Task.Run(
                        () =>
                        {
                            var session = Services.Undo.Begin();
                            session.CaptureLibrary(libraryPath, libraryUndoPaths);
                        },
                        ct);
                }

                await work(progress, ct);
            });

        // Begin has already replaced the previous snapshot, so a write that failed clears the line with it.
        DoneText = ok ? doneText : "";
        DoneUndoable = ok && undoable;
        DoneActionText = "";
        DoneActionCommand = null;
        CanUndo = Services.Undo.Latest is not null;
        await RescanAsync(writtenFolders);
        return ok;
    }
```

Declare the two observable properties beside `DoneUndoable` (`MainViewModel.cs:137`):

```csharp
    /// <summary>The words on the one button a library line may offer instead of Undo, "" for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDoneAction))]
    public partial string DoneActionText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDoneAction))]
    public partial IRelayCommand? DoneActionCommand { get; set; }

    public bool HasDoneAction => DoneActionText.Length > 0 && DoneActionCommand is not null;
```

`SetLibraryDone(string)` gains two lines so the old one-argument calls clear a stale action:

```csharp
    public void SetLibraryDone(string doneText)
    {
        DoneText = doneText;
        DoneUndoable = false;
        DoneActionText = "";
        DoneActionCommand = null;
    }
```

- [ ] **Step 4: The button.** In `PageHeader.xaml`, after the Undo button (:84):

```xml
        <!-- Spec 2.6 4.3: a plain copy has no snapshot behind it and offers the target instead of Undo. -->
        <Button Margin="8,0,0,0"
                Command="{Binding DoneActionCommand}"
                Content="{Binding DoneActionText}"
                FocusVisualStyle="{StaticResource DialogFocusRing}"
                Style="{StaticResource PlainButton}"
                Visibility="{Binding HasDoneAction, Converter={StaticResource BoolToVis}}" />
```

- [ ] **Step 5: Run all Core tests. Format, build. Commit** "Packs: a library write boundary with undo and a done action".

---

## Task 4: Every pack detail tile has a menu

Spec 3.

**Files:**
- Modify: `src\BhMaps.App\ViewModels\Pages\PackDetailViewModel.cs` (`BuildTileMenu` :148-186, `MapForSlot` :445, `PackTileViewModel` :565-620)
- Modify: `src\BhMaps.App\Views\Pages\PackDetailView.xaml.cs` (`OnTileMenuOpening` :70-80, `OnTileMenuButton` :84-98)
- Modify: `src\BhMaps.App\ViewModels\TileMenuCommand.cs` (:19-36)
- Modify: `src\BhMaps.App\Theme\Controls.xaml` (`TileMenuItem` HeaderTemplate :604-612)

**Interfaces:**

Consumes: `PackCopier.MapForSlot`, `PackCopier.MapFiles`, `PackCopier.Touched`, `PackCopier.RemoveMap` (Task 1); `MainViewModel.RunLibraryWriteAsync` (Task 3); `MainViewModel.ApplySetAsync(Pack, IReadOnlyList<MapEntry>, bool)` (`MainViewModel.cs:463`); `DefaultPack.Name`.

Produces:

```csharp
// TileMenuCommand gains a trailing optional parameter, so every existing construction compiles:
public sealed record TileMenuCommand(
    string Text, ICommand? Command, IReadOnlyList<TileMenuCommand>? Children = null, bool IsEnabled = true,
    string? ToolTip = null, TileMenuKind Kind = TileMenuKind.Item, string? Gesture = null);

// PackTileViewModel
public string? FolderPath { get; set; }   // the map's folder in the pack, null on a file tile

// PackDetailViewModel
public void BuildTileMenu(PackTileViewModel tile);           // always fills MenuItems
private Task RemoveMapFromPackAsync(MapEntry map);
```

- [ ] **Step 1: The gesture column.** In `TileMenuCommand`, add `string? Gesture = null` as the last positional parameter with the comment "Spec 2.6 section 3: the key the line answers to, drawn muted on the right. Null on a line with no key." In `Controls.xaml`, change the `TileMenuItem` Header setter and template so the gesture can be drawn:

```xml
    <Setter Property="Header" Value="{Binding}" />

    <!-- A file or pack name reads exactly as typed. Without a template the header string is drawn through
         AccessText, which eats the underscore in "BG_Dojo.jpg" and underlines the D instead; no menu in spec
         section 11 offers an access key, so no menu needs one. The header is the line itself, so the template
         can draw its gesture beside its words. -->
    <Setter Property="HeaderTemplate">
      <Setter.Value>
        <DataTemplate>
          <Grid>
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width="*" />
              <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <TextBlock Text="{Binding Text}" />
            <TextBlock Grid.Column="1" Margin="24,0,0,0" Foreground="{StaticResource Text3Brush}"
                       Text="{Binding Gesture}" />
          </Grid>
        </DataTemplate>
      </Setter.Value>
    </Setter>
```

- [ ] **Step 2: The tile knows its folder.** In `PackTileViewModel`, beside `PicturePath`:

```csharp
    /// <summary>The map's folder inside the pack, so Show in folder on a map tile has something to open. Null on
    /// a file tile, whose picture is what Show in folder reveals.</summary>
    public string? FolderPath { get; set; }
```

In `Rebuild` (:396), the map tile construction becomes:

```csharp
        foreach (var map in MapsIn(snapshot.Catalog, pack))
        {
            Items.Add(new PackTileViewModel(
                map.FolderName, map.DisplayName, map, null, MapCompositor.CardWidth, MapCompositor.CardHeight)
            { FolderPath = pack.FindFolder(map.FolderName)?.FullPath });
        }
```

and the branch that makes a tile for an owned background (:420-430) sets `FolderPath` the same way when it creates one.

- [ ] **Step 3: The menu.** Replace `BuildTileMenu` with:

```csharp
    /// <summary>Spec 2.6 section 3: every tile has a menu. A map tile leads with the map, a file tile with the
    /// file name, and both end with the lines that move the tile between packs. Built when the menu opens, so
    /// the ticked count is the one the user can see.</summary>
    public void BuildTileMenu(PackTileViewModel tile)
    {
        if (Pack is not { } pack)
        {
            tile.MenuItems = [];
            return;
        }

        var isDefault = pack.Name.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase);
        var items = new List<TileMenuCommand>();
        if (tile.Map is { } owner)
        {
            items.Add(TileMenuCommand.Header(owner.DisplayName));
            items.Add(new TileMenuCommand(
                $"Apply to {owner.DisplayName}",
                new AsyncRelayCommand(() => Shell.ApplySetAsync(pack, [owner], clearTicks: false)),
                IsEnabled: Shell.CanWrite));
        }
        else
        {
            items.Add(TileMenuCommand.Header(tile.Caption));
        }

        if (tile.PicturePath is { } path)
        {
            AddPictureLines(items, tile, pack, path);
        }

        items.Add(new TileMenuCommand(
            "Copy to pack...", new AsyncRelayCommand(() => CopyTileAsync(tile, cut: false)), Gesture: "Ctrl+C"));
        if (!isDefault)
        {
            items.Add(new TileMenuCommand(
                "Move to pack...", new AsyncRelayCommand(() => CopyTileAsync(tile, cut: true)), Gesture: "Ctrl+X"));
        }

        if (tile.PicturePath ?? tile.FolderPath is { })
        {
            items.Add(new TileMenuCommand(
                "Show in folder", new RelayCommand(() => ShowInFolder(tile.PicturePath ?? tile.FolderPath!))));
        }

        if (tile.Map is { } removable && !isDefault)
        {
            items.Add(TileMenuCommand.Separator());
            items.Add(new TileMenuCommand(
                $"Remove from {pack.Name}", new AsyncRelayCommand(() => RemoveMapFromPackAsync(removable))));
        }
        else if (tile.Map is null && tile.PicturePath is { } file)
        {
            items.Add(TileMenuCommand.Separator());
            items.Add(new TileMenuCommand(
                $"Remove from {pack.Name}",
                new AsyncRelayCommand(() => RemoveFromPackAsync(file, Path.GetFileName(file)))));
        }

        tile.MenuItems = items;
    }

    /// <summary>The 2.5 picture lines, in their order: the ticked apply, the chooser, every map, then Edit. The
    /// per-map apply of 2.5 is gone: on a map tile Apply to {map} above it already names that map.</summary>
    private void AddPictureLines(List<TileMenuCommand> items, PackTileViewModel tile, Pack pack, string path)
    {
        var name = Path.GetFileName(path);
        var ticked = Shell.SelectedMapCount;
        var slot = tile.Map?.BackgroundSlots.FirstOrDefault();
        if (ticked > 0)
        {
            var text = ticked == 1 ? "Apply to the 1 selected map" : $"Apply to the {ticked} selected maps";
            items.Add(new TileMenuCommand(
                text,
                new AsyncRelayCommand(
                    () => Shell.ApplyPictureAsync(path, Shell.SelectedMaps, true, name, pack.Name))));
        }

        items.Add(new TileMenuCommand(
            "Apply to a map...", new AsyncRelayCommand(() => ApplyToChosenMapAsync(path, name, pack.Name))));
        items.Add(new TileMenuCommand(
            "Apply to all maps", new AsyncRelayCommand(() => ApplyToAllMapsAsync(path, name, pack.Name))));
        items.Add(TileMenuCommand.Separator());
        items.Add(new TileMenuCommand(
            "Edit",
            new AsyncRelayCommand(
                () => Shell.OpenBackgroundEditorAsync(new BackgroundEditorRequest(path, pack.Name, slot)))));
    }
```

Fix the `tile.PicturePath ?? tile.FolderPath is { }` line while writing it: the guard is `if (tile.PicturePath is not null || tile.FolderPath is not null)`, and the command uses `tile.PicturePath ?? tile.FolderPath!`.

- [ ] **Step 4: Remove from pack for a map tile.**

```csharp
    /// <summary>Spec 2.6 section 3 item 7: the map's files in this pack, deleted together, inside a library undo
    /// session so the confirm is the only thing standing between the user and getting them back.</summary>
    private async Task RemoveMapFromPackAsync(MapEntry map)
    {
        if (Pack is not { } pack || _snapshot is not { } snapshot)
        {
            return;
        }

        var count = PackCopier.MapFiles(pack, map, snapshot.Catalog).Count;
        if (!Shell.Dialogs.Confirm(
                $"Remove from {pack.Name}?",
                $"{map.DisplayName} and its {PackRowViewModel.Plural(count, "file")} are removed from {pack.Name}. The game keeps whatever is applied until you apply something else."))
        {
            return;
        }

        var catalog = snapshot.Catalog;
        PackCopyResult? result = null;
        await Shell.RunLibraryWriteAsync(
            $"Removing {map.DisplayName}",
            PackCopier.Touched(pack, PackCopier.MapFiles(pack, map, catalog)),
            (_, ct) => Task.Run(() => { result = PackCopier.RemoveMap(pack, map, catalog); }, ct),
            $"{map.DisplayName} removed from {pack.Name}");
        if (result is not null)
        {
            Shell.Dialogs.ShowFailures("Some files could not be removed", result.Failures);
        }
    }
```

Replace the page's private `MapForSlot` (:445) with a call to `PackCopier.MapForSlot(catalog, file.Name)` in `Rebuild` and delete the private copy, so there is one rule.

- [ ] **Step 5: The empty-menu branches go.** In `PackDetailView.xaml.cs`, `OnTileMenuOpening` becomes:

```csharp
    /// <summary>Spec 2.6 section 3: the lines are built here, not when the ticks change, so the ticked line names
    /// the count the user can see. Every tile has a menu now, so nothing is handled away.</summary>
    private void OnTileMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PackTileViewModel tile }
            && DataContext is PackDetailViewModel page)
        {
            page.BuildTileMenu(tile);
        }
    }
```

and `OnTileMenuButton`:

```csharp
    private void OnTileMenuButton(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PackTileViewModel tile })
        {
            Page?.BuildTileMenu(tile);
        }

        TileMenus.OpenFor(sender);
    }
```

`CopyTileAsync` does not exist until Task 5. Write it in this task as a one-line stub that Task 5 replaces? No: build order is Task 5 before this page compiles. **Do Task 5 Step 1 and Step 2 (the chooser and `CopyTileAsync`) in the same working tree before compiling**, and commit Task 4 and Task 5 separately only if the tree builds at each commit; otherwise commit Task 4 and Task 5 together with the Task 5 message.

- [ ] **Step 6: Format, build, run all Core tests. Commit** "Pack detail: every tile has a menu".

---

## Task 5: The pack chooser, Copy to pack and Move to pack

Spec 4.3 first three bullets.

**Files:**
- Modify: `src\BhMaps.App\ViewModels\ChooserViewModel.cs` (`ChooserRow` :9-33)
- Modify: `src\BhMaps.App\ViewModels\MainViewModel.cs` (beside `ChoosePictureAsync` :627-650)
- Modify: `src\BhMaps.App\ViewModels\Pages\PackDetailViewModel.cs`

**Interfaces:**

Consumes: `ShowChooser(ChooserViewModel)` (`MainViewModel.cs:653`), `PackCreator.TryCreate` (`src\BhMaps.Core\Operations\PackCreator.cs:12`), `Dialogs.PromptText`, `PackCopier.CopyMap/MoveMap/CopyFile/MoveFile/Touched/MapFiles`, `RunLibraryWriteAsync`.

Produces:

```csharp
// ChooserRow gains a trailing optional parameter
public ChooserRow(string name, string detail, string path, MapEntry? map, bool isNewPack = false);
public bool IsNewPack { get; }

// MainViewModel
public async Task<Pack?> ChoosePackAsync(string title, Pack exclude);
public async Task<Pack?> NewPackAsync();          // the existing new-pack flow, returning the pack it made

// PackDetailViewModel
private Task CopyTileAsync(PackTileViewModel tile, bool cut);
public Task PasteIntoAsync(Pack source, PackTileViewModel tile, bool cut);   // used by Task 6
```

- [ ] **Step 1: The chooser row and the pack chooser.** Add to `ChooserRow`:

```csharp
    /// <summary>The last line of the pack chooser (spec 2.6 4.3), which makes a pack rather than picking one.</summary>
    public bool IsNewPack { get; }
```

set from a new trailing constructor parameter `bool isNewPack = false`.

In `MainViewModel`, after `ChoosePictureAsync`:

```csharp
    /// <summary>Spec 2.6 4.3: pick one pack, in the map chooser's window. The pack the tile is already in is
    /// left out, and the last line makes a new one and returns it. Null when the window was cancelled.</summary>
    public async Task<Pack?> ChoosePackAsync(string title, Pack exclude)
    {
        if (Snapshot is not { } snapshot)
        {
            return null;
        }

        var rows = new List<ChooserRow>();
        foreach (var pack in snapshot.Packs.Where(
                     p => !p.Name.Equals(exclude.Name, StringComparison.OrdinalIgnoreCase)))
        {
            var maps = snapshot.Catalog.Maps.Count(m => pack.FindFolder(m.FolderName) is { Files.Count: > 0 });
            var backgrounds = pack.FindFolder(PackCopier.BackgroundsFolder)?.Files.Count ?? 0;
            var lead = (pack.FindFolder(PackCopier.BackgroundsFolder)?.Files ?? Array.Empty<GameFile>())
                .FirstOrDefault(f => Path.GetExtension(f.Name)
                    .Equals(PackCopier.BackgroundExtension, StringComparison.OrdinalIgnoreCase));
            rows.Add(new ChooserRow(
                pack.Name,
                $"{Count(maps, "map")}, {Count(backgrounds, "background")}",
                lead?.FullPath ?? "",
                null));
        }

        rows.Add(new ChooserRow("New pack...", "", "", null, isNewPack: true));

        var vm = new ChooserViewModel(title, "One pack. Nothing is written to the game.", "pack", rows)
        {
            PrimaryPrefix = "Choose",
        };
        if (!ShowChooser(vm) || vm.Selected is not { } chosen)
        {
            return null;
        }

        return chosen.IsNewPack
            ? await NewPackAsync()
            : snapshot.Packs.FirstOrDefault(p => p.Name.Equals(chosen.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Spec 13's new-pack flow, as the Packs page runs it, returning the pack it made so a chooser can
    /// hand it straight back to the operation that asked for one. Null when the name was empty or refused.</summary>
    public async Task<Pack?> NewPackAsync()
    {
        var name = Dialogs.PromptText("New pack", "Name", "");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (!PackCreator.TryCreate(Services.LibraryPath, name.Trim(), out var error))
        {
            Dialogs.Error("Could not make the pack", error);
            return null;
        }

        await RescanAsync();
        return FindPack(name.Trim());
    }
```

`ChooserViewModel.LoadThumbnailsAsync` already catches `ArgumentException`, which is what `File.GetLastWriteTimeUtc("")` throws, so a pack with no jpg draws an empty thumbnail and nothing else happens. `PacksViewModel.NewPackAsync` (`PacksViewModel.cs:276`) becomes `private Task NewPackAsync() => Shell.NewPackAsync();` wrapped in a `[RelayCommand]`, so there is one implementation.

- [ ] **Step 2: Copy and move from the tile menu.** In `PackDetailViewModel`:

```csharp
    /// <summary>Spec 2.6 4.3: pick the target, then copy or move the tile into it. A copy that clashes offers
    /// Replace once; a move is always inside an undo session, a copy only when it replaces something.</summary>
    private async Task CopyTileAsync(PackTileViewModel tile, bool cut)
    {
        if (Pack is not { } pack)
        {
            return;
        }

        var name = tile.Map?.DisplayName ?? tile.Caption;
        var title = cut ? $"Move {name} to" : $"Copy {name} to";
        if (await Shell.ChoosePackAsync(title, pack) is { } target)
        {
            await PasteIntoAsync(pack, tile, cut, target);
        }
    }

    /// <summary>The write itself, shared by the menu lines and by Ctrl+V (spec 2.6 4.3). The first attempt never
    /// replaces; a Skipped result asks once and repeats with replace on, and that second attempt is captured for
    /// undo because it overwrites what the target held.</summary>
    public async Task PasteIntoAsync(Pack source, PackTileViewModel tile, bool cut, Pack target)
    {
        if (_snapshot is not { } snapshot
            || source.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var catalog = snapshot.Catalog;
        var name = tile.Map?.DisplayName ?? tile.Caption;
        var relative = tile.PicturePath is { } picture && tile.Map is null
            ? Path.Combine(PackCopier.BackgroundsFolder, Path.GetFileName(picture))
            : null;
        var files = tile.Map is { } map ? PackCopier.MapFiles(source, map, catalog) : [relative!];
        IReadOnlyList<string> undoPaths =
            [.. PackCopier.Touched(source, files), .. PackCopier.Touched(target, files)];

        var result = await RunPasteAsync(source, target, tile, catalog, cut, replace: false, name, undoPaths);
        if (result is { Skipped: true }
            && Shell.Dialogs.Confirm("Replace", $"{target.Name} already has {name}. Replace it?"))
        {
            result = await RunPasteAsync(source, target, tile, catalog, cut, replace: true, name, undoPaths);
        }

        if (result is not null)
        {
            Shell.Dialogs.ShowFailures("Some files could not be copied", result.Failures);
        }
    }

    /// <summary>One attempt. A plain copy that replaces nothing writes outside an undo session and offers the
    /// target instead of Undo (spec 4.2); everything else goes through the library write boundary.</summary>
    private async Task<PackCopyResult?> RunPasteAsync(
        Pack source,
        Pack target,
        PackTileViewModel tile,
        MapCatalog catalog,
        bool cut,
        bool replace,
        string name,
        IReadOnlyList<string> undoPaths)
    {
        PackCopyResult? result = null;
        var verb = cut ? "Moving" : "Copying";
        var done = cut ? $"{name} moved to {target.Name}" : $"{name} copied to {target.Name}";
        var relative = tile.Map is null && tile.PicturePath is { } picture
            ? Path.Combine(PackCopier.BackgroundsFolder, Path.GetFileName(picture))
            : null;

        Task Work(IProgress<string> progress, CancellationToken ct) => Task.Run(
            () =>
            {
                progress.Report(name);
                result = tile.Map is { } map
                    ? cut
                        ? PackCopier.MoveMap(source, target, map, catalog, replace)
                        : PackCopier.CopyMap(source, target, map, catalog, replace)
                    : cut
                        ? PackCopier.MoveFile(source, target, relative!, replace)
                        : PackCopier.CopyFile(source, target, relative!, replace);
            },
            ct);

        if (cut || replace)
        {
            await Shell.RunLibraryWriteAsync($"{verb} {name}", undoPaths, Work, done);
            return result;
        }

        var ok = await Shell.RunBusyAsync($"{verb} {name}", Work);
        if (ok && result is { Skipped: false })
        {
            var open = target;
            Shell.SetLibraryDone(done, $"Open {target.Name}", new RelayCommand(() => Shell.NavigateToPack(open)));
        }

        await Shell.RescanAsync();
        return result;
    }
```

`CopyTileAsync` in Task 4 calls this two-argument overload; give `CopyTileAsync` the signature `(PackTileViewModel tile, bool cut)` exactly as Task 4 writes it.

- [ ] **Step 3: Format, build, run all Core tests. Commit** "Pack detail: copy and move a tile to another pack".

---

## Task 6: The app clipboard and the three keys

Spec 4.3 last two bullets.

**Files:**
- Modify: `src\BhMaps.App\ViewModels\MainViewModel.cs` (beside `NavigateToPack` :175)
- Modify: `src\BhMaps.App\ViewModels\Pages\PackDetailViewModel.cs`
- Modify: `src\BhMaps.App\Views\Pages\PackDetailView.xaml` (`UserControl.InputBindings` :14-16, header actions :26-38)
- Modify: `src\BhMaps.App\Views\Pages\PackDetailView.xaml.cs`

**Interfaces:**

Produces:

```csharp
// MainViewModel
public sealed record PackClipboardItem(Pack Source, PackTileViewModel Tile, bool Cut);
public PackClipboardItem? PackClipboard { get; set; }

// PackDetailViewModel
[ObservableProperty] public partial string ClipboardHint { get; set; }
public bool HasClipboardHint => ClipboardHint.Length > 0;
public void CopyTileToClipboard(PackTileViewModel? tile, bool cut);
public Task PasteAsync();

// PackDetailView
public PackTileViewModel? HoveredTile { get; private set; }
```

- [ ] **Step 1: The clipboard on the shell.** In `MainViewModel`, beside `NavigateToPack`:

```csharp
    /// <summary>Spec 2.6 4.3: the one tile Ctrl+C or Ctrl+X remembered, held by the shell so it survives moving
    /// between pack pages. Never the Windows clipboard: this carries a pack, a tile and how it was taken.</summary>
    public PackClipboardItem? PackClipboard { get; set; }
```

and, at the end of the file beside the other small records, or in `src\BhMaps.App\ViewModels\PackClipboardItem.cs` if the file is cleaner:

```csharp
/// <summary>What Ctrl+C or Ctrl+X took: the pack it came out of, the tile, and whether it was cut.</summary>
public sealed record PackClipboardItem(Pack Source, PackTileViewModel Tile, bool Cut);
```

- [ ] **Step 2: The page's three actions.** In `PackDetailViewModel`:

```csharp
    /// <summary>Spec 2.6 4.3: the line the page shows for two seconds when Ctrl+V has nothing to paste.</summary>
    public const string NothingCopiedText = "Nothing copied yet";

    private readonly DispatcherTimer _hintTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClipboardHint))]
    public partial string ClipboardHint { get; set; }

    public bool HasClipboardHint => ClipboardHint.Length > 0;

    /// <summary>Ctrl+C and Ctrl+X: the tile is remembered on the shell and the page says so.</summary>
    public void CopyTileToClipboard(PackTileViewModel? tile, bool cut)
    {
        if (tile is null || Pack is not { } pack)
        {
            return;
        }

        Shell.PackClipboard = new PackClipboardItem(pack, tile, cut);
        var name = tile.Map?.DisplayName ?? tile.Caption;
        ShowHint(cut ? $"{name} cut" : $"{name} copied");
    }

    /// <summary>Ctrl+V: what the clipboard holds, into this page's pack. Nothing happens when the clipboard is
    /// empty but the line, and nothing at all when the source is this pack.</summary>
    public async Task PasteAsync()
    {
        if (Pack is not { } pack)
        {
            return;
        }

        if (Shell.PackClipboard is not { } held)
        {
            ShowHint(NothingCopiedText);
            return;
        }

        if (held.Source.Name.Equals(pack.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await PasteIntoAsync(held.Source, held.Tile, held.Cut, pack);
        if (held.Cut)
        {
            Shell.PackClipboard = null;
        }
    }

    /// <summary>The line, and the timer that takes it away again. One timer, restarted, so two keys in a row do
    /// not leave the first line's tick to clear the second's words.</summary>
    private void ShowHint(string text)
    {
        ClipboardHint = text;
        _hintTimer.Stop();
        _hintTimer.Start();
    }
```

In the constructor, after `TransparentText = "";`:

```csharp
        ClipboardHint = "";
        _hintTimer.Tick += (_, _) =>
        {
            _hintTimer.Stop();
            ClipboardHint = "";
        };
```

- [ ] **Step 3: The keys.** In `PackDetailView.xaml`:

```xml
  <UserControl.InputBindings>
    <KeyBinding Key="Escape" Command="{Binding CloseDrawerCommand}" />
    <KeyBinding Key="C" Modifiers="Control" Command="{Binding CopyTileCommand}" />
    <KeyBinding Key="X" Modifiers="Control" Command="{Binding CutTileCommand}" />
    <KeyBinding Key="V" Modifiers="Control" Command="{Binding PasteCommand}" />
  </UserControl.InputBindings>
```

and in the header actions, before the `ZoomSlider`:

```xml
          <TextBlock Margin="0,0,12,0" VerticalAlignment="Center" Foreground="{StaticResource Text3Brush}"
                     Text="{Binding ClipboardHint}"
                     Visibility="{Binding HasClipboardHint, Converter={StaticResource BoolToVis}}" />
```

The three commands are `[RelayCommand]` methods on the page that read the tile the view offers:

```csharp
    /// <summary>The tile the pointer is over wins; the keyboard-focused tile is the fallback (spec 2.6 4.3). The
    /// view knows both, so it hands the page the one it wants before the command runs.</summary>
    public PackTileViewModel? KeyTarget { get; set; }

    [RelayCommand]
    private void CopyTile() => CopyTileToClipboard(KeyTarget ?? SelectedTile, cut: false);

    [RelayCommand]
    private void CutTile() => CopyTileToClipboard(KeyTarget ?? SelectedTile, cut: false is false && true);

    [RelayCommand]
    private Task Paste() => PasteAsync();
```

Write `CutTile` as `CopyTileToClipboard(KeyTarget ?? SelectedTile, cut: true)`; the line above is a typo to fix while typing it.

An InputBinding fires while the page is shown and a modal window takes the keys itself, so "active only when the page is shown and no dialog is open" needs nothing further.

- [ ] **Step 4: The hovered tile.** In `PackDetailView.xaml`, on the tile container in the `ListBox.ItemContainerStyle` (find the `TileListBoxItem`-based style in this file), add `<EventSetter Event="MouseEnter" Handler="Tile_MouseEnter" />` and `<EventSetter Event="MouseLeave" Handler="Tile_MouseLeave" />`, and in the code-behind:

```csharp
    /// <summary>Spec 2.6 4.3: the tile under the pointer is what Ctrl+C, Ctrl+X and the menu act on when there
    /// is one. Written on the page rather than read from the visual tree when the key arrives, because a key
    /// binding has no pointer position.</summary>
    private void Tile_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PackTileViewModel tile } && Page is { } page)
        {
            page.KeyTarget = tile;
        }
    }

    private void Tile_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PackTileViewModel tile } && Page is { } page
            && ReferenceEquals(page.KeyTarget, tile))
        {
            page.KeyTarget = null;
        }
    }
```

- [ ] **Step 5: Format, build, run all Core tests. Launch the app once and check Ctrl+C on a hovered tile, Ctrl+V on another pack, and Ctrl+V with nothing held. Commit** "Pack detail: an app clipboard on Ctrl+C, Ctrl+X and Ctrl+V".

---

## Task 7: Import from pack

Spec 5.2 and 5.3.

**Files:**
- Create: `src\BhMaps.App\ViewModels\ImportFromPackViewModel.cs`
- Create: `src\BhMaps.App\Views\ImportFromPackWindow.xaml`, `src\BhMaps.App\Views\ImportFromPackWindow.xaml.cs`
- Modify: `src\BhMaps.App\ViewModels\MainViewModel.cs`
- Modify: `src\BhMaps.App\ViewModels\Pages\PackDetailViewModel.cs`
- Modify: `src\BhMaps.App\Views\Pages\PackDetailView.xaml` (header actions)

**Interfaces:**

Consumes: `PackCopier.Import`, `PackImportPlan`, `PackImportResult`, `PackCopier.MapFiles`, `PackCopier.Touched`, `RunLibraryWriteAsync`, `ShowChooser`'s window pattern (`MainViewModel.cs:653`), `MissingFgBrush` (`Tokens.xaml:15`), `Icon.Plus` (`Icons.xaml:18`).

Produces:

```csharp
public sealed partial class ImportRowViewModel : ObservableObject   // name it PackImportRowViewModel: ImportRowViewModel exists
{
    public MapEntry? Map { get; }
    public string? LooseFile { get; }          // pack-relative, for a Pictures row
    public string Name { get; }
    public string Detail { get; }              // "{k} files" or the file name
    public bool AlreadyHere { get; }
    [ObservableProperty] public partial bool IsTicked { get; set; }
    [ObservableProperty] public partial bool IsEnabled { get; set; }
    [ObservableProperty] public partial ImageSource? Thumbnail { get; set; }
}

public sealed partial class ImportFromPackViewModel : ObservableObject
{
    public ImportFromPackViewModel(MainViewModel shell, Pack target, IReadOnlyList<Pack> sources);
    public string Title { get; }               // "Import into {target}"
    public string Subtitle { get; }
    public ObservableCollection<PackSourceRow> Sources { get; }
    [ObservableProperty] public partial PackSourceRow? Source { get; set; }
    [ObservableProperty] public partial bool AllMaps { get; set; }
    [ObservableProperty] public partial bool Replace { get; set; }
    public ObservableCollection<PackImportRowViewModel> Rows { get; }
    public string PrimaryText { get; }
    public bool CanImport { get; }
    public PackImportPlan? BuildPlan();
    public void SetProgress(string text);
}

// MainViewModel
public async Task ImportFromPackAsync(Pack target);

// PackDetailViewModel
[RelayCommand(CanExecute = nameof(CanImportFromPack))] private Task ImportFromPackAsync();
public bool CanImportFromPack { get; }         // false with no other pack
public string ImportFromPackTip { get; }       // "No other pack to import from"
```

- [ ] **Step 1: The rows and the view model.** `PackSourceRow` is `record PackSourceRow(Pack Pack, string Label)` with `Label` = `$"{Count(maps, "map")}, {Count(backgrounds, "background")}"`, built the way `ChoosePackAsync` builds its detail line.

```csharp
    /// <summary>Spec 2.6 5.2: the source's maps and its loose Backgrounds files, ticked as the dialog's rules
    /// say. Rows the target already has start unticked, and stay disabled until Replace goes on.</summary>
    private void BuildRows()
    {
        Rows.Clear();
        if (Source is not { } source || _shell.Snapshot is not { } snapshot)
        {
            OnPropertyChanged(nameof(PrimaryText));
            OnPropertyChanged(nameof(CanImport));
            return;
        }

        var catalog = snapshot.Catalog;
        foreach (var map in catalog.Maps.Where(m => source.Pack.FindFolder(m.FolderName) is { Files.Count: > 0 }))
        {
            var here = _target.FindFolder(map.FolderName) is { Files.Count: > 0 };
            Rows.Add(new PackImportRowViewModel(
                map,
                null,
                map.DisplayName,
                here ? "already here" : $"{PackCopier.MapFiles(source.Pack, map, catalog).Count} files",
                here)
            {
                IsTicked = !here,
                IsEnabled = !here || Replace,
            });
        }

        foreach (var file in source.Pack.FindFolder(PackCopier.BackgroundsFolder)?.Files ?? Array.Empty<GameFile>())
        {
            if (!Path.GetExtension(file.Name).Equals(PackCopier.BackgroundExtension, StringComparison.OrdinalIgnoreCase)
                || PackCopier.MapForSlot(catalog, file.Name) is not null)
            {
                continue;
            }

            var relative = Path.Combine(PackCopier.BackgroundsFolder, file.Name);
            var here = File.Exists(Path.Combine(_target.FullPath, relative));
            Rows.Add(new PackImportRowViewModel(null, relative, file.Name, here ? "already here" : file.Name, here)
            {
                IsTicked = !here,
                IsEnabled = !here || Replace,
            });
        }

        OnPropertyChanged(nameof(PrimaryText));
        OnPropertyChanged(nameof(CanImport));
    }

    /// <summary>Spec 5.2: "Import 3 maps", "Import 1 map", "Nothing to import" when none is ticked, and the
    /// running count while it imports.</summary>
    public string PrimaryText =>
        _progress.Length > 0 ? _progress : Importable == 0 ? "Nothing to import" : $"Import {MainViewModel.Count(Importable, "map")}";

    public bool CanImport => Importable > 0 && _progress.Length == 0;

    private int Importable => AllMaps
        ? Rows.Count(r => !r.AlreadyHere || Replace)
        : Rows.Count(r => r.IsTicked && (!r.AlreadyHere || Replace));

    /// <summary>The plan the window's primary button hands back, in row order.</summary>
    public PackImportPlan? BuildPlan()
    {
        if (Source is not { } source)
        {
            return null;
        }

        var chosen = Rows.Where(r => (AllMaps || r.IsTicked) && (!r.AlreadyHere || Replace)).ToList();
        return new PackImportPlan(
            source.Pack,
            _target,
            [.. chosen.Where(r => r.Map is not null).Select(r => r.Map!)],
            [.. chosen.Where(r => r.LooseFile is not null).Select(r => r.LooseFile!)],
            Replace);
    }

    partial void OnSourceChanged(PackSourceRow? value) => BuildRows();

    partial void OnReplaceChanged(bool value)
    {
        foreach (var row in Rows)
        {
            row.IsEnabled = !row.AlreadyHere || value;
        }

        OnPropertyChanged(nameof(PrimaryText));
        OnPropertyChanged(nameof(CanImport));
    }

    partial void OnAllMapsChanged(bool value)
    {
        OnPropertyChanged(nameof(PrimaryText));
        OnPropertyChanged(nameof(CanImport));
    }
```

The constructor sets `Sources`, then `Rows`, then `AllMaps = true`, then `Source = Sources.FirstOrDefault()` last, because `[ObservableProperty]` setters run `OnSourceChanged` during construction and `BuildRows` reads `Rows`, `Replace` and `_target`.

The `All {n} maps` radio label is a property: `public string AllMapsText => $"All {MainViewModel.Count(Rows.Count(r => r.Map is not null), "map")}";` raised from `BuildRows`.

- [ ] **Step 2: The window.** `ImportFromPackWindow.xaml` copies `ChooserWindow.xaml`'s chrome exactly (`Width="460" Height="560"`, `WindowStyle="None"`, `WindowStartupLocation="CenterOwner"`, `Background="{StaticResource BgBrush}"`, the `Border` with `LineBrush` and `Radius`, `Margin="20"`), with rows:

```xml
      <TextBlock FontSize="17" FontWeight="SemiBold" Text="{Binding Title}" TextWrapping="Wrap" />
      <TextBlock Grid.Row="1" Margin="0,6,0,0" FontSize="12" Foreground="{StaticResource Text3Brush}"
                 Text="{Binding Subtitle}" TextWrapping="Wrap" />

      <StackPanel Grid.Row="2" Margin="0,14,0,0">
        <TextBlock Foreground="{StaticResource Text2Brush}" Text="From" />
        <ComboBox x:Name="From" Margin="0,6,0,0" AutomationProperties.Name="From"
                  DisplayMemberPath="Label"
                  FocusVisualStyle="{StaticResource DialogFocusRing}"
                  ItemsSource="{Binding Sources}"
                  SelectedItem="{Binding Source, Mode=TwoWay}" />
        <TextBlock Margin="0,14,0,0" Foreground="{StaticResource Text2Brush}" Text="Which maps" />
        <RadioButton Margin="0,6,0,0" Content="{Binding AllMapsText}"
                     FocusVisualStyle="{StaticResource DialogFocusRing}"
                     GroupName="Which" IsChecked="{Binding AllMaps, Mode=TwoWay}" />
        <RadioButton Margin="0,4,0,0" Content="Choose maps"
                     FocusVisualStyle="{StaticResource DialogFocusRing}" GroupName="Which" />
      </StackPanel>
```

`PackSourceRow` needs a display string: give it `public override string ToString()`-free `Label` and bind `DisplayMemberPath="Label"` with `Label` = `$"{Pack.Name}  {counts}"`.

The tick list is a `ListBox` in row 3 with `ItemsSource="{Binding Rows}"`, `Visibility` bound to `AllMaps` inverted (add a `converters:InverseBoolToVisibilityConverter` only if one exists; otherwise bind `ChooseMaps` on the view model, a plain `bool` = `!AllMaps`, through `BoolToVis`), and an ItemTemplate of a 40x22 thumbnail Border like `ChooserWindow`'s, a `CheckBox` with `IsChecked="{Binding IsTicked, Mode=TwoWay}"`, `IsEnabled="{Binding IsEnabled}"` and `FocusVisualStyle="{StaticResource DialogFocusRing}"`, the name, and the detail `TextBlock` whose Foreground is `{StaticResource MissingFgBrush}` under a `DataTrigger` on `AlreadyHere` being `True`, and `Text3Brush` otherwise. Rows that are loose files come after a divider row: add a header row item by grouping, or simpler, a `TextBlock` "Pictures" above a second `ListBox` bound to `LooseRows` (a second collection on the view model filled by `BuildRows`). Take the second-collection route: it needs no grouping styles.

Row 4:

```xml
      <CheckBox Grid.Row="4" Margin="0,12,0,0"
                Content="{Binding ReplaceText}"
                FocusVisualStyle="{StaticResource DialogFocusRing}"
                IsChecked="{Binding Replace, Mode=TwoWay}" />

      <StackPanel Grid.Row="5" Margin="0,16,0,0" HorizontalAlignment="Right" Orientation="Horizontal">
        <Button Margin="0,0,8,0" AutomationProperties.Name="Cancel" Content="Cancel" IsCancel="True"
                FocusVisualStyle="{StaticResource DialogFocusRing}" Style="{StaticResource OutlineButton}" />
        <Button AutomationProperties.Name="{Binding PrimaryText}" Click="OnImport"
                Content="{Binding PrimaryText}" IsDefault="True" IsEnabled="{Binding CanImport}"
                FocusVisualStyle="{StaticResource DialogFocusRing}" Style="{StaticResource PrimaryButton}" />
      </StackPanel>
```

`ReplaceText` is `$"Replace maps {target} already has"` with the target's name. The code-behind mirrors `ChooserWindow.xaml.cs`: `Loaded += (_, _) => From.Focus();`, `OnKeyDown` closing with false on Escape, `OnImport` setting `DialogResult = true` when `CanImport`. Space ticks and Enter imports come free from `CheckBox` and `IsDefault`.

- [ ] **Step 3: The shell runs it.** In `MainViewModel`:

```csharp
    /// <summary>Spec 2.6 section 5: one pack's maps and pictures into another, off the UI thread, inside a
    /// library undo session holding every path the plan will write or replace.</summary>
    public async Task ImportFromPackAsync(Pack target)
    {
        if (Snapshot is not { } snapshot)
        {
            return;
        }

        var sources = snapshot.Packs
            .Where(p => !p.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (sources.Count == 0)
        {
            return;
        }

        var vm = new ImportFromPackViewModel(this, target, sources);
        var window = new ImportFromPackWindow
        {
            DataContext = vm,
            Owner = Application.Current.MainWindow,
            ShowActivated = !App.Quiet,
        };
        if (window.ShowDialog() != true || vm.BuildPlan() is not { } plan)
        {
            return;
        }

        var catalog = snapshot.Catalog;
        var files = plan.Maps
            .SelectMany(m => PackCopier.MapFiles(plan.Source, m, catalog))
            .Concat(plan.LooseFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        PackImportResult? result = null;
        await RunLibraryWriteAsync(
            $"Importing into {target.Name}",
            PackCopier.Touched(target, files),
            (progress, ct) => Task.Run(() => { result = PackCopier.Import(plan, catalog, progress, ct); }, ct),
            ImportDone(result, plan, target));
        if (result is not null)
        {
            Dialogs.ShowFailures("Some files could not be imported", result.Failures);
        }
    }

    /// <summary>Spec 5.2's done line: the count, or the count with the first failure named.</summary>
    private static string ImportDone(PackImportResult? result, PackImportPlan plan, Pack target)
    {
        var wanted = plan.Maps.Count + plan.LooseFiles.Count;
        var skipped = result?.Skipped.Count ?? 0;
        var done = wanted - skipped;
        return result?.Failures.Count > 0
            ? $"{done} of {wanted} imported. {Path.GetFileName(result.Failures[0].Path)}: {result.Failures[0].Error}"
            : $"{Count(done, "map")} imported into {target.Name}";
    }
```

`ImportDone` reads `result` after the work, so compute the done text after `RunLibraryWriteAsync` returns instead: pass a placeholder and then `DoneText = ImportDone(result, plan, target)` only when the write succeeded. Write it that way:

```csharp
        var ok = await RunLibraryWriteAsync(..., doneText: $"Importing into {target.Name}");
        if (ok)
        {
            DoneText = ImportDone(result, plan, target);
        }
```

- [ ] **Step 4: The two entry points.** In `PackDetailViewModel`:

```csharp
    /// <summary>Spec 5.3: nothing to import from when the library holds only this pack.</summary>
    public bool CanImportFromPack =>
        Pack is { } pack && _snapshot is { } snapshot
        && snapshot.Packs.Any(p => !p.Name.Equals(pack.Name, StringComparison.OrdinalIgnoreCase));

    public const string NoOtherPackText = "No other pack to import from";

    [RelayCommand(CanExecute = nameof(CanImportFromPack))]
    private Task ImportFromPackAsync() =>
        Pack is { } pack ? Shell.ImportFromPackAsync(pack) : Task.CompletedTask;
```

`Pack` already carries `[NotifyCanExecuteChangedFor]` attributes; add `[NotifyCanExecuteChangedFor(nameof(ImportFromPackCommand))]` and `[NotifyPropertyChangedFor(nameof(CanImportFromPack))]` to it, and raise both at the end of `Refresh` with `ImportFromPackCommand.NotifyCanExecuteChanged();`.

In `PackDetailView.xaml`, before the Apply all button:

```xml
          <Button Margin="0,0,8,0" Command="{Binding ImportFromPackCommand}" Content="Import from pack"
                  controls:Icon.Glyph="{StaticResource Icon.Plus}"
                  FocusVisualStyle="{StaticResource DialogFocusRing}"
                  Style="{StaticResource OutlineButton}"
                  ToolTip="{x:Static pages:PackDetailViewModel.NoOtherPackText}"
                  ToolTipService.ShowOnDisabled="True" />
```

- [ ] **Step 5: Format, build, run all Core tests. Launch the app once and import a pack into another. Commit** "Packs: import one pack into another".

---

## Task 8: Duplicate and Import from another pack in the Packs row menu

Spec 6.

**Files:**
- Modify: `src\BhMaps.App\ViewModels\Pages\PacksViewModel.cs` (`BuildMenu` :195-200, new commands after `ExportAsync` :330-357)

**Interfaces:**

Consumes: `PackCopier.DuplicatePack`, `PackCopier.FreeCopyName`, `PackCopier.LibraryRelative`, `Pack.RelativePaths` (`Model\Pack.cs:12`), `RunLibraryWriteAsync`, `Shell.ImportFromPackAsync` (Task 7).

- [ ] **Step 1: The menu.**

```csharp
    private IReadOnlyList<TileMenuCommand> BuildMenu(PackRowViewModel row) =>
    [
        new TileMenuCommand("Duplicate", new RelayCommand(() => DuplicateCommand.Execute(row))),
        new TileMenuCommand("Import from another pack...", new RelayCommand(() => ImportIntoCommand.Execute(row))),
        new TileMenuCommand("Export", new RelayCommand(() => ExportCommand.Execute(row))),
        new TileMenuCommand("Open folder", new RelayCommand(() => OpenFolderCommand.Execute(row))),
        new TileMenuCommand("Remove", new RelayCommand(() => RemoveCommand.Execute(row))),
    ];
```

Remove stays on every row, Default included: that is the rule the app has today (see Conflicts, item 4).

- [ ] **Step 2: The commands.**

```csharp
    /// <summary>Spec 2.6 3.1: the pack folder copied to "{name} copy", off the UI thread. The copy's paths are
    /// captured as absent before it is made, so Undo deletes exactly the files the copy laid down and prunes the
    /// folders they needed.</summary>
    [RelayCommand]
    private async Task DuplicateAsync(PackRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var pack = row.Pack;
        var libraryPath = Shell.Services.LibraryPath;
        var copyName = PackCopier.FreeCopyName(libraryPath, pack.Name);
        var undoPaths = pack.RelativePaths
            .Concat([PlatformEditRecord.FileName, BackgroundEditRecord.FileName])
            .Select(r => Path.Combine(PackCopier.PacksFolderName, copyName, r))
            .ToList();
        string? error = null;
        var made = copyName;
        var ok = await Shell.RunLibraryWriteAsync(
            $"Duplicating {pack.Name}",
            undoPaths,
            (_, ct) => Task.Run(
                () =>
                {
                    try
                    {
                        made = PackCopier.DuplicatePack(libraryPath, pack.Name);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        error = ex.Message;
                    }
                },
                ct),
            $"{pack.Name} duplicated as {copyName}");
        if (error is not null)
        {
            Shell.Dialogs.Error("Could not duplicate pack", $"Could not duplicate {pack.Name}: {error}");
        }
        else if (ok && !made.Equals(copyName, StringComparison.OrdinalIgnoreCase))
        {
            // A pack made between the name and the copy took the name; the line says what was made.
            Shell.SetLibraryDone($"{pack.Name} duplicated as {made}");
        }
    }

    /// <summary>Spec 3.2: the same dialog the pack detail header opens, aimed at this row's pack.</summary>
    [RelayCommand]
    private Task ImportIntoAsync(PackRowViewModel? row) =>
        row is null ? Task.CompletedTask : Shell.ImportFromPackAsync(row.Pack);
```

The name race is real but narrow: `FreeCopyName` runs once for the undo paths and `DuplicatePack` calls it again. Pass the name in instead if `DuplicatePack` is given an overload `DuplicatePack(string libraryPath, string name, string copyName)`; do that and have the one-name overload call it, so the captured paths and the folder that is made can never disagree, and the `made` branch above goes away.

- [ ] **Step 3: Format, build, run all Core tests. Launch the app once, duplicate a pack, undo it, and check the library has no leftover folder. Commit** "Packs: duplicate a pack and import into one from the row menu".

---

## Plan self-review

### Spec coverage

| Spec line | Task |
| --- | --- |
| 2, a map's files in a pack | Task 1, `MapFiles` |
| 3, BuildTileMenu always produces a menu | Task 4 Step 3 |
| 3, the no-items branches go | Task 4 Step 5 |
| 3, map menu header | Task 4 Step 3 |
| 3, `Apply to {map}` | Task 4 Step 3 (see Conflicts 3) |
| 3, `Copy to pack...` with Ctrl+C shown | Task 4 Steps 1 and 3 |
| 3, `Move to pack...` with Ctrl+X | Task 4 Steps 1 and 3 |
| 3, `Show in folder` on a map tile | Task 4 Steps 2 and 3 |
| 3, `Remove from {pack}` with undo | Task 4 Step 4 |
| 3, picture lines stay, Copy and Move before Show in folder | Task 4 Step 3, `AddPictureLines` |
| 3, file tile menu plus Copy and Move after Edit | Task 4 Step 3 |
| 4.1, `CopyMap`, `CopyFile`, `PackCopyResult` | Task 1 |
| 4.1, `MoveMap`, `MoveFile`, `DuplicatePack` | Task 2 |
| 4.1, `RemoveMap` | Task 1 |
| 4.1, Default is a source and target for copy, never for move or remove | Task 4 Step 3 (`isDefault`) |
| 4.2, undo session captures both sides before a move, remove or import | Tasks 3, 4, 5, 7 |
| 4.2, a plain copy gets no session and offers `Open {target}` | Tasks 3 and 5 |
| 4.3, `ChoosePackAsync` with `New pack...` | Task 5 Step 1 |
| 4.3, `Copy {name} to` and `Move {name} to`, skip and replace | Task 5 Step 2 |
| 4.3, done lines and rescan | Task 5 Step 2 |
| 4.3, `PackClipboard`, pointer wins over focus, cut cleared | Task 6 |
| 4.3, `Nothing copied yet` for two seconds, paste into the source does nothing | Task 6 Step 2 |
| 4.3, keys are KeyBindings on PackDetailView | Task 6 Step 3 |
| 5.1, `PackImportPlan`, `PackImportResult`, `Import`, progress line | Task 2 |
| 5.2, dialog, From, Which maps, Replace, primary button, keyboard | Task 7 Steps 1 and 2 |
| 5.2, done line and partial failure | Task 7 Step 3 |
| 5.3, `Import from pack` button and `ImportFromPackCommand` | Task 7 Step 4 |
| 5.3, `Import from another pack...` in the row menu | Task 8 |
| 6, row menu order | Task 8 Step 1 |
| 6, Duplicate with undo and the failure line | Task 8 Step 2 |

### Type consistency

- `PackCopyResult.Written` and `.Removed` are library-relative everywhere, which is exactly what `UndoSession.CaptureLibrary(libraryPath, paths)` takes; `MapFiles` is pack-relative and is only ever passed through `Touched` or joined to a pack's `FullPath`. The one place the two meet is `CopyMap`, which builds `written` pack-relative and maps it through `LibraryRelative` once at the return.
- `PackCopier` lives in `BhMaps.Core.Packs` and takes `MapEntry` from `BhMaps.Core.Maps` and `Pack` from `BhMaps.Core.Model`; `Packs` already depends on neither, so add the two usings and expect no cycle (`Maps` does not reference `Packs`).
- `TileMenuCommand`'s new `Gesture` is the last positional parameter, so every existing `new TileMenuCommand(text, command)` and every `Header`/`Separator`/`Flyout` factory compiles unchanged. `ChooserRow`'s `isNewPack` is likewise last.
- `RunLibraryWriteAsync` returns `Task<bool>` like `RunGameWriteAsync`, refuses through `RunBusyAsync`'s own `IsBusy` check, and always rescans, so callers need no boundary of their own.
- `MainViewModel.Count(int, string)` (`MainViewModel.cs:509`) is the one pluraliser for the done lines; `PackRowViewModel.Plural` is the one the confirms already use. Do not add a third.
- `ImportRowViewModel` already exists (`src\BhMaps.App\ViewModels\ImportRowViewModel.cs`) for the folder-import flow, so Task 7's row type is `PackImportRowViewModel`.
