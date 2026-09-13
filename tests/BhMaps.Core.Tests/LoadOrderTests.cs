using BhMaps.Core.Maps;

namespace BhMaps.Core.Tests;

public class LoadOrderTests
{
    [Fact]
    public void PutsTheNamesGivenFirstInTheirOwnOrder()
    {
        Assert.Equal(["C", "A", "B", "D"], LoadOrder.Prioritise(["A", "B", "C", "D"], ["c", "A"]));
    }

    [Fact]
    public void NoNamesGivenLeavesTheOrderAlone()
    {
        Assert.Equal(["A", "B", "C"], LoadOrder.Prioritise(["A", "B", "C"], null));
        Assert.Equal(["A", "B", "C"], LoadOrder.Prioritise(["A", "B", "C"], []));
    }

    [Fact]
    public void DropsANameNothingIsCalled()
    {
        Assert.Equal(["B", "A"], LoadOrder.Prioritise(["A", "B"], ["Nowhere", "B"]));
    }

    [Fact]
    public void PlacesANameGivenTwiceOnce()
    {
        Assert.Equal(["B", "A", "C"], LoadOrder.Prioritise(["A", "B", "C"], ["B", "b"]));
    }
}
