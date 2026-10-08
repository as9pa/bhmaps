using BhMaps.Core.Operations;
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
}
