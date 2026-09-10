using BhMaps.Core.LevelData;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class LevelTypesParserTests
{
    [Fact]
    public void ParseTypes_ReadsDisplayNamesAndExclusionFlags()
    {
        var xml = LevelXml.Types(
            ("Grove", "Twilight Grove", false, false),
            ("DevRoom", "Dev Room", true, false),
            ("TestBox", "Test Box", false, true));

        var types = LevelTypesParser.ParseTypes(xml);

        Assert.Equal(3, types.Count);
        Assert.Equal("Twilight Grove", types[0].DisplayName);
        Assert.True(types[0].Included);
        Assert.False(types[1].Included);
        Assert.False(types[2].Included);
    }

    [Fact]
    public void ParseTypes_ReadsTheRealShapeWhereFlagsAreAttributes()
    {
        var xml = "<LevelTypes>"
            + "<LevelType LevelName=\"Template\" DevOnly=\"true\" TestLevel=\"false\">"
            + "<DisplayName>Template</DisplayName><LevelID>0</LevelID></LevelType>"
            + "<LevelType LevelName=\"Grove\" DevOnly=\"false\" TestLevel=\"false\">"
            + "<DisplayName>Twilight Grove</DisplayName><LevelID>1</LevelID></LevelType>"
            + "</LevelTypes>";

        var types = LevelTypesParser.ParseTypes(xml);

        Assert.Equal("Template", types[0].LevelName);
        Assert.False(types[0].Included);
        Assert.Equal("Grove", types[1].LevelName);
        Assert.Equal("Twilight Grove", types[1].DisplayName);
        Assert.True(types[1].Included);
    }

    [Fact]
    public void ParseSets_SplitsTheCommaSeparatedLevelList()
    {
        var sets = LevelTypesParser.ParseSets(LevelXml.Sets(("Ranked1v1", "Grove, Blackguard ,Enigma"), ("Empty", "")));

        Assert.Equal("Ranked1v1", sets[0].Name);
        Assert.Equal(["Grove", "Blackguard", "Enigma"], sets[0].LevelNames);
        Assert.Empty(sets[1].LevelNames);
    }

    [Fact]
    public void ParseSets_TreatsASetWithNoLevelListAsEmpty()
    {
        var xml = "<LevelSetTypes>"
            + "<LevelSetType><LevelSetName>Auto</LevelSetName><DisplayNameKey>x</DisplayNameKey>"
            + "<LevelSetID>1</LevelSetID></LevelSetType>"
            + "<LevelSetType LevelSetName=\"Standard1v1\"><LevelTypes>Brawlhaven,Grove</LevelTypes></LevelSetType>"
            + "</LevelSetTypes>";

        var sets = LevelTypesParser.ParseSets(xml);

        Assert.Equal("Auto", sets[0].Name);
        Assert.Empty(sets[0].LevelNames);
        Assert.Equal("Standard1v1", sets[1].Name);
        Assert.Equal(["Brawlhaven", "Grove"], sets[1].LevelNames);
    }
}
