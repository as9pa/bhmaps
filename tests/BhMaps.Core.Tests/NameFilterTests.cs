using BhMaps.Core.Maps;

namespace BhMaps.Core.Tests;

public sealed class NameFilterTests
{
    [Theory]
    [InlineData("Brawlhaven", "", true)]
    [InlineData("Brawlhaven", "haven", true)]
    [InlineData("Brawlhaven", "HAVEN", true)]
    [InlineData("Brawlhaven", "  ", true)]
    [InlineData("Brawlhaven", "dojo", false)]
    [InlineData("", "a", false)]
    public void Matches_IsCaseInsensitiveContains(string name, string search, bool expected) =>
        Assert.Equal(expected, NameFilter.Matches(name, search));
}
