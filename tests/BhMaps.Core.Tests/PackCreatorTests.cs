using BhMaps.Core.Operations;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class PackCreatorTests
{
    [Fact]
    public void TryCreate_ValidName_CreatesFolder()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");

        var ok = PackCreator.TryCreate(lib, "My Backgrounds", out var error);

        Assert.True(ok);
        Assert.Equal("", error);
        Assert.True(Directory.Exists(Path.Combine(lib, "packs", "My Backgrounds")));
    }

    [Fact]
    public void TryCreate_TakenName_Errors()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        Directory.CreateDirectory(Path.Combine(lib, "packs", "My Backgrounds"));

        var ok = PackCreator.TryCreate(lib, "my backgrounds", out var error);

        Assert.False(ok);
        Assert.Equal("A pack called my backgrounds already exists.", error);
        Assert.Single(Directory.GetDirectories(Path.Combine(lib, "packs")));
    }

    [Fact]
    public void TryCreate_BadName_ErrorsWithValidatorMessage()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        PackNameValidator.IsValid(@"bad\name", out var expected);

        var ok = PackCreator.TryCreate(lib, @"bad\name", out var error);

        Assert.False(ok);
        Assert.Equal(expected, error);
        Assert.False(Directory.Exists(Path.Combine(lib, "packs")));
    }
}
