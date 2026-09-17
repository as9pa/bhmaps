using BhMaps.Core.Hashing;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class AppliedRecordTests
{
    private static readonly DateTimeOffset Noted = new(2026, 9, 16, 11, 30, 0, TimeSpan.Zero);

    /// <summary>A library with one pack holding Grove\\a.png, the game folder that pack was applied to, and the
    /// record path beside them.</summary>
    private static (string Record, string Game, string Library, Pack Pack) Arrange(TempDir tmp, string gameBytes)
    {
        var library = Path.Combine(tmp.Path, "library");
        var packFile = Path.Combine(library, "packs", "dark", "Grove", "a.png");
        Directory.CreateDirectory(Path.GetDirectoryName(packFile)!);
        File.WriteAllText(packFile, "art");

        var game = Path.Combine(tmp.Path, "game");
        if (gameBytes.Length > 0)
        {
            new FakeGameTree(game).File("Grove", "a.png", gameBytes);
        }

        var pack = new Pack("dark", Path.Combine(library, "packs", "dark"), []);
        return (Path.Combine(tmp.Path, "appdata", "applied.json"), game, library, pack);
    }

    /// <summary>No game file before the write, for the tests where what was there first does not matter.</summary>
    private static string? Nothing(string relativePath) => null;

    private static AppliedSource Source(Pack pack) =>
        new("Grove\\a.png", Path.Combine(pack.FullPath, "Grove", "a.png"), pack.Name);

    [Fact]
    public void PathFor_SitsInTheAppDataFolder()
    {
        Assert.Equal(Path.Combine("appdata", "applied.json"), AppliedRecord.PathFor("appdata"));
    }

    [Fact]
    public void Load_OfAMissingFileIsEmpty()
    {
        using var tmp = new TempDir();

        Assert.Empty(AppliedRecord.Load(Path.Combine(tmp.Path, "applied.json")).Entries);
    }

    [Fact]
    public void Load_OfAMalformedFileIsEmpty()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("applied.json");
        File.WriteAllText(path, "{ not json at all");

        Assert.Empty(AppliedRecord.Load(path).Entries);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEveryEntryAndComparesKeysIgnoringCase()
    {
        using var tmp = new TempDir();
        var (record, game, library, pack) = Arrange(tmp, "art");
        new FakeGameTree(game).File("Grove", "b.png", "art");
        AppliedRecord.Note(
            record,
            game,
            library,
            [Source(pack), new AppliedSource("Grove\\b.png", Path.Combine(pack.FullPath, "Grove", "a.png"), null)],
            Noted, Nothing);

        var loaded = AppliedRecord.Load(record);

        Assert.Equal(2, loaded.Entries.Count);
        Assert.Equal("dark", loaded.Entries["grove\\A.PNG"].Pack);
        Assert.Null(loaded.Entries["Grove\\b.png"].Pack);
        Assert.Equal(Noted, loaded.Entries["Grove\\b.png"].At);
    }

    [Fact]
    public void Note_RecordsTheSourceRelativeToTheLibraryWhenTheWriteLanded()
    {
        using var tmp = new TempDir();
        var (record, game, library, pack) = Arrange(tmp, "art");

        AppliedRecord.Note(record, game, library, [Source(pack)], Noted, Nothing);

        var entry = AppliedRecord.Load(record).Entries["Grove\\a.png"];
        Assert.Equal(Path.Combine("packs", "dark", "Grove", "a.png"), entry.Source);
        Assert.Equal("dark", entry.Pack);
        Assert.Equal(FileHasher.Hash(Path.Combine(game, "Grove", "a.png")), entry.Hash);
        Assert.Equal(entry.Hash, entry.SourceHash);
    }

    [Fact]
    public void Note_RecordsAWriteThatLandedAsNeitherTheSourceNorTheOldFile()
    {
        using var tmp = new TempDir();
        var (record, game, library, pack) = Arrange(tmp, "fitted to 2048x1151");
        var previous = Path.Combine(tmp.Path, "previous.png");
        File.WriteAllText(previous, "the art the game had");

        // A picture fitted on the way in lands as neither the source's bytes nor the ones it replaced.
        AppliedRecord.Note(record, game, library, [Source(pack)], Noted, _ => FileHasher.Hash(previous));

        var entry = AppliedRecord.Load(record).Entries["Grove\\a.png"];
        Assert.Equal(FileHasher.Hash(Path.Combine(game, "Grove", "a.png")), entry.Hash);
        Assert.Equal(FileHasher.Hash(Path.Combine(pack.FullPath, "Grove", "a.png")), entry.SourceHash);
        Assert.NotEqual(entry.Hash, entry.SourceHash);
    }

    [Fact]
    public void Note_LeavesTheOldEntryWhenTheWriteNeverLanded()
    {
        using var tmp = new TempDir();
        var (record, game, library, pack) = Arrange(tmp, "art");
        AppliedRecord.Note(record, game, library, [Source(pack)], Noted, Nothing);
        var applied = AppliedRecord.Load(record).Entries["Grove\\a.png"];

        // The write never ran: the game file is not the source's bytes and not one byte different from before.
        var gameFile = Path.Combine(game, "Grove", "a.png");
        File.WriteAllText(gameFile, "something else");
        var before = FileHasher.Hash(gameFile);
        AppliedRecord.Note(record, game, library, [Source(pack)], Noted.AddHours(1), _ => before);

        Assert.Equal(applied, AppliedRecord.Load(record).Entries["Grove\\a.png"]);
    }

    [Fact]
    public void Note_DropsTheEntryWhenTheGameFileHasGone()
    {
        using var tmp = new TempDir();
        var (record, game, library, pack) = Arrange(tmp, "art");
        AppliedRecord.Note(record, game, library, [Source(pack)], Noted, Nothing);

        File.Delete(Path.Combine(game, "Grove", "a.png"));
        AppliedRecord.Note(record, game, library, [Source(pack)], Noted.AddHours(1), Nothing);

        Assert.Empty(AppliedRecord.Load(record).Entries);
    }

    [Fact]
    public void Forget_DropsThePathsGivenAndLeavesTheRest()
    {
        using var tmp = new TempDir();
        var (record, game, library, pack) = Arrange(tmp, "art");
        new FakeGameTree(game).File("Grove", "b.png", "art");
        AppliedRecord.Note(
            record,
            game,
            library,
            [Source(pack), new AppliedSource("Grove\\b.png", Path.Combine(pack.FullPath, "Grove", "a.png"), "dark")],
            Noted, Nothing);

        AppliedRecord.Forget(record, ["GROVE\\A.PNG"]);

        Assert.Equal("Grove\\b.png", Assert.Single(AppliedRecord.Load(record).Entries).Key);
    }

    [Fact]
    public void FromPack_PutsEverySourceAtTheSameRelativePathInsideThePack()
    {
        var pack = new Pack("dark", Path.Combine("library", "packs", "dark"), []);

        var sources = AppliedSources.FromPack(pack, ["Grove\\a.png", "Backgrounds\\b.jpg"]);

        Assert.Equal(
            [Path.Combine("library", "packs", "dark", "Grove", "a.png"),
                Path.Combine("library", "packs", "dark", "Backgrounds", "b.jpg")],
            sources.Select(s => s.SourceFullPath));
        Assert.All(sources, s => Assert.Equal("dark", s.PackName));
        Assert.Equal("Grove\\a.png", sources[0].GameRelativePath);
    }

    [Fact]
    public void CaptureRecordThenRestore_PutsThePreviousRecordBack()
    {
        using var tmp = new TempDir();
        var (record, game, library, pack) = Arrange(tmp, "art");
        AppliedRecord.Note(record, game, library, [Source(pack)], Noted, Nothing);
        var store = new UndoStore(Path.Combine(tmp.Path, "appdata"));
        var session = store.Begin();
        session.CaptureRecord(record);
        session.Capture(game, ["Grove\\a.png"]);

        // The write after the capture applies something else, and the record follows it.
        File.WriteAllText(Path.Combine(game, "Grove", "a.png"), "other");
        AppliedRecord.Forget(record, ["Grove\\a.png"]);
        var result = store.Restore(session, game, library, "", record);

        Assert.Empty(result.Failures);
        Assert.Equal("art", File.ReadAllText(Path.Combine(game, "Grove", "a.png")));
        Assert.Equal("dark", AppliedRecord.Load(record).Entries["Grove\\a.png"].Pack);
    }

    [Fact]
    public void CaptureRecordOfNoRecordThenRestore_DeletesTheOneTheWriteMade()
    {
        using var tmp = new TempDir();
        var (record, game, library, pack) = Arrange(tmp, "art");
        var store = new UndoStore(Path.Combine(tmp.Path, "appdata"));
        var session = store.Begin();
        session.CaptureRecord(record);
        session.Capture(game, ["Grove\\a.png"]);
        AppliedRecord.Note(record, game, library, [Source(pack)], Noted, Nothing);

        var result = store.Restore(session, game, library, "", record);

        Assert.Empty(result.Failures);
        Assert.False(File.Exists(record));
    }

    [Fact]
    public void Restore_LeavesTheRecordOutOfTheGameSide()
    {
        using var tmp = new TempDir();
        var (record, game, library, pack) = Arrange(tmp, "art");
        AppliedRecord.Note(record, game, library, [Source(pack)], Noted, Nothing);
        var store = new UndoStore(Path.Combine(tmp.Path, "appdata"));
        var session = store.Begin();
        session.CaptureRecord(record);

        store.Restore(session, game, library, "", record);

        // applied.json lives at the session root, so a restore must not mistake it for a game file.
        Assert.False(File.Exists(Path.Combine(game, "applied.json")));
    }

    [Fact]
    public void Renamed_PointsEveryEntryFromTheOldPathAtTheNewOne()
    {
        using var tmp = new TempDir();
        var (record, game, library, pack) = Arrange(tmp, "art");
        new FakeGameTree(game).File("Grove", "b.png", "art");
        AppliedRecord.Note(
            record,
            game,
            library,
            [Source(pack), new AppliedSource("Grove\\b.png", Path.Combine(pack.FullPath, "Grove", "a.png"), "dark")],
            Noted, Nothing);

        AppliedRecord.Renamed(
            record,
            library,
            Path.Combine(pack.FullPath, "Grove", "a.png"),
            Path.Combine(pack.FullPath, "Grove", "sunset.png"));

        var loaded = AppliedRecord.Load(record);
        var renamed = Path.Combine("packs", "dark", "Grove", "sunset.png");
        Assert.Equal(renamed, loaded.Entries["Grove\\a.png"].Source);
        Assert.Equal(renamed, loaded.Entries["Grove\\b.png"].Source);

        // Only the name moved: the hashes say the game still holds the bytes that file gave it.
        Assert.Equal("dark", loaded.Entries["Grove\\a.png"].Pack);
        Assert.Equal(Noted, loaded.Entries["Grove\\a.png"].At);
    }

    [Fact]
    public void Renamed_LeavesAnEntryFromAnotherFileAlone()
    {
        using var tmp = new TempDir();
        var (record, game, library, pack) = Arrange(tmp, "art");
        AppliedRecord.Note(record, game, library, [Source(pack)], Noted, Nothing);

        AppliedRecord.Renamed(
            record,
            library,
            Path.Combine(pack.FullPath, "Grove", "other.png"),
            Path.Combine(pack.FullPath, "Grove", "sunset.png"));

        Assert.Equal(
            Path.Combine("packs", "dark", "Grove", "a.png"),
            AppliedRecord.Load(record).Entries["Grove\\a.png"].Source);
    }

    /// <summary>The lookup a catalog of these maps makes: each one named by its folder, holding the background
    /// slots listed after it. Nothing else of a map's is read by the count.</summary>
    private static MapFolders Folders(params (string Folder, string[] Slots)[] maps) =>
        MapFolders.Of(maps.Select(map => new MapEntry(
            map.Folder,
            map.Folder,
            new LevelDesc(map.Folder, map.Folder, new CameraBounds(0, 0, 100, 50), [], []),
            [],
            [],
            map.Slots,
            [])));

    [Fact]
    public void MapsPerPack_CountsMapFoldersPerPackAndPassesOverTheRest()
    {
        using var tmp = new TempDir();
        var record = Path.Combine(tmp.Path, "applied.json");

        // Two packs sharing Grove, a background no map of the catalog's uses, and a picture of the owner's own,
        // whose entry names no pack.
        File.WriteAllText(record, Written(
            ("Grove\\a.png", "dark"),
            ("Grove\\b.png", "dark"),
            ("Grove\\c.png", "light"),
            ("Brawlhaven\\a.png", "dark"),
            ("Backgrounds\\BG_Grove.jpg", "dark"),
            ("Thundergard\\a.png", null)));

        var counts = AppliedRecord.Load(record).MapsPerPack(
            Folders(("Grove", []), ("Brawlhaven", []), ("Thundergard", [])));

        Assert.Equal(2, counts["dark"]);
        Assert.Equal(1, counts["light"]);
        Assert.Equal(2, counts["DARK"]);
        Assert.False(counts.ContainsKey("quiet"));
    }

    [Fact]
    public void MapsPerPack_CountsABackgroundForEveryMapWhoseLevelsUseIt()
    {
        using var tmp = new TempDir();
        var record = Path.Combine(tmp.Path, "applied.json");

        // Backgrounds live in the game's one shared folder, so only the level data says whose they are: this one
        // is on two maps, and the pack that wrote it is on both of them without owning either folder.
        File.WriteAllText(record, Written(("Backgrounds\\BG_Sewer.jpg", "dark")));

        var counts = AppliedRecord.Load(record).MapsPerPack(
            Folders(("Grove", ["BG_Sewer.jpg"]), ("Swamp", ["BG_Sewer.jpg"]), ("Brawlhaven", ["BG_Brawlhaven.jpg"])));

        Assert.Equal(2, counts["dark"]);
    }

    [Fact]
    public void MapsPerPack_PassesOverABackgroundNoMapUses()
    {
        using var tmp = new TempDir();
        var record = Path.Combine(tmp.Path, "applied.json");

        // A background the game ships but no included level names is on no map, so the pack that wrote it is in
        // no map either and stays out of the counts rather than reading as zero.
        File.WriteAllText(record, Written(("Backgrounds\\BG_Unused.jpg", "dark")));

        var counts = AppliedRecord.Load(record).MapsPerPack(Folders(("Grove", ["BG_Grove.jpg"])));

        Assert.False(counts.ContainsKey("dark"));
    }

    /// <summary>An applied.json holding exactly these game-relative paths, each from the named pack or, for a
    /// null, from no pack at all. Written as text because nothing but a write puts an entry in a record.</summary>
    private static string Written(params (string Path, string? Pack)[] entries)
    {
        var files = entries.Select(entry => string.Format(
            "\"{0}\":{{\"source\":\"s\",\"pack\":{1},\"hash\":\"h\",\"sourceHash\":\"h\",\"at\":\"2026-09-16T11:30:00+00:00\"}}",
            entry.Path.Replace("\\", "\\\\"),
            entry.Pack is null ? "null" : $"\"{entry.Pack}\""));
        return $"{{\"version\":{AppliedRecord.SchemaVersion},\"files\":{{{string.Join(",", files)}}}}}";
    }
}
