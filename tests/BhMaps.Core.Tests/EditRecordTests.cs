using BhMaps.Core.Packs;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class EditRecordTests
{
    private static readonly DateTimeOffset SavedAt =
        new(2026, 9, 12, 18, 30, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void Platform_record_round_trips()
    {
        using var tmp = new TempDir();
        var record = new PlatformEditRecord();
        record.SetMap("BLOODMOON", SavedAt, new Dictionary<string, PlatformPieceEntry>
        {
            ["BLOODMOON\\platform_bm1.png"] = new()
            {
                Opacity = 80,
                Hue = 210,
                Art = PlatformArt.Across,
                Picture = "Platforms\\stone.png",
                PanX = 0.25,
                PanY = 0.75,
                Hash = "aa11",
            },
            ["BLOODMOON\\platform_bm2.png"] = new()
            {
                Art = PlatformArt.WorkingCopy,
                Hash = "bb22",
            },
        });

        record.Save(tmp.Path);
        var loaded = PlatformEditRecord.Load(tmp.Path);

        Assert.Equal(PlatformEditRecord.SchemaVersion, loaded.Version);
        var map = loaded.Map("bloodmoon");
        Assert.NotNull(map);
        Assert.Equal(SavedAt, map.SavedAt);
        Assert.Equal(2, map.Pieces.Count);

        var across = loaded.Entry("bloodmoon", "BLOODMOON\\platform_bm1.png");
        Assert.NotNull(across);
        Assert.Equal(80, across.Opacity);
        Assert.Equal(210, across.Hue);
        Assert.Equal(PlatformArt.Across, across.Art);
        Assert.Equal("Platforms\\stone.png", across.Picture);
        Assert.Equal(0.25, across.PanX);
        Assert.Equal(0.75, across.PanY);
        Assert.Equal("aa11", across.Hash);

        var working = loaded.Entry("BLOODMOON", "bloodmoon\\PLATFORM_BM2.PNG");
        Assert.NotNull(working);
        Assert.Equal(PlatformArt.WorkingCopy, working.Art);
        Assert.Null(working.Opacity);
        Assert.Null(working.Hue);
        Assert.Null(working.Picture);
        Assert.Null(working.PanX);
        Assert.Null(working.PanY);
        Assert.Equal("bb22", working.Hash);
    }

    [Fact]
    public void Missing_file_loads_empty()
    {
        using var tmp = new TempDir();

        Assert.Empty(PlatformEditRecord.Load(tmp.Path).Maps);
        Assert.Empty(BackgroundEditRecord.Load(tmp.Path).Slots);
    }

    [Fact]
    public void Unreadable_file_loads_empty()
    {
        using var tmp = new TempDir();
        File.WriteAllText(PlatformEditRecord.PathFor(tmp.Path), "{ not json");

        Assert.Empty(PlatformEditRecord.Load(tmp.Path).Maps);
    }

    [Fact]
    public void Unknown_fields_survive_round_trip()
    {
        using var tmp = new TempDir();
        File.WriteAllText(PlatformEditRecord.PathFor(tmp.Path), """
            {
              "version": 1,
              "future": 1,
              "maps": {
                "BLOODMOON": {
                  "savedAt": "2026-09-12T18:30:00-04:00",
                  "future": 1,
                  "pieces": {
                    "BLOODMOON\\platform_bm1.png": {
                      "art": "across",
                      "hash": "aa11",
                      "future": 1
                    }
                  }
                }
              }
            }
            """);

        var loaded = PlatformEditRecord.Load(tmp.Path);
        loaded.Save(tmp.Path);

        var json = File.ReadAllText(PlatformEditRecord.PathFor(tmp.Path));
        Assert.Equal(3, json.Split("\"future\"").Length - 1);
    }

    [Fact]
    public void Remove_map_drops_only_that_map()
    {
        var record = new PlatformEditRecord();
        record.SetMap("A", SavedAt, new Dictionary<string, PlatformPieceEntry>());
        record.SetMap("B", SavedAt, new Dictionary<string, PlatformPieceEntry>());

        Assert.True(record.RemoveMap("A"));
        Assert.Equal(new[] { "B" }, record.Maps.Keys);
        Assert.False(record.RemoveMap("A"));
    }

    [Fact]
    public void Working_copy_entry_writes_only_art_and_hash()
    {
        using var tmp = new TempDir();
        var record = new PlatformEditRecord();
        record.SetMap("BLOODMOON", SavedAt, new Dictionary<string, PlatformPieceEntry>
        {
            ["BLOODMOON\\platform_bm1.png"] = new() { Art = PlatformArt.WorkingCopy, Hash = "aa11" },
        });

        record.Save(tmp.Path);

        var json = File.ReadAllText(PlatformEditRecord.PathFor(tmp.Path));
        Assert.Contains("\"art\": \"workingCopy\"", json);
        Assert.DoesNotContain("opacity", json);
    }

    [Fact]
    public void Background_record_round_trips()
    {
        using var tmp = new TempDir();
        var record = new BackgroundEditRecord();
        record.Set("Backgrounds\\BG_Sewer.jpg", new BackgroundSlotEntry
        {
            SavedAt = SavedAt,
            Picture = "Pictures\\sewer.jpg",
            Mode = BackgroundMode.Contain,
            PanX = 0.2,
            PanY = 0.8,
            Darken = 35,
            Hash = "cc33",
        });

        record.Save(tmp.Path);
        var loaded = BackgroundEditRecord.Load(tmp.Path);

        var slot = loaded.Entry("backgrounds\\bg_sewer.jpg");
        Assert.NotNull(slot);
        Assert.Equal(SavedAt, slot.SavedAt);
        Assert.Equal("Pictures\\sewer.jpg", slot.Picture);
        Assert.Equal(BackgroundMode.Contain, slot.Mode);
        Assert.Equal(0.2, slot.PanX);
        Assert.Equal(0.8, slot.PanY);
        Assert.Equal(35, slot.Darken);
        Assert.Equal("cc33", slot.Hash);
        Assert.Contains("\"mode\": \"contain\"", File.ReadAllText(BackgroundEditRecord.PathFor(tmp.Path)));
    }

    [Fact]
    public void Write_leaves_no_temp_file()
    {
        using var tmp = new TempDir();
        var record = new PlatformEditRecord();
        record.SetMap("BLOODMOON", SavedAt, new Dictionary<string, PlatformPieceEntry>());

        record.Save(tmp.Path);

        Assert.Equal(
            new[] { PlatformEditRecord.FileName },
            Directory.GetFileSystemEntries(tmp.Path).Select(Path.GetFileName));
    }
}
