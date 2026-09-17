using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

/// <summary>The top-up a game update needs: the Default pack gains the new map's art and keeps every file it
/// already holds, whatever its bytes.</summary>
public class DefaultPackAddMissingTests
{
    private static Pack ScanDefault(string libraryPath) => DefaultPack.Find(PackScanner.ScanAll(libraryPath))!;

    private static string PackPath(string libraryPath, params string[] parts) =>
        Path.Combine([PackScanner.PacksRoot(libraryPath), DefaultPack.Name, .. parts]);

    private static LevelDesc Level(string name, string folder, string slot, params string[] platformFiles) =>
        new(name, folder, new CameraBounds(0, 0, 100, 50),
            [new LevelBackground(slot, null, null)],
            [new PlatformNode(0, 0, 1, 1, 1, 0, null, platformFiles.Select(f => new LevelAsset(f, 0, 0, 1, 1)).ToList(), [])]);

    /// <summary>A catalog of one map per level, each under the name the game gives it in LevelTypes.</summary>
    private static MapCatalog Catalog(params (string Display, LevelDesc Level)[] maps) =>
        MapCatalog.Build(new LevelDataModel(
            maps.Select(m => m.Level).ToList(),
            maps.Select(m => new LevelType(m.Level.LevelName, m.Display, false, false)).ToList(),
            [],
            DateTimeOffset.UtcNow));

    /// <summary>The 2026-09-16 update: a Celestial folder and a BG_Celestial_ALL.jpg slot the Default pack, which
    /// was captured before it, has never seen.</summary>
    private static (string Game, string Library, MapCatalog Catalog) Update(string root)
    {
        var game = Path.Combine(root, "game");
        new FakeGameTree(game)
            .File("Celestial", "Celestial_Deco.png", "deco")
            .File("Celestial", "Celestial_Plat.png", "plat")
            .File("Backgrounds", "BG_Celestial_ALL.jpg", "bg");
        var library = Path.Combine(root, "lib");
        new FakeGameTree(PackPath(library)).File("Backgrounds", "BG_Grove.jpg", "grove");
        var catalog = Catalog(("Eternity's End", Level("Celestial", "Celestial", "BG_Celestial_ALL.jpg", "Celestial_Plat.png")));
        return (game, library, catalog);
    }

    [Fact]
    public void AddMissing_AddsTheWholeFolderAndSlotOfAMapTheDefaultPackLacks()
    {
        using var tmp = new TempDir();
        var (game, library, catalog) = Update(tmp.Path);
        var progress = new List<string>();

        var missing = DefaultPack.FindMissing(GameTreeScanner.Scan(game), catalog, ScanDefault(library), _ => true);
        var result = DefaultPack.AddMissing(game, library, missing, new SyncProgress(progress));

        Assert.Equal(
            [
                Path.Combine("Celestial", "Celestial_Deco.png"),
                Path.Combine("Celestial", "Celestial_Plat.png"),
                Path.Combine("Backgrounds", "BG_Celestial_ALL.jpg"),
            ],
            missing.Select(m => m.GameRelativePath));
        Assert.Equal(3, result.Copied);
        Assert.Equal(0, result.Failed);
        Assert.Equal("deco", File.ReadAllText(PackPath(library, "Celestial", "Celestial_Deco.png")));
        Assert.Equal("plat", File.ReadAllText(PackPath(library, "Celestial", "Celestial_Plat.png")));
        Assert.Equal("bg", File.ReadAllText(PackPath(library, "Backgrounds", "BG_Celestial_ALL.jpg")));
        Assert.Equal("grove", File.ReadAllText(PackPath(library, "Backgrounds", "BG_Grove.jpg")));
        Assert.Equal(["Eternity's End", "Eternity's End", "Eternity's End"], progress);
    }

    [Fact]
    public void AddMissing_AddsOnlyTheSlotFileAFolderTheDefaultPackAlreadyHasIsMissing()
    {
        using var tmp = new TempDir();
        var (game, library, catalog) = Update(tmp.Path);
        new FakeGameTree(PackPath(library))
            .File("Celestial", "Celestial_Deco.png", "deco")
            .File("Celestial", "Celestial_Plat.png", "plat");

        var missing = DefaultPack.FindMissing(GameTreeScanner.Scan(game), catalog, ScanDefault(library), _ => true);
        var result = DefaultPack.AddMissing(game, library, missing, null);

        Assert.Equal([Path.Combine("Backgrounds", "BG_Celestial_ALL.jpg")], missing.Select(m => m.GameRelativePath));
        Assert.Equal(1, result.Copied);
        Assert.Equal(["Eternity's End"], result.MapsAdded);
        Assert.Equal("bg", File.ReadAllText(PackPath(library, "Backgrounds", "BG_Celestial_ALL.jpg")));
    }

