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

    [Fact]
    public void PutsTheShownItemsFirstAndKeepsBothPartsInOrder()
    {
        // The 3.2 QA case: under Tournament 1v1 the last shown card sat behind every hidden card before it.
        string[] order = ["Demon Island", "Mammoth Fortress", "Small Terminus", "Temple Climb", "Western Air Temple"];
        HashSet<string> shown = ["Small Terminus", "Demon Island", "Western Air Temple"];

        Assert.Equal(
            ["Demon Island", "Small Terminus", "Western Air Temple", "Mammoth Fortress", "Temple Climb"],
            LoadOrder.ShownFirst(order, shown.Contains));
    }

    [Fact]
    public void ShownFirstLeavesTheOrderAloneWhenEverythingOrNothingIsShown()
    {
        Assert.Equal(["B", "A", "C"], LoadOrder.ShownFirst(["B", "A", "C"], _ => true));
        Assert.Equal(["B", "A", "C"], LoadOrder.ShownFirst(["B", "A", "C"], _ => false));
    }
}
