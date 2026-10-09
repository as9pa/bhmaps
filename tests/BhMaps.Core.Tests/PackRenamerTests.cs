using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class PackRenamerTests
{
    [Fact]
    public void TryRename_ValidName_MovesTheFolderWithItsFiles()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        var file = Path.Combine(lib, "packs", "Dark", "BloodMoon", "a.png");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "art");

        var ok = PackRenamer.TryRename(lib, "Dark", "Night", out var error);

        Assert.True(ok);
        Assert.Equal("", error);
        Assert.False(Directory.Exists(Path.Combine(lib, "packs", "Dark")));
        Assert.Equal("art", File.ReadAllText(Path.Combine(lib, "packs", "Night", "BloodMoon", "a.png")));
    }

    [Fact]
    public void TryRename_WrappedPack_MovesTheOuterFolder()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        Directory.CreateDirectory(Path.Combine(lib, "packs", "anything", "mapArt", "BloodMoon"));

        var ok = PackRenamer.TryRename(lib, "anything", "Moon", out _);

        Assert.True(ok);
        Assert.True(Directory.Exists(Path.Combine(lib, "packs", "Moon", "mapArt", "BloodMoon")));
        Assert.Single(Directory.GetDirectories(Path.Combine(lib, "packs")));
    }

    [Fact]
    public void TryRename_TakenName_Errors()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        Directory.CreateDirectory(Path.Combine(lib, "packs", "Dark"));
        Directory.CreateDirectory(Path.Combine(lib, "packs", "Night"));

        var ok = PackRenamer.TryRename(lib, "Dark", "night", out var error);

        Assert.False(ok);
        Assert.Equal("A pack called night already exists.", error);
        Assert.True(Directory.Exists(Path.Combine(lib, "packs", "Dark")));
    }

    [Fact]
    public void TryRename_CaseOnly_RenamesTheSamePack()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        Directory.CreateDirectory(Path.Combine(lib, "packs", "dark", "BloodMoon"));

        var ok = PackRenamer.TryRename(lib, "dark", "Dark", out var error);

        Assert.True(ok);
        Assert.Equal("", error);
        var folder = Assert.Single(new DirectoryInfo(Path.Combine(lib, "packs")).GetDirectories());
        Assert.Equal("Dark", folder.Name);
        Assert.True(Directory.Exists(Path.Combine(folder.FullName, "BloodMoon")));
    }

    [Fact]
    public void TryRename_BadName_ErrorsWithValidatorMessage()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        Directory.CreateDirectory(Path.Combine(lib, "packs", "Dark"));
        PackNameValidator.IsValid(@"bad\name", out var expected);

        var ok = PackRenamer.TryRename(lib, "Dark", @"bad\name", out var error);

        Assert.False(ok);
        Assert.Equal(expected, error);
        Assert.True(Directory.Exists(Path.Combine(lib, "packs", "Dark")));
    }

    [Fact]
    public void TryRename_MissingPack_Errors()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");

        var ok = PackRenamer.TryRename(lib, "Dark", "Night", out var error);

        Assert.False(ok);
        Assert.Equal("The pack Dark is not there any more.", error);
    }

    [Fact]
    public void TryRename_DiscoveredPack_RenamesTheOuterFolder()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(Path.Combine(lib, "Summer", "mapArt")).File("BloodMoon", "A.png", "art");
        new FakeGameTree(Path.Combine(lib, "packs", "keep")).File("BloodMoon", "B.png", "x");
        var pack = Assert.Single(PackScanner.ScanAll(lib), p => p.IsDiscovered);

        var ok = PackRenamer.TryRename(lib, pack, "Winter", out var error);

        Assert.True(ok);
        Assert.Equal("", error);
        Assert.False(Directory.Exists(Path.Combine(lib, "Summer")));
        Assert.Equal("art", File.ReadAllText(Path.Combine(lib, "Winter", "mapArt", "BloodMoon", "A.png")));
        var packs = Assert.Single(Directory.GetDirectories(Path.Combine(lib, "packs")));
        Assert.Equal("keep", Path.GetFileName(packs));
        Assert.True(File.Exists(Path.Combine(lib, "packs", "keep", "BloodMoon", "B.png")));
    }

    [Fact]
    public void TryRename_DiscoveredPack_TakenSibling_Errors()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(Path.Combine(lib, "Summer", "mapArt")).File("BloodMoon", "A.png", "x");
        Directory.CreateDirectory(Path.Combine(lib, "Winter"));
        var pack = Assert.Single(PackScanner.ScanAll(lib), p => p.IsDiscovered);

        var ok = PackRenamer.TryRename(lib, pack, "winter", out var error);

        Assert.False(ok);
        Assert.Equal("A pack called winter already exists.", error);
        Assert.True(Directory.Exists(Path.Combine(lib, "Summer", "mapArt")));
    }

    [Fact]
    public void TryRename_DiscoveredPack_TakenInPacks_Errors()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(Path.Combine(lib, "Summer", "mapArt")).File("BloodMoon", "A.png", "x");
        new FakeGameTree(Path.Combine(lib, "packs", "Winter")).File("BloodMoon", "B.png", "x");
        var pack = Assert.Single(PackScanner.ScanAll(lib), p => p.IsDiscovered);

        var ok = PackRenamer.TryRename(lib, pack, "Winter", out var error);

        Assert.False(ok);
        Assert.Equal("A pack called Winter already exists.", error);
        Assert.True(Directory.Exists(Path.Combine(lib, "Summer", "mapArt")));
        Assert.False(Directory.Exists(Path.Combine(lib, "Winter")));
    }

    [Fact]
    public void TryRename_DiscoveredPack_CaseOnly_Renames()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(Path.Combine(lib, "summer", "mapArt")).File("BloodMoon", "A.png", "x");
        var pack = Assert.Single(PackScanner.ScanAll(lib), p => p.IsDiscovered);

        var ok = PackRenamer.TryRename(lib, pack, "Summer", out var error);

        Assert.True(ok);
        Assert.Equal("", error);
        var folder = Assert.Single(new DirectoryInfo(lib).GetDirectories(), d => d.Name != "packs");
        Assert.Equal("Summer", folder.Name);
        Assert.True(File.Exists(Path.Combine(folder.FullName, "mapArt", "BloodMoon", "A.png")));
    }

    [Fact]
    public void TryRename_DiscoveredPack_OutsideLibrary_Refused()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        Directory.CreateDirectory(lib);
        var folder = Path.Combine(tmp.Path, "elsewhere", "Summer");
        var content = Path.Combine(folder, "mapArt");
        new FakeGameTree(content).File("BloodMoon", "A.png", "x");
        var pack = new Pack("Summer", content, Array.Empty<GameFolder>(), IsDiscovered: true) { FolderPath = folder };

        var ok = PackRenamer.TryRename(lib, pack, "Winter", out var error);

        Assert.False(ok);
        Assert.StartsWith("Refusing to rename", error);
        Assert.True(Directory.Exists(content));
        Assert.False(Directory.Exists(Path.Combine(tmp.Path, "elsewhere", "Winter")));
    }
}
