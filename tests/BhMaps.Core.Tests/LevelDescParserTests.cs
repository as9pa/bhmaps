using BhMaps.Core.LevelData;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class LevelDescParserTests
{
    /// <summary>The shape a real LevelDesc_*.xml has: one line, comments, attribute values, a nested seasonal
    /// platform and a sibling-folder asset reference. Built by hand; no game data is copied here.</summary>
    private const string RealShapedLevel =
        "<LevelDesc AssetDir=\"Grove\" LevelName=\"Grove\">"
        + "<!--camera is not 16:9; do not copy these bounds for new map-->"
        + "<CameraBounds H=\"3035\" W=\"4742.15\" X=\"-929.8\" Y=\"-2.5\" />"
        + "<SpawnBotBounds H=\"400\" W=\"900\" X=\"-100\" Y=\"200\" />"
        + "<Background AssetName=\"BG_Grove.jpg\" H=\"1151\" W=\"2048\"/>"
        + "<Platform InstanceName=\"am_Midground11\" X=\"2032.5\" Y=\"728\">"
        + "<Asset AssetName=\"Enviro_Tree3B.png\" H=\"574.48\" W=\"477.62\" X=\"-167.75\" Y=\"-107.85\" />"
        + "<Platform InstanceName=\"Enviro_Tree2B\" ScaleX=\"0.668\" ScaleY=\"0.668\" X=\"-552.25\" Y=\"-404.5\""
        + " Theme=\"Halloween,TWDHalloween\">"
        + "<Asset AssetName=\"../Halloween/HalloweenJackoLanternPumpkinSquat.png\" H=\"120\" W=\"-140\" X=\"0\" Y=\"0\" />"
        + "</Platform></Platform></LevelDesc>";

    [Fact]
    public void Parse_ReadsNameDirAndCameraFromAttributes()
    {
        var xml = LevelXml.Level("Grove", "Grove", LevelXml.Camera(-100, -50, 2000, 1200));

        var level = LevelDescParser.Parse(xml);

        Assert.Equal("Grove", level.LevelName);
        Assert.Equal("Grove", level.AssetDir);
        Assert.Equal(new CameraBounds(-100, -50, 2000, 1200), level.Camera);
    }

    [Fact]
    public void Parse_ReadsNameDirAndCameraFromChildElements()
    {
        var level = LevelDescParser.Parse(LevelXml.LevelWithElements("Grove", "Grove", LevelXml.Camera(0, 0, 100, 50)));

        Assert.Equal("Grove", level.LevelName);
        Assert.Equal("Grove", level.AssetDir);
        Assert.Equal(100, level.Camera.W);
    }

    [Fact]
    public void Parse_ReadsBackgroundsWithOptionalSize()
    {
        var xml = LevelXml.Level("Grove", "Grove", LevelXml.Camera(0, 0, 10, 10)
            + "<Background AssetName=\"BG_Grove.jpg\" W=\"2048\" H=\"1151\" /><Background AssetName=\"BG_Alt.jpg\" />");

        var level = LevelDescParser.Parse(xml);

        Assert.Equal(2, level.Backgrounds.Count);
        Assert.Equal("BG_Grove.jpg", level.Backgrounds[0].AssetName);
        Assert.Equal(2048, level.Backgrounds[0].W);
        Assert.Null(level.Backgrounds[1].W);
    }

    [Fact]
    public void Parse_BuildsANestedPlatformTreeWithDefaultsForAbsentAttributes()
    {
        var xml = LevelXml.Level("Grove", "Grove", LevelXml.Camera(0, 0, 10, 10)
            + "<Platform X=\"10\" Y=\"20\" Scale=\"2\">"
            + "<Asset AssetName=\"a.png\" X=\"1\" Y=\"2\" W=\"3\" H=\"4\" />"
            + "<Platform Rotation=\"90\" ScaleX=\"3\"><Asset AssetName=\"b.png\" X=\"0\" Y=\"0\" W=\"5\" H=\"6\" /></Platform>"
            + "</Platform>");

        var root = Assert.Single(LevelDescParser.Parse(xml).Platforms);

        Assert.Equal(10, root.X);
        Assert.Equal(2, root.Scale);
        Assert.Equal(1, root.ScaleX);
        Assert.Equal(0, root.Rotation);
        Assert.Equal("a.png", Assert.Single(root.Assets).AssetName);
        var child = Assert.Single(root.Children);
        Assert.Equal(90, child.Rotation);
        Assert.Equal(3, child.ScaleX);
        Assert.Equal(1, child.Scale);
    }

    [Fact]
    public void Parse_KeepsNegativeWidthAndHeightAsFlipMarkers()
    {
        var xml = LevelXml.Level("Grove", "Grove", LevelXml.Camera(0, 0, 10, 10)
            + "<Platform><Asset AssetName=\"a.png\" X=\"0\" Y=\"0\" W=\"-3\" H=\"4\" /></Platform>");

        Assert.Equal(-3, LevelDescParser.Parse(xml).Platforms[0].Assets[0].W);
    }

    [Fact]
    public void Parse_KeepsTheThemeAttributeSoTheCompositorCanSkipIt()
    {
        var xml = LevelXml.Level("Grove", "Grove", LevelXml.Camera(0, 0, 10, 10)
            + "<Platform Theme=\"Halloween\"><Asset AssetName=\"pumpkin.png\" X=\"0\" Y=\"0\" W=\"1\" H=\"1\" /></Platform>");

        var node = LevelDescParser.Parse(xml).Platforms[0];

        Assert.Equal("Halloween", node.Theme);
        Assert.True(node.IsThemed);
    }

    [Fact]
    public void Parse_TreatsUnknownContainerElementsAsTransformNodes()
    {
        var xml = LevelXml.Level("Grove", "Grove", LevelXml.Camera(0, 0, 10, 10)
            + "<MovingPlatform X=\"5\"><Platform><Asset AssetName=\"a.png\" X=\"0\" Y=\"0\" W=\"1\" H=\"1\" /></Platform></MovingPlatform>");

        var moving = Assert.Single(LevelDescParser.Parse(xml).Platforms);

        Assert.Equal(5, moving.X);
        Assert.Equal("a.png", moving.Children[0].Assets[0].AssetName);
    }

    [Fact]
    public void FixMalformedAttributes_RepairsTheThreeShipsFile()
    {
        var broken = "<LevelDesc LevelName=\"ThreeShips\" AssetDir=\"ThreeShips\">"
            + "<Platform Initial=\"true\"X=\"5\"><Asset AssetName=\"a.png\" X=\"0\" Y=\"0\" W=\"1\" H=\"1\" /></Platform></LevelDesc>";

        Assert.Contains("\"true\" X=\"5\"", LevelDescParser.FixMalformedAttributes(broken));
        Assert.Equal(5, LevelDescParser.Parse(broken).Platforms[0].X);
    }

    [Fact]
    public void FixMalformedAttributes_LeavesWellFormedXmlAlone()
    {
        var good = "<LevelDesc LevelName=\"Grove\" AssetDir=\"Grove\" />";

        Assert.Equal(good, LevelDescParser.FixMalformedAttributes(good));
    }

    [Fact]
    public void Parse_ReadsARealShapedFileIncludingCommentsAndSeasonalNodes()
    {
        var level = LevelDescParser.Parse(RealShapedLevel);

        Assert.Equal("Grove", level.LevelName);
        Assert.Equal(new CameraBounds(-929.8, -2.5, 4742.15, 3035), level.Camera);
        Assert.Equal(2048, Assert.Single(level.Backgrounds).W);

        // SpawnBotBounds is not one of the four known elements, so decision D2 makes it an assetless transform node.
        Assert.Equal(2, level.Platforms.Count);
        Assert.Empty(level.Platforms[0].Assets);

        var midground = level.Platforms[1];
        Assert.Equal(2032.5, midground.X);
        Assert.Equal("Enviro_Tree3B.png", Assert.Single(midground.Assets).AssetName);
        Assert.False(midground.IsThemed);

        var seasonal = Assert.Single(midground.Children);
        Assert.Equal("Halloween,TWDHalloween", seasonal.Theme);
        Assert.Equal(0.668, seasonal.EffectiveScaleX);
        Assert.Equal(0.668, seasonal.EffectiveScaleY);
        Assert.Equal(-140, seasonal.Assets[0].W);
        Assert.Equal(
            @"Halloween\HalloweenJackoLanternPumpkinSquat.png",
            AssetPath.Resolve(level.AssetDir, seasonal.Assets[0].AssetName));
    }

    [Fact]
    public void Parse_FallsBackToDefaultsWhenANumberCannotBeRead()
    {
        var xml = LevelXml.Level("Grove", "Grove", LevelXml.Camera(0, 0, 10, 10)
            + "<Platform X=\"far left\" Scale=\"\"><Asset AssetName=\"a.png\" W=\"wide\" /></Platform>");

        var node = LevelDescParser.Parse(xml).Platforms[0];

        Assert.Equal(0, node.X);
        Assert.Equal(1, node.Scale);
        Assert.Equal(0, node.Assets[0].X);
        Assert.Equal(0, node.Assets[0].W);
    }

    [Theory]
    [InlineData("Grove", "Snow1.png", @"Grove\Snow1.png")]
    [InlineData("Grove", "../Snow/Snow1.png", @"Snow\Snow1.png")]
    [InlineData("Grove", @"..\Snow\Snow1.png", @"Snow\Snow1.png")]
    public void AssetPath_ResolvesRelativeAndParentReferences(string dir, string asset, string expected)
    {
        Assert.Equal(expected, AssetPath.Resolve(dir, asset));
    }

    [Fact]
    public void AssetPath_PutsBackgroundsInTheBackgroundsFolder()
    {
        Assert.Equal(@"Backgrounds\BG_Grove.jpg", AssetPath.Background("BG_Grove.jpg"));
    }

    [Theory]
    [InlineData(@"Grove\Snow1.png", "Grove")]
    [InlineData(@"Backgrounds\BG_Grove.jpg", "Backgrounds")]
    [InlineData("loose.png", "")]
    public void AssetPath_FolderOfNamesTheContainingFolder(string relativePath, string expected)
    {
        Assert.Equal(expected, AssetPath.FolderOf(relativePath));
    }
}
