using BhMaps.Core.Hashing;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class CustomPictureLibraryTests
{
    private static (GameTree Tree, IReadOnlyList<Pack> Packs, HashCache Cache) Arrange(
        TempDir tmp, Action<string, string> build)
    {
        var game = Path.Combine(tmp.Path, "game");
        var library = Path.Combine(tmp.Path, "lib");
        build(game, library);
        return (GameTreeScanner.Scan(game), PackScanner.ScanAll(library), HashCache.Load(tmp.Sub("cache.json")));
    }

    private static FakeGameTree PackTree(string library, string packName) =>
        new(Path.Combine(library, "packs", packName));

    /// <summary>One map for <paramref name="folder"/> with the background slots given.</summary>
    private static MapCatalog Catalog(string folder, params string[] slots) =>
        MapCatalog.Build(new LevelDataModel(
            [
                new LevelDesc(
                    folder,
                    folder,
                    new CameraBounds(0, 0, 100, 50),
                    slots.Select(s => new LevelBackground(s, null, null)).ToList(),
                    []),
            ],
            [new LevelType(folder, folder, false, false)],
            [],
            DateTimeOffset.UtcNow));

    [Fact]
    public void Build_TakesAPackPictureWhoseNameIsNoMapsSlot()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (_, lib) =>
            PackTree(lib, "My Backgrounds").File("Backgrounds", "sunset.jpg", "sun"));

        var picture = Assert.Single(
            CustomPictureLibrary.Build(packs, tree, Catalog("Grove", "BG_Grove.jpg"), cache));

        Assert.Equal("sunset.jpg", picture.DisplayName);
        Assert.Equal("My Backgrounds", picture.PackName);
        Assert.Empty(picture.InGameSlots);
        Assert.Single(picture.LibraryPaths);
    }

    [Fact]
    public void Build_LeavesAPackPictureThatIsAMapsSlotOut()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (_, lib) =>
            PackTree(lib, "flowermap").File("Backgrounds", "BG_Grove.jpg", "flowers"));

        Assert.Empty(CustomPictureLibrary.Build(packs, tree, Catalog("Grove", "BG_Grove.jpg"), cache));
    }

    [Fact]
    public void Build_TakesAGameBackgroundNoPackAccountsFor()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game).File("Backgrounds", "BG_Grove.jpg", "mine");
            PackTree(lib, "Default").File("Backgrounds", "BG_Grove.jpg", "vanilla");
        });

        var picture = Assert.Single(
            CustomPictureLibrary.Build(packs, tree, Catalog("Grove", "BG_Grove.jpg"), cache));

        Assert.Equal("BG_Grove.jpg", picture.DisplayName);
        Assert.Null(picture.PackName);
        Assert.Empty(picture.LibraryPaths);
        Assert.Equal(["BG_Grove.jpg"], picture.InGameSlots);
    }

    [Fact]
    public void Build_LeavesAGameBackgroundThatMatchesAPackFileOut()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game).File("Backgrounds", "BG_Grove.jpg", "flowers");
            PackTree(lib, "flowermap").File("Backgrounds", "BG_Grove.jpg", "flowers");
        });

        Assert.Empty(CustomPictureLibrary.Build(packs, tree, Catalog("Grove", "BG_Grove.jpg"), cache));
    }

    [Fact]
    public void Build_GroupsTheSameBytesUnderTwoNamesIntoOnePicture()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game)
                .File("Backgrounds", "BG_Grove.jpg", "sun")
                .File("Backgrounds", "BG_Sewer.jpg", "sun");
            PackTree(lib, "My Backgrounds").File("Backgrounds", "sunset.jpg", "sun");
        });

        var picture = Assert.Single(CustomPictureLibrary.Build(
            packs, tree, Catalog("Grove", "BG_Grove.jpg", "BG_Sewer.jpg"), cache));

        Assert.Equal("sunset.jpg", picture.DisplayName);
        Assert.Equal("My Backgrounds", picture.PackName);
        Assert.Equal(["BG_Grove.jpg", "BG_Sewer.jpg"], picture.InGameSlots);
    }

    [Fact]
    public void Build_KeepsOneEntryPerPictureAcrossTwoPacksAndNamesItAfterTheFirst()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (_, lib) =>
        {
            PackTree(lib, "alpha").File("Backgrounds", "sunset.jpg", "sun");
            PackTree(lib, "bravo").File("Backgrounds", "copy.jpg", "sun");
        });

        var picture = Assert.Single(CustomPictureLibrary.Build(packs, tree, Catalog("Grove"), cache));

        Assert.Equal("sunset.jpg", picture.DisplayName);
        Assert.Equal("alpha", picture.PackName);
        Assert.Equal(2, picture.LibraryPaths.Count);
    }

    [Fact]
    public void Build_IgnoresFilesThatAreNotJpg()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game).File("Grove", "A.png", "a");
            PackTree(lib, "My Backgrounds").File("Backgrounds", "notes.png", "n");
        });

        Assert.Empty(CustomPictureLibrary.Build(packs, tree, Catalog("Grove"), cache));
    }

    [Fact]
    public void Build_SortsByDisplayName()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (_, lib) =>
            PackTree(lib, "My Backgrounds")
                .File("Backgrounds", "zebra.jpg", "z")
                .File("Backgrounds", "apple.jpg", "a"));

        var pictures = CustomPictureLibrary.Build(packs, tree, Catalog("Grove"), cache);

        Assert.Equal(["apple.jpg", "zebra.jpg"], pictures.Select(p => p.DisplayName));
    }
}
