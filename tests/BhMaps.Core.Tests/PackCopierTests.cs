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
}
