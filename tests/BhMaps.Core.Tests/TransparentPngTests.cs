using BhMaps.Core.Imaging;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class TransparentPngTests
{
    [Fact]
    public void IsFullyTransparent_TrueForAnAllZeroAlphaPng()
    {
        using var tmp = new TempDir();
        var path = SyntheticImage.SavePng(tmp.Sub("clear.png"), 8, 8, (_, _) => (255, 0, 0, 0));

        Assert.True(TransparentPng.IsFullyTransparent(path));
    }

    [Fact]
    public void IsFullyTransparent_FalseWhenOnePixelIsOpaque()
    {
        using var tmp = new TempDir();
        var path = SyntheticImage.SavePng(tmp.Sub("almost.png"), 8, 8,
            (x, y) => (255, 0, 0, (byte)(x == 3 && y == 3 ? 1 : 0)));

        Assert.False(TransparentPng.IsFullyTransparent(path));
    }

    [Fact]
    public void IsFullyTransparent_FalseForAnOpaqueImage()
    {
        using var tmp = new TempDir();

        Assert.False(TransparentPng.IsFullyTransparent(SyntheticImage.SaveQuadrants(tmp.Sub("solid.png"), 8, 8)));
    }

    [Fact]
    public void IsFullyTransparent_FalseForAMissingOrUndecodableFile()
    {
        using var tmp = new TempDir();
        var junk = tmp.Sub("junk.png");
        File.WriteAllText(junk, "not an image");

        Assert.False(TransparentPng.IsFullyTransparent(junk));
        Assert.False(TransparentPng.IsFullyTransparent(Path.Combine(tmp.Path, "nope.png")));
    }
}
