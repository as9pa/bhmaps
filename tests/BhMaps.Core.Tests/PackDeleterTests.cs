using BhMaps.Core.Operations;
using BhMaps.Core.Packs;
using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class PackDeleterTests
{
    [Fact]
    public void DeletesAnExistingPackAndLeavesPacksRoot()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(Path.Combine(lib, "packs", "flower")).File("BloodMoon", "A.png", "x");
        new FakeGameTree(Path.Combine(lib, "packs", "keep")).File("BloodMoon", "B.png", "x");

        var error = PackDeleter.Delete(lib, "flower");

        Assert.Null(error);
        Assert.False(Directory.Exists(Path.Combine(lib, "packs", "flower")));
        Assert.True(File.Exists(Path.Combine(lib, "packs", "keep", "BloodMoon", "B.png")));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("keep/../..")]
    [InlineData(@"keep\..\..")]
    [InlineData(@"C:\Windows")]
    public void RefusesAnythingThatIsNotAPackFolder(string packName)
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(Path.Combine(lib, "packs", "keep")).File("BloodMoon", "B.png", "x");
        File.WriteAllText(Path.Combine(lib, "outside.txt"), "x");

        var error = PackDeleter.Delete(lib, packName);

        Assert.NotNull(error);
        Assert.True(File.Exists(Path.Combine(lib, "packs", "keep", "BloodMoon", "B.png")));
        Assert.True(File.Exists(Path.Combine(lib, "outside.txt")));
        Assert.True(Directory.Exists(Path.Combine(lib, "packs")));
    }

    [Fact]
    public void ReportsAMissingPack()
    {
        using var tmp = new TempDir();

        var error = PackDeleter.Delete(tmp.Path, "nope");

        Assert.NotNull(error);
        Assert.Contains("not found", error);
    }

    /// <summary>The path guard on its own: every name the public entry point would accept resolves inside packs\, so
    /// these reach <see cref="PackDeleter.DeleteFolder"/> with a path the validator can never produce.</summary>
    [Theory]
    [InlineData("outside")]
    [InlineData("packs")]
    [InlineData(@"packs2\x")]
    public void RefusesAPathThatIsNotAStrictChildOfPacks(string relativeTarget)
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(Path.Combine(lib, "packs", "keep")).File("BloodMoon", "B.png", "x");
        new FakeGameTree(Path.Combine(lib, "packs2", "x")).File("BloodMoon", "C.png", "x");
        new FakeGameTree(Path.Combine(lib, "outside")).File("BloodMoon", "D.png", "x");

        var error = PackDeleter.DeleteFolder(lib, Path.Combine(lib, relativeTarget));

        Assert.NotNull(error);
        Assert.Contains("not a pack folder", error);
        Assert.True(File.Exists(Path.Combine(lib, "packs", "keep", "BloodMoon", "B.png")));
        Assert.True(File.Exists(Path.Combine(lib, "packs2", "x", "BloodMoon", "C.png")));
        Assert.True(File.Exists(Path.Combine(lib, "outside", "BloodMoon", "D.png")));
        Assert.True(Directory.Exists(Path.Combine(lib, "packs")));
    }

    [Fact]
    public void DeletesAPathThatIsAStrictChildOfPacks()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(Path.Combine(lib, "packs", "flower")).File("BloodMoon", "A.png", "x");
        new FakeGameTree(Path.Combine(lib, "packs", "keep")).File("BloodMoon", "B.png", "x");

        var error = PackDeleter.DeleteFolder(lib, Path.Combine(lib, "packs", "flower"));

        Assert.Null(error);
        Assert.False(Directory.Exists(Path.Combine(lib, "packs", "flower")));
        Assert.True(File.Exists(Path.Combine(lib, "packs", "keep", "BloodMoon", "B.png")));
    }

    [Fact]
    public void RefusesToDeleteADiscoveredPack()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        var content = Path.Combine(lib, "Summer", "mapArt");
        new FakeGameTree(content).File("BloodMoon", "A.png", "x");
        var pack = Assert.Single(PackScanner.ScanAll(lib));

        var error = PackDeleter.Delete(lib, pack);

        Assert.NotNull(error);
        Assert.True(File.Exists(Path.Combine(content, "BloodMoon", "A.png")));
    }

    [Fact]
    public void RefusesToRemoveFilesFromADiscoveredPack()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        var content = Path.Combine(lib, "Summer", "mapArt");
        new FakeGameTree(content).File("Backgrounds", "BG_Sewer.jpg", "x");
        var pack = Assert.Single(PackScanner.ScanAll(lib));

        var result = PackCopier.RemoveBackground(pack, "BG_Sewer.jpg");

        Assert.Empty(result.Removed);
        Assert.NotEmpty(result.Failures);
        Assert.True(File.Exists(Path.Combine(content, "Backgrounds", "BG_Sewer.jpg")));
    }
}
