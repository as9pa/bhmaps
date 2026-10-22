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
    public void Parse_ReadsAnAssetCarriedOnThePlatformElementItself()
    {
        var xml = LevelXml.Level("Enigma", "Enigma", LevelXml.Camera(0, 0, 10, 10)
            + "<Platform InstanceName=\"am_Midground1\" AssetName=\"Platform_Steam1A.png\" H=\"1227.92\" W=\"1044.03\" />"
            + "<Platform InstanceName=\"am_Midground2\" X=\"5\">"
            + "<Asset AssetName=\"a.png\" X=\"1\" Y=\"2\" W=\"3\" H=\"4\" /></Platform>"
            + "<Platform InstanceName=\"am_Midground3\" X=\"9\" />");

        var level = LevelDescParser.Parse(xml);

        var owned = Assert.Single(level.Platforms[0].Assets);
        Assert.Equal("Platform_Steam1A.png", owned.AssetName);
        Assert.Equal(0, owned.X);
        Assert.Equal(0, owned.Y);
        Assert.Equal(1044.03, owned.W);
        Assert.Equal(1227.92, owned.H);

        // A node that names no asset of its own owns nothing: its assets are its Asset children, and no more.
        var child = Assert.Single(level.Platforms[1].Assets);
        Assert.Equal("a.png", child.AssetName);
        Assert.Equal(1, child.X);
        Assert.Empty(level.Platforms[2].Assets);
    }

    [Fact]
    public void Parse_PutsAPlatformsOwnAssetBeforeTheOnesNestedInsideIt()
    {
        var xml = LevelXml.Level("Enigma", "Enigma", LevelXml.Camera(0, 0, 10, 10)
            + "<Platform AssetName=\"own.png\" W=\"10\" H=\"20\">"
            + "<Asset AssetName=\"child.png\" X=\"0\" Y=\"0\" W=\"1\" H=\"1\" /></Platform>");

        var node = Assert.Single(LevelDescParser.Parse(xml).Platforms);

        Assert.Equal(["own.png", "child.png"], node.Assets.Select(a => a.AssetName));
    }

    [Fact]
    public void Parse_KeepsTheThemeOfAPlatformThatCarriesItsOwnAsset()
    {
        var xml = LevelXml.Level("Enigma", "Enigma", LevelXml.Camera(0, 0, 10, 10)
            + "<Platform InstanceName=\"am_Holiday\" Theme=\"Halloween,TWDHalloween\""
            + " AssetName=\"../Halloween/HalloweenJackoLanternPumpkinTall.png\" H=\"246.5\" W=\"132.25\" />");

        var node = Assert.Single(LevelDescParser.Parse(xml).Platforms);

        Assert.Equal("Halloween,TWDHalloween", node.Theme);
        Assert.True(node.IsThemed);
        Assert.Equal(
            "../Halloween/HalloweenJackoLanternPumpkinTall.png",
            Assert.Single(node.Assets).AssetName);
    }

    /// <summary>The game names SWF animation symbols the same way it names images, and those carry no size and
    /// no file. Only a sized name is map art.</summary>
    [Fact]
    public void Parse_IgnoresAnUnsizedAssetNameBecauseItNamesAnAnimationSymbolNotAnImage()
    {
        var xml = LevelXml.Level("Climb", "Climb", LevelXml.Camera(0, 0, 10, 10)
            + "<Platform AssetName=\"a_LevelAnim_Flag\" X=\"3\" Y=\"4\" />"
            + "<Platform AssetName=\"flat.png\" X=\"3\" W=\"10\" H=\"0\" />"
            + "<PressurePlateCollision AssetName=\"a__AnimationPressurePlate\" X1=\"3\" X2=\"5\" Y=\"4\" />");

        var level = LevelDescParser.Parse(xml);

        Assert.Equal(2, level.Platforms.Count);
        Assert.Empty(level.Platforms[0].Assets);
        Assert.Equal(3, level.Platforms[0].X);
        Assert.Empty(level.Platforms[1].Assets);
        Assert.Equal(CollisionKind.PressurePlate, Assert.Single(level.Collisions).Kind);
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

    [Fact]
    public void Parse_NormalizesTheThreeSegmentFormsToTwoEnds()
    {
        var xml = LevelXml.Level("Grove", "Grove", LevelXml.Camera(0, 0, 10, 10)
            + "<HardCollision X1=\"-100\" X2=\"250.5\" Y=\"40\" />"
            + "<NoSlideCollision X=\"-100\" Y1=\"40\" Y2=\"300\" />"
            + "<BouncyHardCollision X1=\"0\" X2=\"30\" Y1=\"10\" Y2=\"90\" />"
            + "<HardCollision AnchorX=\"20\" AnchorY=\"5\" X1=\"0\" X2=\"40\" Y1=\"10\" Y2=\"30\" />");

        var level = LevelDescParser.Parse(xml);

        Assert.Equal(
            [
                new CollisionLine(CollisionKind.Hard, -100, 40, 250.5, 40),
                new CollisionLine(CollisionKind.NoSlide, -100, 40, -100, 300),
                new CollisionLine(CollisionKind.BouncyHard, 0, 10, 30, 90),
                new CollisionLine(CollisionKind.Hard, 0, 10, 40, 30, null, 20, 5),
            ],
            level.Collisions);
    }

    [Fact]
    public void Parse_OffsetsTheLinesOfADynamicCollisionByItsPositionAndKeepsItsPlatId()
    {
        var xml = LevelXml.Level("Grove", "Grove", LevelXml.Camera(0, 0, 10, 10)
            + "<MovingPlatform PlatID=\"1\" X=\"400\" Y=\"-50\"><Platform AssetName=\"a.png\" W=\"10\" H=\"10\" />"
            + "</MovingPlatform>"
            + "<DynamicCollision PlatID=\"1\" X=\"500\" Y=\"-100\">"
            + "<SoftCollision X1=\"10\" X2=\"310\" Y=\"0\" />"
            + "<HardCollision X=\"0\" Y1=\"0\" Y2=\"90\" />"
            + "<PressurePlateCollision AssetName=\"a__AnimationPressurePlate\" PlatID=\"1\" X1=\"-5\" X2=\"5\" Y1=\"1\" Y2=\"2\""
            + " AnchorX=\"3\" AnchorY=\"4\" />"
            + "</DynamicCollision>");

        var level = LevelDescParser.Parse(xml);

        Assert.Equal(
            [
                new CollisionLine(CollisionKind.Soft, 510, -100, 810, -100, "1"),
                new CollisionLine(CollisionKind.Hard, 500, -100, 500, -10, "1"),
                new CollisionLine(CollisionKind.PressurePlate, 495, -99, 505, -98, "1", 503, -96),
            ],
            level.Collisions);
    }

    [Fact]
    public void Parse_KeepsCollisionOutOfThePlatformTreeAndLeavesPlatformsAlone()
    {
        var xml = LevelXml.Level("Grove", "Grove", LevelXml.Camera(0, 0, 10, 10)
            + "<HardCollision X1=\"0\" X2=\"10\" Y=\"0\" />"
            + "<Platform X=\"7\"><Asset AssetName=\"a.png\" W=\"1\" H=\"1\" /></Platform>"
            + "<DynamicCollision PlatID=\"1\" X=\"5\" Y=\"5\"><SoftCollision X1=\"0\" X2=\"1\" Y=\"0\" /></DynamicCollision>"
            + "<MovingPlatform PlatID=\"1\" X=\"3\"><Platform AssetName=\"b.png\" W=\"2\" H=\"2\" /></MovingPlatform>");

        var level = LevelDescParser.Parse(xml);

        Assert.Equal(2, level.Platforms.Count);
        Assert.Equal(7, level.Platforms[0].X);
        Assert.Equal("a.png", Assert.Single(level.Platforms[0].Assets).AssetName);
        Assert.True(level.Platforms[1].Moving);
        Assert.Equal(3, level.Platforms[1].X);
        Assert.Equal(2, level.Collisions.Count);
    }

    [Fact]
    public void Parse_CountsTheLinesOfABrawlhavenShapedLevel()
    {
        // Brawlhaven has 25 HardCollision lines and one SoftCollision. Synthetic coordinates in all three forms.
        var hard = string.Concat(Enumerable.Range(0, 25).Select(i => (i % 3) switch
        {
            0 => $"<HardCollision X1=\"{i}\" X2=\"{i + 100}\" Y=\"{i * 10}\" />",
            1 => $"<HardCollision X=\"{i}\" Y1=\"{i * 10}\" Y2=\"{(i * 10) + 50}\" />",
            _ => $"<HardCollision X1=\"{i}\" X2=\"{i + 40}\" Y1=\"{i * 10}\" Y2=\"{(i * 10) + 40}\" />",
        }));
        var xml = LevelXml.Level("Brawlhaven", "Brawlhaven", LevelXml.Camera(0, 0, 10, 10)
            + "<Platform X=\"1\"><Asset AssetName=\"a.png\" W=\"1\" H=\"1\" /></Platform>"
            + hard
            + "<SoftCollision X1=\"390\" X2=\"1080\" Y=\"2180\" />");

        var level = LevelDescParser.Parse(xml);

        Assert.Equal(26, level.Collisions.Count);
        Assert.Equal(25, level.Collisions.Count(c => c.Kind == CollisionKind.Hard));
        Assert.Equal(1, level.Collisions.Count(c => c.Kind == CollisionKind.Soft));
        Assert.All(level.Collisions, c => Assert.Null(c.PlatId));
        Assert.Single(level.Platforms);
    }

    [Theory]
    [InlineData("Hard", CollisionKind.Hard)]
    [InlineData("Soft", CollisionKind.Soft)]
    [InlineData("NoSlide", CollisionKind.NoSlide)]
    [InlineData("BouncyHard", CollisionKind.BouncyHard)]
    [InlineData("BouncySoft", CollisionKind.BouncySoft)]
    [InlineData("BouncyNoSlide", CollisionKind.BouncyNoSlide)]
    [InlineData("Lava", CollisionKind.Lava)]
    [InlineData("PressurePlate", CollisionKind.PressurePlate)]
    [InlineData("SoftPressurePlate", CollisionKind.SoftPressurePlate)]
    [InlineData("GameModeHard", CollisionKind.GameModeHard)]
    [InlineData("Mud", CollisionKind.Mud)]
    public void Parse_NamesEveryCollisionKindTheGameUses(string prefix, CollisionKind expected)
    {
        var xml = LevelXml.Level("Grove", "Grove", $"<{prefix}Collision X1=\"0\" X2=\"1\" Y=\"0\" />");

        Assert.Equal(expected, Assert.Single(LevelDescParser.Parse(xml).Collisions).Kind);
    }

    [Fact]
    public void Parse_SkipsACollisionKindItDoesNotKnow()
    {
        var xml = LevelXml.Level("Grove", "Grove", "<WarpCollision X1=\"0\" X2=\"1\" Y=\"0\" />");

        var level = LevelDescParser.Parse(xml);

        Assert.Empty(level.Collisions);
        Assert.Empty(level.Platforms);
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
