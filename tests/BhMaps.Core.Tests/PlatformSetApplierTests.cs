using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class PlatformSetApplierTests
{
    private static FakeGameTree PackTree(string library, string packName) =>
        new(Path.Combine(PackScanner.PacksRoot(library), packName));

    private static Pack Scan(string library, string packName) =>
        PackScanner.ScanPack(Path.Combine(PackScanner.PacksRoot(library), packName));

    private static string Read(string root, string folder, string name) =>
        File.ReadAllText(Path.Combine(root, folder, name));

    [Fact]
    public void SetsFor_ListsTheDefaultPackFirstThenEveryPackWithFilesForThatFolder()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        PackTree(lib, "dark").File("Grove", "a.png", "dark-a");
        PackTree(lib, "Default").File("Grove", "a.png", "default-a");
        PackTree(lib, "flowers").File("Grove", "b.png", "flowers-b");

        var sets = PlatformSetApplier.SetsFor("grove", PackScanner.ScanAll(lib));

        Assert.Equal(new[] { "Default", "dark", "flowers" }, sets.Select(p => p.Name));
    }

    [Fact]
    public void SetsFor_OmitsPacksWithNoFilesForThatFolder()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        PackTree(lib, "dark").File("Grove", "a.png", "dark-a");
        PackTree(lib, "wallpapers").File("Backgrounds", "BG_Grove.jpg", "wallpapers-bg");

        var sets = PlatformSetApplier.SetsFor("Grove", PackScanner.ScanAll(lib));

        Assert.Equal(new[] { "dark" }, sets.Select(p => p.Name));
    }

    [Fact]
    public void Apply_CopiesEveryFileThePackHasForThatFolder()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(game).File("Grove", "a.png", "game-a");
        PackTree(lib, "dark")
            .File("Grove", "a.png", "dark-a")
            .File("Grove", "b.png", "dark-b")
            .File("Enigma", "c.png", "dark-c");
        var pack = Scan(lib, "dark");
        var progress = new List<string>();

        var result = PlatformSetApplier.Apply(pack, "grove", game, new SyncProgress(progress));

        Assert.Equal(2, result.Copied);
        Assert.Empty(result.Failures);
        Assert.Equal("dark-a", Read(game, "Grove", "a.png"));
        Assert.Equal("dark-b", Read(game, "Grove", "b.png"));
        Assert.False(Directory.Exists(Path.Combine(game, "Enigma")));
        Assert.Equal(2, progress.Count);
        Assert.Equal(
            new[] { Path.Combine("Grove", "a.png"), Path.Combine("Grove", "b.png") },
            PlatformSetApplier.TargetPaths(pack, "grove"));
    }

    [Fact]
    public void Apply_CopiesFullyTransparentFilesLikeAnyOther()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        var lib = Path.Combine(tmp.Path, "lib");
        var source = SyntheticImage.SavePng(
            Path.Combine(PackScanner.PacksRoot(lib), "dark", "Grove", "clear.png"), 8, 8, (_, _) => (255, 0, 0, 0));

        var result = PlatformSetApplier.Apply(Scan(lib, "dark"), "Grove", game);

        Assert.Equal(1, result.Copied);
        Assert.Empty(result.Failures);
        var target = Path.Combine(game, "Grove", "clear.png");
        Assert.True(File.Exists(target));
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(target));
    }

    [Fact]
    public void TransparentFiles_ListsOnlyTheFullyTransparentPngs()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        var packRoot = Path.Combine(PackScanner.PacksRoot(lib), "dark");
        SyntheticImage.SavePng(Path.Combine(packRoot, "Grove", "clear.png"), 8, 8, (_, _) => (255, 0, 0, 0));
        SyntheticImage.SaveQuadrants(Path.Combine(packRoot, "Grove", "solid.png"), 8, 8);

        // Transparent pixels under a .jpg name: a background is never listed, whatever it holds.
        SyntheticImage.SavePng(Path.Combine(packRoot, "Backgrounds", "BG_Grove.jpg"), 8, 8, (_, _) => (255, 0, 0, 0));

        var transparent = PlatformSetApplier.TransparentFiles(Scan(lib, "dark"));

        Assert.Equal(new[] { Path.Combine("Grove", "clear.png") }, transparent);
    }

    [Fact]
    public void ApplyAll_StacksSoAnEarlierPacksFilesSurviveWhereTheLaterPackHasNone()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        var lib = Path.Combine(tmp.Path, "lib");
        PackTree(lib, "A")
            .File("Grove", "a.png", "A-a")
            .File("Grove", "b.png", "A-b")
            .File("Enigma", "c.png", "A-c");
        PackTree(lib, "B").File("Grove", "a.png", "B-a");

        PackApplier.ApplyPack(Scan(lib, "A"), game);
        PackApplier.ApplyPack(Scan(lib, "B"), game);

        Assert.Equal("B-a", Read(game, "Grove", "a.png"));
        Assert.Equal("A-b", Read(game, "Grove", "b.png"));
        Assert.Equal("A-c", Read(game, "Enigma", "c.png"));
    }

    [Fact]
    public void ApplyAll_NeverDeletesAGameFileThePackDoesNotHave()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(game).File("Grove", "keep.png", "game-keep").File("Swamp", "Mud1.png", "game-mud");
        PackTree(lib, "dark").File("Grove", "a.png", "dark-a");

        var result = PackApplier.ApplyPack(Scan(lib, "dark"), game);

        Assert.Equal(1, result.Copied);
        Assert.Equal("dark-a", Read(game, "Grove", "a.png"));
        Assert.Equal("game-keep", Read(game, "Grove", "keep.png"));
        Assert.Equal("game-mud", Read(game, "Swamp", "Mud1.png"));
    }

    [Fact]
    public void Export_CopiesTheWholePackUnderAFolderNamedAfterIt()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        PackTree(lib, "flower").File("Grove", "a.png", "flower-a").File("Backgrounds", "BG_Grove.jpg", "flower-bg");
        var destination = Path.Combine(tmp.Path, "export");
        Directory.CreateDirectory(destination);
        var progress = new List<string>();

        var result = PackExporter.Export(Scan(lib, "flower"), destination, new SyncProgress(progress));

        Assert.Equal(2, result.Copied);
        Assert.Empty(result.Failures);
        Assert.Equal("flower-a", Read(Path.Combine(destination, "flower"), "Grove", "a.png"));
        Assert.Equal("flower-bg", Read(Path.Combine(destination, "flower"), "Backgrounds", "BG_Grove.jpg"));
        Assert.Equal(2, progress.Count);
    }

    [Fact]
    public void Export_CreatesTheDestinationWhenItIsMissing()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        PackTree(lib, "flower").File("Grove", "a.png", "flower-a");
        var destination = Path.Combine(tmp.Path, "export", "nested");
        Assert.False(Directory.Exists(destination));

        var result = PackExporter.Export(Scan(lib, "flower"), destination);

        Assert.Equal(1, result.Copied);
        Assert.Empty(result.Failures);
        Assert.Equal("flower-a", Read(Path.Combine(destination, "flower"), "Grove", "a.png"));
    }

    [Fact]
    public void Export_RecordsAFailureAndContinuesWhenOneTargetIsLocked()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        PackTree(lib, "flower").File("Grove", "a.png", "flower-a").File("Grove", "b.png", "flower-b");
        var destination = Path.Combine(tmp.Path, "export");
        var lockedPath = Path.Combine(destination, "flower", "Grove", "a.png");
        Directory.CreateDirectory(Path.GetDirectoryName(lockedPath)!);
        File.WriteAllText(lockedPath, "stale");

        using (new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = PackExporter.Export(Scan(lib, "flower"), destination);

            Assert.Equal(1, result.Copied);
            var failure = Assert.Single(result.Failures);
            Assert.Equal(lockedPath, failure.Path);
            Assert.NotEmpty(failure.Error);
        }

        Assert.Equal("flower-b", Read(Path.Combine(destination, "flower"), "Grove", "b.png"));
        Assert.Equal("stale", File.ReadAllText(lockedPath));
    }
}
