using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Packs;
using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class PackCopierTests
{
    /// <summary>One level named after its folder, holding the "BG_&lt;folder&gt;.jpg" slot and no platforms.</summary>
    private static LevelDesc Desc(string folder) =>
        new(folder, folder, new CameraBounds(0, 0, 100, 50), [new LevelBackground("BG_" + folder + ".jpg", null, null)], []);

    /// <summary>One map, one slot. The catalog is built by hand because the copier reads nothing else off it.</summary>
    private static MapCatalog Catalog() =>
        MapCatalogTests.CatalogOf(
            new MapEntry("BloodMoon", "Blood Moon", Desc("BloodMoon"), [], [], ["BG_BloodMoon.jpg"], []));

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

    /// <summary>A platform entry for the map and a background entry for one slot, written to the pack, so that a
    /// narrow remove can be seen taking one of them and leaving the other where it was.</summary>
    private static void Records(Pack pack, string slot)
    {
        var platforms = PlatformEditRecord.Load(pack.FullPath);
        platforms.SetMap("BloodMoon", DateTimeOffset.UnixEpoch, new Dictionary<string, PlatformPieceEntry>());
        platforms.Save(pack.FullPath);

        var backgrounds = BackgroundEditRecord.Load(pack.FullPath);
        backgrounds.Set(Path.Combine("Backgrounds", slot), new BackgroundSlotEntry { Picture = slot });
        backgrounds.Save(pack.FullPath);
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
        Assert.Single(result.Written, w => w.EndsWith("BG_Other.jpg", StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public void RemovePlatforms_takes_the_folder_and_leaves_the_background_alone()
    {
        using var tmp = new TempDir();
        var (lib, source, _) = Arrange(tmp, targetHasMap: false);
        Records(source, slot: "BG_BloodMoon.jpg");

        var result = PackCopier.RemovePlatforms(source, Catalog().Maps[0], Catalog());

        Assert.Empty(result.Failures);
        Assert.Contains(Path.Combine("packs", "flower", "BloodMoon", "a.png"), result.Removed);
        Assert.False(Directory.Exists(Path.Combine(lib, "packs", "flower", "BloodMoon")));
        Assert.Null(PlatformEditRecord.Load(source.FullPath).Map("BloodMoon"));
        Assert.Equal("flower-bg", File.ReadAllText(Path.Combine(lib, "packs", "flower", "Backgrounds", "BG_BloodMoon.jpg")));
        Assert.NotNull(BackgroundEditRecord.Load(source.FullPath).Entry(Path.Combine("Backgrounds", "BG_BloodMoon.jpg")));
    }

    [Fact]
    public void RemoveBackground_takes_one_slot_and_leaves_the_map_alone()
    {
        using var tmp = new TempDir();
        var (lib, source, _) = Arrange(tmp, targetHasMap: false);
        Records(source, slot: "BG_BloodMoon.jpg");
        Records(source, slot: "BG_Other.jpg");

        var result = PackCopier.RemoveBackground(source, "BG_BloodMoon.jpg");

        Assert.Empty(result.Failures);
        Assert.Contains(Path.Combine("packs", "flower", "Backgrounds", "BG_BloodMoon.jpg"), result.Removed);
        Assert.False(File.Exists(Path.Combine(lib, "packs", "flower", "Backgrounds", "BG_BloodMoon.jpg")));
        Assert.Null(BackgroundEditRecord.Load(source.FullPath).Entry(Path.Combine("Backgrounds", "BG_BloodMoon.jpg")));
        Assert.NotNull(BackgroundEditRecord.Load(source.FullPath).Entry(Path.Combine("Backgrounds", "BG_Other.jpg")));
        Assert.Equal("flower-a", File.ReadAllText(Path.Combine(lib, "packs", "flower", "BloodMoon", "a.png")));
        Assert.NotNull(PlatformEditRecord.Load(source.FullPath).Map("BloodMoon"));
    }

    [Fact]
    public void RemoveBackground_on_a_picture_the_pack_no_longer_has_is_not_a_failure()
    {
        using var tmp = new TempDir();
        var (_, source, _) = Arrange(tmp, targetHasMap: false);

        var result = PackCopier.RemoveBackground(source, "BG_Gone.jpg");

        Assert.Empty(result.Failures);
        Assert.Equal(new[] { Path.Combine("packs", "flower", "Backgrounds", "BG_Gone.jpg") }, result.Removed);
    }

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
    public void MoveMap_that_could_not_copy_every_file_leaves_the_source_alone()
    {
        using var tmp = new TempDir();
        var (lib, source, target) = Arrange(tmp, targetHasMap: false);

        // A folder standing where b.png has to land: the copy of that one file fails, the others land.
        Directory.CreateDirectory(Path.Combine(lib, "packs", "stone", "BloodMoon", "b.png"));

        var result = PackCopier.MoveMap(source, target, Catalog().Maps[0], Catalog(), replace: false);

        Assert.False(result.Skipped);
        Assert.Single(result.Failures);
        Assert.Empty(result.Removed);
        Assert.True(File.Exists(Path.Combine(lib, "packs", "flower", "BloodMoon", "a.png")));
        Assert.True(File.Exists(Path.Combine(lib, "packs", "flower", "BloodMoon", "b.png")));
        Assert.True(File.Exists(Path.Combine(lib, "packs", "flower", "Backgrounds", "BG_BloodMoon.jpg")));
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
    public void DuplicatePaths_names_every_file_the_copy_takes_under_the_copy_name()
    {
        using var tmp = new TempDir();
        var (lib, source, _) = Arrange(tmp, targetHasMap: false);
        File.WriteAllText(Path.Combine(source.FullPath, "BloodMoon", "Thumbs.db"), "not an image");

        var paths = PackCopier.DuplicatePaths(lib, "flower", "flower copy");

        Assert.Contains(Path.Combine("packs", "flower copy", "BloodMoon", "Thumbs.db"), paths);
        Assert.Contains(Path.Combine("packs", "flower copy", "BloodMoon", "a.png"), paths);
        Assert.Equal("flower copy", PackCopier.DuplicatePack(lib, "flower"));
        Assert.Equal(
            paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase),
            Directory.EnumerateFiles(Path.Combine(lib, "packs", "flower copy"), "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(lib, f))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Import_copies_every_map_and_reports_the_ones_it_skipped()
    {
        using var tmp = new TempDir();
        var (lib, source, target) = Arrange(tmp, targetHasMap: true);
        var progress = new List<string>();
        var plan = new PackImportPlan(
            source, target, [Catalog().Maps[0]], [Path.Combine("Backgrounds", "BG_Other.jpg")], Replace: false);

        var result = PackCopier.Import(plan, Catalog(), new SyncProgress(progress), CancellationToken.None);

        Assert.Equal(["Blood Moon"], result.Skipped);
        Assert.Equal("stone-a", File.ReadAllText(Path.Combine(lib, "packs", "stone", "BloodMoon", "a.png")));
        Assert.Equal("flower-other", File.ReadAllText(Path.Combine(lib, "packs", "stone", "Backgrounds", "BG_Other.jpg")));
        Assert.Contains("Importing 1 of 2: Blood Moon", progress);
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
}
