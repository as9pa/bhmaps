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

    [Fact]
    public void PackScanner_OldLayoutPacksAreNotDiscovered()
    {
        using var tmp = new TempDir();
        new FakeGameTree(Path.Combine(tmp.Path, "packs", "dark")).File("Swamp", "Mud1.png", "d");

        var pack = Assert.Single(PackScanner.ScanAll(tmp.Path));

        Assert.False(pack.IsDiscovered);
        Assert.Equal(Path.Combine(tmp.Path, "packs", "dark"), pack.FullPath);
    }

    [Fact]
    public void PackScanner_FindsNestedMapArtPack()
    {
        using var tmp = new TempDir();
        var content = Path.Combine(tmp.Path, "Summer", "mapArt");
        new FakeGameTree(content).File("BloodMoon", "a.png", "s").File("Backgrounds", "BG_Sewer.jpg", "s");

        var pack = Assert.Single(PackScanner.ScanAll(tmp.Path));

        Assert.Equal("Summer", pack.Name);
        Assert.Equal(content, pack.FullPath);
        Assert.True(pack.IsDiscovered);
        Assert.Equal(2, pack.FileCount);
        Assert.NotNull(pack.FindFolder("bloodmoon"));
    }

    [Fact]
    public void PackScanner_MapArtChildMatchesIgnoringCase()
    {
        using var tmp = new TempDir();
        new FakeGameTree(Path.Combine(tmp.Path, "Summer", "MAPART")).File("BloodMoon", "a.png", "s");

        var pack = Assert.Single(PackScanner.ScanAll(tmp.Path));

        Assert.Equal("Summer", pack.Name);
    }

    [Fact]
    public void PackScanner_ReadsOldAndNestedLayoutsTogether()
    {
        using var tmp = new TempDir();
        new FakeGameTree(Path.Combine(tmp.Path, "packs", "dark")).File("Swamp", "Mud1.png", "d");
        new FakeGameTree(Path.Combine(tmp.Path, "Summer", "mapArt")).File("BloodMoon", "a.png", "s");
        new FakeGameTree(Path.Combine(tmp.Path, "packs", "hidden", "mapArt")).File("BloodMoon", "a.png", "h");

        var result = PackScanner.ScanAll(tmp.Path);

        Assert.Equal(new[] { "dark", "hidden", "Summer" }, result.Select(p => p.Name));
        Assert.Equal(new[] { false, false, true }, result.Select(p => p.IsDiscovered));
    }

    [Fact]
    public void PackScanner_MapArtWithinThreeLevelsIsFoundAndDeeperIsIgnored()
    {
        using var tmp = new TempDir();
        // mapArt at depth 3 (library children are depth 1): found.
        new FakeGameTree(Path.Combine(tmp.Path, "a", "Near", "mapArt")).File("BloodMoon", "a.png", "n");
        // mapArt at depth 4: past the cap.
        new FakeGameTree(Path.Combine(tmp.Path, "b", "c", "Far", "mapArt")).File("BloodMoon", "a.png", "f");

        var pack = Assert.Single(PackScanner.ScanAll(tmp.Path));

        Assert.Equal("Near", pack.Name);
    }

    [Fact]
    public void PackScanner_StopsDescendingBelowAPack()
    {
        using var tmp = new TempDir();
        new FakeGameTree(Path.Combine(tmp.Path, "Outer", "mapArt")).File("BloodMoon", "a.png", "o");
        new FakeGameTree(Path.Combine(tmp.Path, "Outer", "Inner", "mapArt")).File("BloodMoon", "a.png", "i");

        var pack = Assert.Single(PackScanner.ScanAll(tmp.Path));

        Assert.Equal("Outer", pack.Name);
    }

    [Fact]
    public void PackScanner_DuplicateNamesTakeNumericSuffixes()
    {
        using var tmp = new TempDir();
        new FakeGameTree(Path.Combine(tmp.Path, "packs", "Summer")).File("Swamp", "Mud1.png", "p");
        new FakeGameTree(Path.Combine(tmp.Path, "summer", "mapArt")).File("BloodMoon", "a.png", "1");
        new FakeGameTree(Path.Combine(tmp.Path, "Summer (1)", "mapArt")).File("BloodMoon", "a.png", "2");
        new FakeGameTree(Path.Combine(tmp.Path, "x", "Summer", "mapArt")).File("BloodMoon", "a.png", "3");

        var result = PackScanner.ScanAll(tmp.Path);
        string NameOf(string fullPath) => result.Single(p => p.FullPath == fullPath).Name;

        Assert.Equal(4, result.Count);
        Assert.Equal(4, result.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("Summer", NameOf(Path.Combine(tmp.Path, "packs", "Summer")));
        Assert.Equal("Summer (1)", NameOf(Path.Combine(tmp.Path, "Summer (1)", "mapArt")));
        Assert.Equal("summer (2)", NameOf(Path.Combine(tmp.Path, "summer", "mapArt")));
        Assert.Equal("Summer (3)", NameOf(Path.Combine(tmp.Path, "x", "Summer", "mapArt")));
    }

    [Fact]
    public void PackScanner_SkipsJunctions()
    {
        using var tmp = new TempDir();
        var outside = Path.Combine(tmp.Path, "outside");
        new FakeGameTree(Path.Combine(outside, "mapArt")).File("BloodMoon", "a.png", "o");
        var library = Path.Combine(tmp.Path, "lib");
        Directory.CreateDirectory(library);
        var link = Path.Combine(library, "Linked");
        if (!TryCreateJunction(link, outside))
        {
            // No junction support on this machine: nothing to test.
            return;
        }

        Assert.Empty(PackScanner.ScanAll(library));
        Directory.Delete(link);
    }

    // Unreadable folders are not simulated here: denying read access portably on Windows needs ACL edits the
    // test account may not be allowed to undo. The scanner's catch covers them; the junction test covers the
    // reparse point skip.

    private static bool TryCreateJunction(string link, string target)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });
            process!.WaitForExit();
            return process.ExitCode == 0 && Directory.Exists(link);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
