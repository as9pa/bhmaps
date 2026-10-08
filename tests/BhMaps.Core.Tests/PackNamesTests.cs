using BhMaps.Core.Operations;

namespace BhMaps.Core.Tests;

public class PackNamesTests
{
    [Fact]
    public void NoPacksGivesTheStem()
    {
        Assert.Equal("New Pack", PackNames.NextFree([]));
    }

    [Fact]
    public void OtherNamesDoNotTakeTheStem()
    {
        Assert.Equal("New Pack", PackNames.NextFree(["My Backgrounds", "New Pack 2"]));
    }

    [Fact]
    public void TakenStemGivesTwo()
    {
        Assert.Equal("New Pack 2", PackNames.NextFree(["New Pack"]));
    }

    [Fact]
    public void SkipsTakenNumbers()
    {
        Assert.Equal("New Pack 4", PackNames.NextFree(["New Pack", "New Pack 2", "New Pack 3", "New Pack 5"]));
    }

    [Fact]
    public void MatchesCaseInsensitively()
    {
        Assert.Equal("New Pack 3", PackNames.NextFree(["new pack", "NEW PACK 2"]));
    }

    [Fact]
    public void ToleratesDuplicates()
    {
        Assert.Equal("New Pack 2", PackNames.NextFree(["New Pack", "new pack"]));
    }

    [Fact]
    public void UsesTheGivenStem()
    {
        Assert.Equal("Flower 2", PackNames.NextFree(["Flower"], "Flower"));
    }
}
