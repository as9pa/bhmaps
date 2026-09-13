using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Packs;
using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class RecordResetTests
{
    private const string MapX = "X";
    private const string MapY = "Y";
    private const string PieceFile = @"X\Platform_X1.png";
    private const string SlotX = "BG_X.jpg";
    private const string SlotXPath = @"Backgrounds\BG_X.jpg";
    private const string SlotZPath = @"Backgrounds\BG_Z.jpg";

    [Fact]
    public void Matched_packs_are_the_ones_the_files_match()
    {
        using var tmp = new TempDir();
        var packs = Packs(tmp, "A", "B");

        var matched = RecordReset.MatchedPacks(Map(), Status("A", "B"), packs);

        Assert.Equal(["A", "B"], matched.Select(p => p.Name));
    }

    [Fact]
    public void Matched_packs_are_empty_without_a_status()
    {
        using var tmp = new TempDir();

        Assert.Empty(RecordReset.MatchedPacks(Map(), status: null, Packs(tmp, "A", "B")));
    }

    [Fact]
    public void Matched_packs_are_empty_for_another_maps_status()
    {
        using var tmp = new TempDir();
        var status = new MapStatus(
            MapY, MapState.Packs, ["A"], [new MapFileStatus(PieceFile, MapFileState.Pack, ["A"])]);

        Assert.Empty(RecordReset.MatchedPacks(Map(), status, Packs(tmp, "A", "B")));
    }

    [Fact]
    public void Clear_leaves_the_other_maps_entry_set_standing()
    {
        using var tmp = new TempDir();
        var packs = Packs(tmp, "A", "B");
        WriteRecords(packs, "A");

        RecordReset.Clear(Map(), RecordReset.MatchedPacks(Map(), Status("A", "B"), packs));

        var record = PlatformEditRecord.Load(Find(packs, "A").FullPath);
        Assert.Null(record.Map(MapX));
        Assert.NotNull(record.Map(MapY));
    }

    [Fact]
    public void Clear_removes_the_maps_slots_and_keeps_the_rest()
    {
        using var tmp = new TempDir();
        var packs = Packs(tmp, "A", "B");
        WriteRecords(packs, "A");

        RecordReset.Clear(Map(), RecordReset.MatchedPacks(Map(), Status("A", "B"), packs));

        var record = BackgroundEditRecord.Load(Find(packs, "A").FullPath);
        Assert.Null(record.Entry(SlotXPath));
        Assert.NotNull(record.Entry(SlotZPath));
    }

    [Fact]
    public void Clear_writes_nothing_into_a_pack_that_kept_no_record()
    {
        using var tmp = new TempDir();
        var packs = Packs(tmp, "A", "B");
        WriteRecords(packs, "A");

        RecordReset.Clear(Map(), RecordReset.MatchedPacks(Map(), Status("A", "B"), packs));

        var b = Find(packs, "B").FullPath;
        Assert.False(File.Exists(PlatformEditRecord.PathFor(b)));
        Assert.False(File.Exists(BackgroundEditRecord.PathFor(b)));
    }

    [Fact]
    public void Undo_paths_name_both_records_of_every_matched_pack()
    {
        using var tmp = new TempDir();
        var packs = Packs(tmp, "A", "B");

        var paths = RecordReset.UndoPaths(RecordReset.MatchedPacks(Map(), Status("A", "B"), packs), tmp.Path);

        Assert.Equal(
            [
                @"packs\A\platforms.bhmaps.json",
                @"packs\A\backgrounds.bhmaps.json",
                @"packs\B\platforms.bhmaps.json",
                @"packs\B\backgrounds.bhmaps.json",
            ],
            paths);
    }

    [Fact]
    public void Undo_paths_leave_out_a_pack_outside_the_library()
    {
        using var tmp = new TempDir();
        using var other = new TempDir();
        var outside = new Pack("Outside", Path.Combine(other.Path, "Outside"), []);

        Assert.Empty(RecordReset.UndoPaths([outside], tmp.Path));
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

    /// <summary>Pack A remembers two maps and two slots; only what belongs to map X is the reset's to clear.</summary>
    private static void WriteRecords(IReadOnlyList<Pack> packs, string packName)
    {
        var packRoot = Find(packs, packName).FullPath;
        var platforms = new PlatformEditRecord();
        foreach (var folder in new[] { MapX, MapY })
        {
            platforms.SetMap(folder, DateTimeOffset.Now, new Dictionary<string, PlatformPieceEntry>
            {
                [PieceFile] = new() { Art = PlatformArt.Own, Opacity = 80, Hash = "aa11" },
            });
        }

        platforms.Save(packRoot);

        var backgrounds = new BackgroundEditRecord();
        backgrounds.Set(SlotXPath, new BackgroundSlotEntry { Picture = @"C:\pictures\x.png", Hash = "bb22" });
        backgrounds.Set(SlotZPath, new BackgroundSlotEntry { Picture = @"C:\pictures\z.png", Hash = "cc33" });
        backgrounds.Save(packRoot);
    }

    private static Pack Find(IReadOnlyList<Pack> packs, string name) =>
        packs.First(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static MapStatus Status(params string[] packNames) =>
        new(MapX,
            MapState.Packs,
            packNames,
            [
                new MapFileStatus(PieceFile, MapFileState.Pack, packNames),
                new MapFileStatus(SlotXPath, MapFileState.Pack, packNames),
            ]);

    private static MapEntry Map() =>
        new(MapX,
            "Map X",
            new LevelDesc("X", MapX, new CameraBounds(0, 0, 100, 100), [], []),
            [],
            [],
            [SlotX],
            [PieceFile]);
}
