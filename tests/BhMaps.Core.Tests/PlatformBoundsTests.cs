using BhMaps.Core.Imaging;
using BhMaps.Core.LevelData;

namespace BhMaps.Core.Tests;

public class PlatformBoundsTests
{
    private const double Aspect = 16d / 9d;

    [Fact]
    public void For_BoxesThePlatformsPadsThemAndKeepsTheAspect()
    {
        // One 200 x 100 platform in the middle of a 2000 x 1000 level.
        var level = Level(new CameraBounds(0, 0, 2000, 1000), Node(900, 450, Asset(0, 0, 200, 100)));

        var bounds = PlatformBounds.For(level, 0.12, Aspect);

        Assert.NotNull(bounds);

        // 200 wide plus 12 percent each side is 248; 100 tall plus 12 percent each side is 124. 248 / 124 is
        // exactly 2, which is wider than 16:9, so the height grows to 248 / (16/9) = 139.5.
        Assert.Equal(248, bounds!.W, 3);
        Assert.Equal(139.5, bounds.H, 3);

        // Centred on the platform, which sits at 900..1100 by 450..550, so the centre is 1000, 500.
        Assert.Equal(1000, bounds.X + (bounds.W / 2), 3);
        Assert.Equal(500, bounds.Y + (bounds.H / 2), 3);
    }

    [Fact]
    public void For_UnionsEveryPlatformIncludingNestedAndScaledOnes()
    {
        var level = Level(
            new CameraBounds(0, 0, 4000, 2000),
            Node(1000, 1000, Asset(0, 0, 100, 100)),
            Node(2000, 1000, scale: 2, child: Node(0, 0, Asset(0, 0, 100, 100))));

        var bounds = PlatformBounds.For(level, 0, Aspect);

        // 1000..1100 from the first, and 2000..2200 from the second (100 wide at scale 2).
        Assert.NotNull(bounds);
        Assert.True(bounds!.X <= 1000);
        Assert.True(bounds.X + bounds.W >= 2200);
    }

    [Fact]
    public void For_ClampsToTheCameraAndNeverLeavesIt()
    {
        // A platform hard against the left edge, with padding that would push the box off the level.
        var level = Level(new CameraBounds(0, 0, 1000, 600), Node(0, 300, Asset(0, 0, 40, 20)));

        var bounds = PlatformBounds.For(level, 0.12, Aspect);

        Assert.NotNull(bounds);
        Assert.True(bounds!.X >= 0);
        Assert.True(bounds.Y >= 0);
        Assert.True(bounds.X + bounds.W <= 1000.001);
        Assert.True(bounds.Y + bounds.H <= 600.001);
    }

    [Fact]
    public void For_SkipsSeasonalNodesAndReturnsNullWhenNothingIsLeft()
    {
        var level = Level(
            new CameraBounds(0, 0, 1000, 600),
            Node(100, 100, Asset(0, 0, 50, 50)) with { Theme = "Snow" });

        Assert.Null(PlatformBounds.For(level, 0.12, Aspect));
    }

    [Fact]
    public void For_ReturnsNullForALevelWithNoUsableCamera()
    {
        var level = Level(new CameraBounds(0, 0, 0, 0), Node(10, 10, Asset(0, 0, 50, 50)));

        Assert.Null(PlatformBounds.For(level, 0.12, Aspect));
    }

    [Fact]
    public void For_WithAFocusSetBoxesOnlyTheFocusedAssets()
    {
        var level = new LevelDesc(
            "Test", "Test", new CameraBounds(0, 0, 4000, 2000), [],
            [Named(1000, 1000, "a.png"), Named(3000, 1000, "b.png")]);
        var focus = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AssetPath.Resolve("Test", "a.png"),
        };

        var bounds = PlatformBounds.For(level, 0, Aspect, focus);

        // Only the first node is in the set, so the box is 1000..1100 rather than 1000..3100.
        Assert.NotNull(bounds);
        Assert.True(
            bounds!.X + bounds.W < 2000,
            $"the unfocused asset was boxed too: {bounds.X}..{bounds.X + bounds.W}");
        Assert.InRange(bounds.X + (bounds.W / 2), 1040, 1060);
    }

    [Fact]
    public void For_WithAFocusSetThatMatchesNothingFallsBackToTheFullCamera()
    {
        var level = new LevelDesc(
            "Test", "Test", new CameraBounds(0, 0, 4000, 2000), [], [Named(1000, 1000, "a.png")]);

        var bounds = PlatformBounds.For(level, 0, Aspect, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        // Null is the caller's signal to render the whole level, which is what nothing ticked has to do.
        Assert.Null(bounds);
    }

    private static PlatformNode Named(double x, double y, string assetName) =>
        new(x, y, 1, 1, 1, 0, null, [new LevelAsset(assetName, 0, 0, 100, 100)], []);

    private static LevelDesc Level(CameraBounds camera, params PlatformNode[] platforms) =>
        new("Test", "Test", camera, [], platforms);

    private static PlatformNode Node(double x, double y, LevelAsset asset) =>
        new(x, y, 1, 1, 1, 0, null, [asset], []);

    private static PlatformNode Node(double x, double y, double scale, PlatformNode child) =>
        new(x, y, scale, 1, 1, 0, null, [], [child]);

    private static LevelAsset Asset(double x, double y, double w, double h) => new("a.png", x, y, w, h);
}
