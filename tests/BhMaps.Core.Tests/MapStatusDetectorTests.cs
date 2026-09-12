using BhMaps.Core.Hashing;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class MapStatusDetectorTests
{
    private static (GameTree Tree, IReadOnlyList<Pack> Packs, HashCache Cache) Arrange(TempDir tmp, Action<string, string> build)
    {
        var game = Path.Combine(tmp.Path, "game");
        var library = Path.Combine(tmp.Path, "lib");
        build(game, library);
        return (GameTreeScanner.Scan(game), PackScanner.ScanAll(library), HashCache.Load(tmp.Sub("cache.json")));
    }

    private static FakeGameTree PackTree(string library, string packName) =>
        new(Path.Combine(library, "packs", packName));

    /// <summary>A level-data catalog holding one map for <paramref name="folder"/> with the background slots given.</summary>
    private static MapCatalog Catalog(string folder, params string[] backgroundSlots) =>
        MapCatalog.Build(new LevelDataModel(
            [
                new LevelDesc(
                    folder,
                    folder,
                    new CameraBounds(0, 0, 100, 50),
                    backgroundSlots.Select(s => new LevelBackground(s, null, null)).ToList(),
                    []),
            ],
            [new LevelType(folder, folder, false, false)],
            [],
            DateTimeOffset.UtcNow));

    [Fact]
    public void Detect_ReportsDefaultWhenEveryFileMatchesTheDefaultPack()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game).File("Grove", "A.png", "a").File("Grove", "B.png", "b");
            PackTree(lib, "Default").File("Grove", "A.png", "a").File("Grove", "B.png", "b");
        });

        var status = MapStatusDetector.Detect(MapCatalog.FromFolders(tree), tree, packs, cache)["Grove"];

        Assert.Equal(MapState.Default, status.State);
        Assert.Equal("Default", status.Text);
        Assert.False(status.IsColoured);
        Assert.Equal([@"Grove\A.png", @"Grove\B.png"], status.Files.Select(f => f.RelativePath));
        Assert.All(status.Files, f => Assert.Equal(MapFileState.Default, f.State));
        Assert.All(status.Files, f => Assert.Equal("Default", f.Text));
    }

    [Fact]
    public void Detect_ReportsThePackNameWhenFilesMatchAnotherPack()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game).File("Grove", "A.png", "dark-a");
            PackTree(lib, "Default").File("Grove", "A.png", "a");
            PackTree(lib, "dark").File("Grove", "A.png", "dark-a");
        });

        var status = MapStatusDetector.Detect(MapCatalog.FromFolders(tree), tree, packs, cache)["Grove"];

        Assert.Equal(MapState.Packs, status.State);
        Assert.Equal("dark", status.Text);
        Assert.False(status.IsColoured);
        Assert.Equal(MapFileState.Pack, status.Files.Single().State);
        Assert.Equal(["dark"], status.Files.Single().PackNames);
        Assert.Equal("dark", status.Files.Single().Text);
    }

    [Fact]
    public void Detect_ListsTwoPackNamesThenPlusNForMore()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game).File("Grove", "A.png", "a");
            foreach (var pack in new[] { "alpha", "bravo", "charlie", "delta" })
            {
                PackTree(lib, pack).File("Grove", "A.png", "a");
            }
        });

        var status = MapStatusDetector.Detect(MapCatalog.FromFolders(tree), tree, packs, cache)["Grove"];

        Assert.Equal(MapState.Packs, status.State);
        Assert.Equal(["alpha", "bravo", "charlie", "delta"], status.PackNames);
        Assert.Equal("alpha, bravo +2", status.Text);
    }

    [Fact]
    public void Detect_PrefersANonDefaultPackWhenAFileMatchesBoth()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game).File("Grove", "A.png", "a");
            PackTree(lib, "Default").File("Grove", "A.png", "a");
            PackTree(lib, "dark").File("Grove", "A.png", "a");
        });

        var status = MapStatusDetector.Detect(MapCatalog.FromFolders(tree), tree, packs, cache)["Grove"];

        var file = status.Files.Single();
        Assert.Equal(MapFileState.Pack, file.State);
        Assert.Equal(["dark"], file.PackNames);
        Assert.Equal("dark", file.Text);
        Assert.Equal("dark", status.Text);
    }

    [Fact]
    public void Detect_ReportsCustomWhenOneFileMatchesNothing()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game).File("Grove", "A.png", "a").File("Grove", "B.png", "mine");
            PackTree(lib, "Default").File("Grove", "A.png", "a").File("Grove", "B.png", "b");
        });

        var status = MapStatusDetector.Detect(MapCatalog.FromFolders(tree), tree, packs, cache)["Grove"];

        Assert.Equal(MapState.Custom, status.State);
        Assert.Equal("In game only", status.Text);
        Assert.False(status.IsColoured);
        var file = status.Files.Single(f => f.RelativePath == @"Grove\B.png");
        Assert.Equal(MapFileState.Custom, file.State);
        Assert.Equal("In game only", file.Text);
    }

    [Fact]
    public void Detect_ReportsMissingWhenTheDefaultPackHasAFileTheGameLacks()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game).File("Grove", "A.png", "a");
            PackTree(lib, "Default").File("Grove", "A.png", "a").File("Grove", "B.png", "b");
        });

        var status = MapStatusDetector.Detect(MapCatalog.FromFolders(tree), tree, packs, cache)["Grove"];

        Assert.Equal(MapState.Missing, status.State);
        Assert.Equal("Missing", status.Text);
        Assert.True(status.IsColoured);
        var file = status.Files.Single(f => f.RelativePath == @"Grove\B.png");
        Assert.Equal(MapFileState.Missing, file.State);
        Assert.Equal("Missing", file.Text);
    }

    [Fact]
    public void Detect_PrefersMissingOverCustom()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game).File("Grove", "A.png", "mine");
            PackTree(lib, "Default").File("Grove", "A.png", "a").File("Grove", "B.png", "b");
        });

        var status = MapStatusDetector.Detect(MapCatalog.FromFolders(tree), tree, packs, cache)["Grove"];

        Assert.Equal(MapFileState.Custom, status.Files.Single(f => f.RelativePath == @"Grove\A.png").State);
        Assert.Equal(MapFileState.Missing, status.Files.Single(f => f.RelativePath == @"Grove\B.png").State);
        Assert.Equal(MapState.Missing, status.State);
        Assert.Equal("Missing", status.Text);
    }

    [Fact]
    public void Detect_IncludesTheMapsBackgroundSlotsInTheFileList()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game).File("Grove", "A.png", "a").File("Backgrounds", "BG_Grove.jpg", "mine");
            PackTree(lib, "Default").File("Grove", "A.png", "a").File("Backgrounds", "BG_Grove.jpg", "bg");
        });

        var status = MapStatusDetector.Detect(Catalog("Grove", "BG_Grove.jpg"), tree, packs, cache)["Grove"];

        var file = status.Files.Single(f => f.RelativePath == @"Backgrounds\BG_Grove.jpg");
        Assert.Equal(MapFileState.Custom, file.State);
        Assert.Equal(MapState.Custom, status.State);
    }

    [Fact]
    public void Detect_ReturnsAnEntryForEveryMapInTheCatalogAndNoneForThemeFolders()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, _) =>
            new FakeGameTree(game)
                .File("Grove", "A.png", "a")
                .File("Enigma", "A.png", "a")
                .File("Halloween", "A.png", "a"));

        var statuses = MapStatusDetector.Detect(MapCatalog.FromFolders(tree), tree, packs, cache);

        Assert.Equal(["Enigma", "Grove"], statuses.Keys.Order(StringComparer.OrdinalIgnoreCase));
        Assert.False(statuses.ContainsKey("Halloween"));
    }

    [Fact]
    public void Detect_HandlesAMapFolderThatDoesNotExistOnDisk()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        var library = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(game).File("Enigma", "A.png", "a");
        PackTree(library, "Default").File("Grove", "A.png", "a");

        var tree = GameTreeScanner.Scan(game);
        var catalog = Catalog("Grove");
        var cache = HashCache.Load(tmp.Sub("cache.json"));

        var missing = MapStatusDetector.Detect(catalog, tree, PackScanner.ScanAll(library), cache)["Grove"];

        Assert.Equal(MapState.Missing, missing.State);
        Assert.Equal("Missing", missing.Text);
        Assert.Equal([@"Grove\A.png"], missing.Files.Select(f => f.RelativePath));

        var custom = MapStatusDetector.Detect(catalog, tree, Array.Empty<Pack>(), cache)["Grove"];

        Assert.Equal(MapState.Custom, custom.State);
        Assert.Equal("In game only", custom.Text);
        Assert.Empty(custom.Files);
    }
}
