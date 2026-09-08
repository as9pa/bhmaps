using BhMaps.Core.Imaging;
using BhMaps.Core.Model;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class ThumbnailProviderTests
{
    [Fact]
    public void PickRepresentative_ReturnsLargestFileBySize()
    {
        var folder = new GameFolder("X", @"C:\x", new[]
        {
            new GameFile("small.png", @"C:\x\small.png", 10, 1),
            new GameFile("big.png", @"C:\x\big.png", 300, 1),
            new GameFile("mid.png", @"C:\x\mid.png", 20, 1),
        });

        Assert.Equal("big.png", ThumbnailProvider.PickRepresentative(folder)!.Name);
        Assert.Null(ThumbnailProvider.PickRepresentative(new GameFolder("Y", @"C:\y", Array.Empty<GameFile>())));
    }

    [Fact]
    public async Task GetAsync_DecodesAtReducedWidthFrozenAndCached()
    {
        using var tmp = new TempDir();
        var src = SyntheticImage.SaveQuadrants(tmp.Sub("src.png"), 800, 200);
        var provider = new ThumbnailProvider();

        var first = await provider.GetAsync(src, 1);
        var second = await provider.GetAsync(src, 1);
        var other = await provider.GetAsync(src, 2);

        Assert.NotNull(first);
        Assert.Equal(ThumbnailProvider.DecodeWidth, first!.PixelWidth);
        Assert.Equal(60, first.PixelHeight);
        Assert.True(first.IsFrozen);
        Assert.Same(first, second);
        Assert.NotSame(first, other);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullForUnreadableFile()
    {
        using var tmp = new TempDir();
        var bogus = tmp.Sub("bogus.png");
        File.WriteAllText(bogus, "not an image");

        Assert.Null(await new ThumbnailProvider().GetAsync(bogus, 1));
        Assert.Null(ThumbnailProvider.Decode(Path.Combine(tmp.Path, "missing.png")));
    }
}