    [Fact]
    public void AddMissing_NeverWritesOverAFileTheDefaultPackAlreadyHas()
    {
        using var tmp = new TempDir();
        var (game, library, catalog) = Update(tmp.Path);

        // The owner's art is on in the game and the pack's copy is the vanilla one: neither may move.
        new FakeGameTree(PackPath(library)).File("Celestial", "Celestial_Plat.png", "vanilla");
        var defaultPack = ScanDefault(library);
        var map = catalog.Maps[0];

        var missing = DefaultPack.FindMissing(GameTreeScanner.Scan(game), catalog, defaultPack, _ => true);
        var result = DefaultPack.AddMissing(
            game,
            library,

            // A list naming the file anyway, the way a pack written between the scan and the copy would leave it.
            [.. missing, new MissingDefault(Path.Combine("Celestial", "Celestial_Plat.png"), map)],
            null);

        Assert.DoesNotContain(Path.Combine("Celestial", "Celestial_Plat.png"), missing.Select(m => m.GameRelativePath));
        Assert.Equal(2, result.Copied);
        Assert.Equal(0, result.Failed);
        Assert.Equal("vanilla", File.ReadAllText(PackPath(library, "Celestial", "Celestial_Plat.png")));
    }

    [Fact]
    public void FindMissing_SkipsAFileIsVanillaRejects()
    {
        using var tmp = new TempDir();
        var (game, library, catalog) = Update(tmp.Path);
        var slot = Path.Combine("Backgrounds", "BG_Celestial_ALL.jpg");

        // A pack is on that slot, so what the game holds there is applied art and not the game's own.
        var missing = DefaultPack.FindMissing(
            GameTreeScanner.Scan(game), catalog, ScanDefault(library), r => !r.Equals(slot, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(
            [Path.Combine("Celestial", "Celestial_Deco.png"), Path.Combine("Celestial", "Celestial_Plat.png")],
            missing.Select(m => m.GameRelativePath));
    }

    [Fact]
    public void AddMissing_NamesEachMapThatGotAFileOnce()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        new FakeGameTree(game)
            .File("Celestial", "Celestial_Plat.png", "plat")
            .File("Celestial", "Celestial_Deco.png", "deco")
            .File("Atlas", "Atlas_Plat.png", "atlas-plat")
            .File("Backgrounds", "BG_Celestial_ALL.jpg", "bg");
        var library = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(PackPath(library)).File("Atlas", "Atlas_Plat.png", "atlas-plat");
        var catalog = Catalog(
            ("Eternity's End", Level("Celestial", "Celestial", "BG_Celestial_ALL.jpg", "Celestial_Plat.png")),
            ("Atlas", Level("Atlas", "Atlas", "BG_Atlas.jpg", "Atlas_Plat.png")));

        var missing = DefaultPack.FindMissing(GameTreeScanner.Scan(game), catalog, ScanDefault(library), _ => true);
        var result = DefaultPack.AddMissing(game, library, missing, null);

        Assert.Equal(3, result.Copied);
        Assert.Equal(["Eternity's End"], result.MapsAdded);
    }

    [Fact]
    public void FindMissing_IsEmptyWhenTheDefaultPackHasEverythingTheMapsName()
    {
        using var tmp = new TempDir();
        var (game, library, catalog) = Update(tmp.Path);
        new FakeGameTree(PackPath(library))
            .File("Celestial", "Celestial_Deco.png", "deco")
            .File("Celestial", "Celestial_Plat.png", "plat")
            .File("Backgrounds", "BG_Celestial_ALL.jpg", "bg");

        var missing = DefaultPack.FindMissing(GameTreeScanner.Scan(game), catalog, ScanDefault(library), _ => true);

        Assert.Empty(missing);
        Assert.Equal(0, DefaultPack.AddMissing(game, library, missing, null).Copied);
    }
}
