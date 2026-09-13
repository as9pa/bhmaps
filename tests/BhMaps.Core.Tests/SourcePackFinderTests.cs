using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Packs;
using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class SourcePackFinderTests
{
    private const string MapFolder = "BloodMoon";
    private const string PieceFile = @"BloodMoon\Platform_BM1.png";
    private const string SlotFile = @"Backgrounds\BG_BloodMoon.jpg";

    [Fact]
    public void Platforms_take_the_first_listed_pack_that_has_a_record()
    {
        using var tmp = new TempDir();
        var packs = Packs(tmp, "A", "B", "C");
        WritePlatformRecord(packs, "A");

        var found = SourcePackFinder.ForPlatforms(Map(), Status(PieceFile, "B", "A"), packs);

        Assert.NotNull(found);
        Assert.Equal("A", found.Name);
    }

    [Fact]
    public void Platforms_prefer_the_pack_the_status_lists_first()
    {
        using var tmp = new TempDir();
        var packs = Packs(tmp, "A", "B", "C");
        WritePlatformRecord(packs, "A");
        WritePlatformRecord(packs, "B");

        var found = SourcePackFinder.ForPlatforms(Map(), Status(PieceFile, "B", "A"), packs);

        Assert.NotNull(found);
        Assert.Equal("B", found.Name);
    }

    [Fact]
    public void Platforms_are_null_when_no_pack_has_a_record()
    {
        using var tmp = new TempDir();
        var packs = Packs(tmp, "A", "B", "C");

        Assert.Null(SourcePackFinder.ForPlatforms(Map(), Status(PieceFile, "B", "A"), packs));
    }

    [Fact]
    public void Platforms_are_null_without_a_status()
    {
        using var tmp = new TempDir();
        var packs = Packs(tmp, "A", "B", "C");
        WritePlatformRecord(packs, "A");

        Assert.Null(SourcePackFinder.ForPlatforms(Map(), status: null, packs));
    }

    [Fact]
    public void Platforms_skip_a_file_the_game_itself_shows()
    {
        using var tmp = new TempDir();
        var packs = Packs(tmp, "A", "B", "C");
        WritePlatformRecord(packs, "A");
        var status = new MapStatus(
            MapFolder, MapState.Default, [], [new MapFileStatus(PieceFile, MapFileState.Default, [])]);

        Assert.Null(SourcePackFinder.ForPlatforms(Map(), status, packs));
    }

    [Fact]
    public void Backgrounds_take_the_first_listed_pack_that_has_an_entry()
    {
        using var tmp = new TempDir();
        var packs = Packs(tmp, "A", "B", "C");
        WriteBackgroundRecord(packs, "A");

        var found = SourcePackFinder.ForBackground(SlotFile, Status(SlotFile, "B", "A"), packs);

        Assert.NotNull(found);
        Assert.Equal("A", found.Name);
    }

    [Fact]
    public void Backgrounds_are_null_when_no_pack_has_an_entry()
    {
        using var tmp = new TempDir();
        var packs = Packs(tmp, "A", "B", "C");

        Assert.Null(SourcePackFinder.ForBackground(SlotFile, Status(SlotFile, "B", "A"), packs));
    }

    [Fact]
    public void Backgrounds_are_null_without_a_status()
    {
        using var tmp = new TempDir();
        var packs = Packs(tmp, "A", "B", "C");
        WriteBackgroundRecord(packs, "A");

        Assert.Null(SourcePackFinder.ForBackground(SlotFile, status: null, packs));
    }

    private static IReadOnlyList<Pack> Packs(TempDir tmp, params string[] names)
    {
        var root = PackScanner.PacksRoot(tmp.Path);
        var packs = new List<Pack>();
        foreach (var name in names)
        {
            var full = Path.Combine(root, name);
            Directory.CreateDirectory(full);
            packs.Add(new Pack(name, full, []));
        }

        return packs;
    }

    private static void WritePlatformRecord(IReadOnlyList<Pack> packs, string packName)
    {
        var record = new PlatformEditRecord();
        record.SetMap(MapFolder, DateTimeOffset.Now, new Dictionary<string, PlatformPieceEntry>
        {
            [PieceFile] = new() { Art = PlatformArt.Own, Opacity = 80, Hue = 0, Hash = "aa11" },
        });
        record.Save(Find(packs, packName).FullPath);
    }

    private static void WriteBackgroundRecord(IReadOnlyList<Pack> packs, string packName)
    {
        var record = new BackgroundEditRecord();
        record.Set(SlotFile, new BackgroundSlotEntry { Picture = @"C:\pictures\moon.png", Hash = "bb22" });
        record.Save(Find(packs, packName).FullPath);
    }

    private static Pack Find(IReadOnlyList<Pack> packs, string name) =>
        packs.First(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static MapStatus Status(string relativePath, params string[] packNames) =>
        new(MapFolder, MapState.Packs, packNames, [new MapFileStatus(relativePath, MapFileState.Pack, packNames)]);

    private static MapEntry Map() =>
        new(MapFolder,
            "Blood Moon",
            new LevelDesc("BloodMoon", MapFolder, new CameraBounds(0, 0, 100, 100), [], []),
            [],
            [],
            [SlotFile],
            [PieceFile]);
}
