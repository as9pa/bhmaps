using BhMaps.Core.Hashing;
using BhMaps.Core.Model;
using BhMaps.Core.Scanning;
using BhMaps.Core.Status;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class StatusDetectorTests
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

    [Fact]
    public void EmptyFolderIsEmptyEvenWhenAPackHasFilesForIt()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game).Folder("BloodMoon");
            PackTree(lib, "flower").File("BloodMoon", "A.png", "a");
        });

        var status = StatusDetector.Detect(tree, packs, cache).ForFolder("BloodMoon");

        Assert.Equal(FolderState.Empty, status.State);
        Assert.Empty(status.PackNames);
        Assert.Equal("Reset, launch game to regenerate", status.Text);
    }

    [Fact]
    public void AppliedWhenEveryPackFileMatches()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game).File("BloodMoon", "A.png", "a").File("BloodMoon", "B.png", "b");
            PackTree(lib, "flower").File("BloodMoon", "A.png", "a").File("BloodMoon", "B.png", "b");
        });

        var status = StatusDetector.Detect(tree, packs, cache).ForFolder("bloodmoon");

        Assert.Equal(FolderState.Applied, status.State);
        Assert.Equal(new[] { "flower" }, status.PackNames);
        Assert.Equal("flower", status.Text);
    }

    [Fact]
    public void AppliedListsEveryMatchingPack()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game).File("BloodMoon", "A.png", "a").File("BloodMoon", "B.png", "b");
            PackTree(lib, "flower").File("BloodMoon", "A.png", "a");
            PackTree(lib, "dark").File("BloodMoon", "B.png", "b");
        });

        var status = StatusDetector.Detect(tree, packs, cache).ForFolder("BloodMoon");

        Assert.Equal(FolderState.Applied, status.State);
        Assert.Equal(new[] { "dark", "flower" }, status.PackNames);
        Assert.Equal("dark, flower", status.Text);
    }

    [Fact]
    public void UnmanagedWhenOnePackFileDiffers()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game).File("BloodMoon", "A.png", "a").File("BloodMoon", "B.png", "b");
            PackTree(lib, "flower").File("BloodMoon", "A.png", "a").File("BloodMoon", "B.png", "DIFFERENT");
        });

        var status = StatusDetector.Detect(tree, packs, cache).ForFolder("BloodMoon");

        Assert.Equal(FolderState.Unmanaged, status.State);
        Assert.Empty(status.PackNames);
        Assert.Equal("Default or unmanaged", status.Text);
    }

    [Fact]
    public void UnmanagedWhenPackFileIsMissingFromGame()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game).File("BloodMoon", "A.png", "a");
            PackTree(lib, "flower").File("BloodMoon", "A.png", "a").File("BloodMoon", "B.png", "b");
        });

        Assert.Equal(FolderState.Unmanaged, StatusDetector.Detect(tree, packs, cache).ForFolder("BloodMoon").State);
    }

    [Fact]
    public void ExtraGameFilesAreIgnored()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game).File("BloodMoon", "A.png", "a").File("BloodMoon", "B.png", "b").File("BloodMoon", "C.png", "c");
            PackTree(lib, "flower").File("BloodMoon", "A.png", "a");
        });

        var status = StatusDetector.Detect(tree, packs, cache).ForFolder("BloodMoon");

        Assert.Equal(FolderState.Applied, status.State);
        Assert.Equal(new[] { "flower" }, status.PackNames);
    }

    [Fact]
    public void FolderWithNoPacksIsUnmanaged()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, _) => new FakeGameTree(game).File("BloodMoon", "A.png", "a"));

        Assert.Equal(FolderState.Unmanaged, StatusDetector.Detect(tree, packs, cache).ForFolder("BloodMoon").State);
    }

    [Fact]
    public void PerFileStatusListsPacksWithIdenticalCopy()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game).File("BloodMoon", "A.png", "a").File("BloodMoon", "B.png", "b");
            PackTree(lib, "flower").File("BloodMoon", "A.png", "a").File("BloodMoon", "B.png", "zzz");
            PackTree(lib, "dark").File("BloodMoon", "A.png", "a");
        });

        var report = StatusDetector.Detect(tree, packs, cache);

        Assert.Equal(new[] { "dark", "flower" }, report.ForFile("BloodMoon", "a.png").PackNames);
        Assert.Equal("dark, flower", report.ForFile("BloodMoon", "A.png").Text);
        Assert.Empty(report.ForFile("BloodMoon", "B.png").PackNames);
        Assert.Equal("Default or unmanaged", report.ForFile("BloodMoon", "B.png").Text);
        Assert.Empty(report.ForFile("BloodMoon", "Nope.png").PackNames);
        Assert.Equal(FolderState.Empty, report.ForFolder("NoSuchFolder").State);
    }
}
