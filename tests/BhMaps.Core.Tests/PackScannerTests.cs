using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class PackScannerTests
{
    private static void WriteFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "art");
    }

    [Fact]
    public void ScanAll_PackWrappingMapArt_IsOnePackReadFromTheChild()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        var outer = Path.Combine(lib, "packs", "anything");
        WriteFile(Path.Combine(outer, "mapArt", "BloodMoon", "a.png"));
        WriteFile(Path.Combine(outer, "mapArt", "Backgrounds", "b.jpg"));

        var pack = Assert.Single(PackScanner.ScanAll(lib));

        Assert.Equal("anything", pack.Name);
        Assert.EndsWith("mapArt", pack.FullPath);
        Assert.Equal(Path.Combine(outer, "mapArt"), pack.FullPath);
        Assert.Equal(outer, pack.FolderPath);
        Assert.NotNull(pack.FindFolder("BloodMoon"));
        Assert.NotNull(pack.FindFolder("Backgrounds"));
        Assert.Null(pack.FindFolder("mapArt"));
    }

    [Fact]
    public void ScanAll_PlainPack_IsUnchanged()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        var folder = Path.Combine(lib, "packs", "plain");
        WriteFile(Path.Combine(folder, "BloodMoon", "a.png"));

        var pack = Assert.Single(PackScanner.ScanAll(lib));

        Assert.Equal("plain", pack.Name);
        Assert.Equal(folder, pack.FullPath);
        Assert.Equal(folder, pack.FolderPath);
        Assert.NotNull(pack.FindFolder("BloodMoon"));
    }

    [Fact]
    public void ScanAll_FolderNamedMapArtUnderPacks_IsAPackCalledMapArt()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        var folder = Path.Combine(lib, "packs", "mapArt");
        WriteFile(Path.Combine(folder, "BloodMoon", "a.png"));

        var pack = Assert.Single(PackScanner.ScanAll(lib));

        Assert.Equal("mapArt", pack.Name);
        Assert.Equal(folder, pack.FullPath);
        Assert.NotNull(pack.FindFolder("BloodMoon"));
    }

    [Fact]
    public void PackRootFor_WrappedPack_IsTheMapArtChild()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        Directory.CreateDirectory(Path.Combine(lib, "packs", "anything", "MAPART"));

        Assert.Equal(Path.Combine(lib, "packs", "anything", "MAPART"), PackScanner.PackRootFor(lib, "anything"));
    }

    [Fact]
    public void PackRootFor_PlainOrMissingPack_IsThePackFolder()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        Directory.CreateDirectory(Path.Combine(lib, "packs", "plain", "BloodMoon"));

        Assert.Equal(Path.Combine(lib, "packs", "plain"), PackScanner.PackRootFor(lib, "plain"));
        Assert.Equal(Path.Combine(lib, "packs", "missing"), PackScanner.PackRootFor(lib, "missing"));
    }
}
