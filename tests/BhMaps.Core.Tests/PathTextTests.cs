using BhMaps.Core.Settings;

namespace BhMaps.Core.Tests;

public sealed class PathTextTests
{
    [Fact]
    public void MiddleTruncate_LeavesAPathThatFits()
    {
        const string path = @"C:\bh\packs";
        Assert.Equal(path, PathText.MiddleTruncate(path, 48));
    }

    [Fact]
    public void MiddleTruncate_DropsMiddleSegments()
    {
        Assert.Equal(
            @"C:\Program Files (x86)\...\Brawlhalla\mapArt",
            PathText.MiddleTruncate(@"C:\Program Files (x86)\Steam\steamapps\common\Brawlhalla\mapArt", 48));
    }

    [Fact]
    public void MiddleTruncate_KeepsForwardSlashes()
    {
        Assert.Equal(
            "C:/Program Files (x86)/.../Brawlhalla/mapArt",
            PathText.MiddleTruncate("C:/Program Files (x86)/Steam/steamapps/common/Brawlhalla/mapArt", 48));
    }

    [Fact]
    public void MiddleTruncate_HardCutsASingleLongSegment()
    {
        Assert.Equal("...ckgrounds", PathText.MiddleTruncate(@"C:\a-very-long-folder-of-backgrounds", 12));
    }

    [Fact]
    public void MiddleTruncate_StaysInsideABudgetSmallerThanTheDots() =>
        Assert.Equal("nds", PathText.MiddleTruncate(@"C:\a-very-long-folder-of-backgrounds", 3));

    [Fact]
    public void MiddleTruncate_LeavesAnEmptyPathAlone() => Assert.Equal("", PathText.MiddleTruncate("", 48));
}
