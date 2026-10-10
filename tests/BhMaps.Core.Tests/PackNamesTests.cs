using BhMaps.Core.Operations;

namespace BhMaps.Core.Tests;

public class PackNamesTests
{
    [Fact]
    public void NoPacksGivesTheStem()
    {
        Assert.Equal("Custom Pack", PackNames.NextFree([]));
    }

    [Fact]
    public void OtherNamesDoNotTakeTheStem()
    {
        Assert.Equal("Custom Pack", PackNames.NextFree(["My Backgrounds", "Custom Pack 2"]));
    }

    [Fact]
    public void TakenStemGivesTwo()
    {
        Assert.Equal("Custom Pack 2", PackNames.NextFree(["Custom Pack"]));
    }

    [Fact]
    public void SkipsTakenNumbers()
    {
        Assert.Equal("Custom Pack 4", PackNames.NextFree(["Custom Pack", "Custom Pack 2", "Custom Pack 3", "Custom Pack 5"]));
    }

    [Fact]
    public void MatchesCaseInsensitively()
    {
        Assert.Equal("Custom Pack 3", PackNames.NextFree(["custom pack", "CUSTOM PACK 2"]));
    }

    [Fact]
    public void ToleratesDuplicates()
    {
        Assert.Equal("Custom Pack 2", PackNames.NextFree(["Custom Pack", "custom pack"]));
    }

    [Fact]
    public void UsesTheGivenStem()
    {
        Assert.Equal("Flower 2", PackNames.NextFree(["Flower"], "Flower"));
    }
}
