using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class ScanningTests
{
    [Fact]
    public void Scan_FindsFoldersAndFilesWithMetadata()
    {
        using var tmp = new TempDir();
        FakeGameTree.Standard(tmp.Path);

        var tree = GameTreeScanner.Scan(tmp.Path);

        Assert.Equal(tmp.Path, tree.RootPath);
        Assert.Equal(
            new[] { "Backgrounds", "BloodMoon", "BP8", "BrawlFest", "Mustafar", "Swamp", "Tekken", "Zombie" },
            tree.Folders.Select(f => f.Name));
        var bloodMoon = tree.FindFolder("bloodmoon");
        Assert.NotNull(bloodMoon);
        Assert.Equal("BloodMoon", bloodMoon!.Name);
        Assert.Equal(Path.Combine(tmp.Path, "BloodMoon"), bloodMoon.FullPath);
        Assert.Equal(new[] { "BloodMoon_PlatformA01.png", "BloodMoon_PlatformA02.png" }, bloodMoon.Files.Select(f => f.Name));
        var a01 = bloodMoon.Files[0];
        Assert.Equal(Path.Combine(tmp.Path, "BloodMoon", "BloodMoon_PlatformA01.png"), a01.FullPath);
        Assert.Equal(6, a01.Size);
        Assert.Equal(File.GetLastWriteTimeUtc(a01.FullPath).Ticks, a01.MtimeTicks);
        Assert.NotNull(bloodMoon.FindFile("BLOODMOON_PLATFORMA02.PNG"));
        Assert.Null(bloodMoon.FindFile("nope.png"));
    }

    [Fact]
    public void Scan_SkipsNonImageFilesAndAcceptsAnyExtensionCase()
    {
        using var tmp = new TempDir();
        new FakeGameTree(tmp.Path)
            .File("BloodMoon", "Thumbs.db", "x")
            .File("BloodMoon", "notes.txt", "x")
            .File("BloodMoon", "Plat.PNG", "x")
            .File("BloodMoon", "bg.JPG", "x")
            .File("BloodMoon", "anim.gif", "x");

        var tree = GameTreeScanner.Scan(tmp.Path);

        Assert.Equal(new[] { "bg.JPG", "Plat.PNG" }, tree.Folders.Single().Files.Select(f => f.Name));
    }

    [Fact]
    public void Scan_IgnoresNestedFoldersAndRootFiles()
    {
        using var tmp = new TempDir();
        new FakeGameTree(tmp.Path)
            .File("BloodMoon", "Top.png", "x")
            .File(Path.Combine("BloodMoon", "deeper"), "Hidden.png", "x");
        File.WriteAllText(Path.Combine(tmp.Path, "root.png"), "x");

        var tree = GameTreeScanner.Scan(tmp.Path);

        var folder = Assert.Single(tree.Folders);
        Assert.Equal("BloodMoon", folder.Name);
        Assert.Equal(new[] { "Top.png" }, folder.Files.Select(f => f.Name));
    }

    [Fact]
    public void Scan_MissingPathReturnsEmptyTree()
    {
        using var tmp = new TempDir();
        var missing = Path.Combine(tmp.Path, "nope");

        var tree = GameTreeScanner.Scan(missing);

        Assert.Equal(missing, tree.RootPath);
        Assert.Empty(tree.Folders);
    }

    [Fact]
    public void Scan_OrdersFoldersAndFilesIgnoringCase()
    {
        using var tmp = new TempDir();
        new FakeGameTree(tmp.Path)
            .File("beta", "z.png", "x")
            .File("beta", "Y.png", "x")
            .File("beta", "x.png", "x")
            .Folder("Alpha")
            .Folder("charlie");

        var tree = GameTreeScanner.Scan(tmp.Path);

        Assert.Equal(new[] { "Alpha", "beta", "charlie" }, tree.Folders.Select(f => f.Name));
        Assert.Equal(new[] { "x.png", "Y.png", "z.png" }, tree.FindFolder("beta")!.Files.Select(f => f.Name));
    }

    [Fact]
    public void PackScanner_ReadsEveryFolderUnderPacksRoot()
    {
        using var tmp = new TempDir();
        var packs = Path.Combine(tmp.Path, "packs");
        new FakeGameTree(Path.Combine(packs, "flower")).File("BloodMoon", "BloodMoon_PlatformA01.png", "f");
        new FakeGameTree(Path.Combine(packs, "dark")).File("Backgrounds", "BG_Sewer.jpg", "d").File("Swamp", "Mud1.png", "d");
        File.WriteAllText(Path.Combine(packs, "flower", "loose.png"), "ignored: pack root file");
        new FakeGameTree(Path.Combine(tmp.Path, "flowermap")).File("BloodMoon", "x.png", "not under packs");

        var result = PackScanner.ScanAll(tmp.Path);

        Assert.Equal(new[] { "dark", "flower" }, result.Select(p => p.Name));
        Assert.Equal(Path.Combine(packs, "dark"), result[0].FullPath);
        Assert.Equal(2, result[0].FileCount);
        Assert.Equal(1, result[1].FileCount);
        Assert.NotNull(result[1].FindFolder("BLOODMOON"));
        Assert.Equal(Path.Combine(tmp.Path, "packs"), PackScanner.PacksRoot(tmp.Path));
    }

    [Fact]
    public void PackScanner_MissingPacksRootReturnsEmptyList()
    {
        using var tmp = new TempDir();

        Assert.Empty(PackScanner.ScanAll(tmp.Path));
        Assert.Empty(PackScanner.ScanAll(Path.Combine(tmp.Path, "missing-library")));
    }
}
