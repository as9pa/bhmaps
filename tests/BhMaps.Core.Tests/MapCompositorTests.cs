using System.Windows.Media;
using BhMaps.Core.Imaging;
using BhMaps.Core.LevelData;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class MapCompositorTests
{
    private static LevelDesc Level(CameraBounds camera, string? background, params PlatformNode[] platforms) =>
        new("Test", "Grove", camera,
            background is null ? [] : [new LevelBackground(background, null, null)],
            platforms);

    private static PlatformNode Node(double x, double y, string? theme, params LevelAsset[] assets) =>
        new(x, y, 1, 1, 1, 0, theme, assets, []);

    private static string Solid(TempDir tmp, string rel, byte r, byte g, byte b, int size = 16) =>
        SyntheticImage.SavePng(Path.Combine(tmp.Path, rel), size, size, (_, _) => (r, g, b, 255));

    private static void AssertColour((byte R, byte G, byte B) actual, byte r, byte g, byte b) =>
        Assert.True(Math.Abs(actual.R - r) < 40 && Math.Abs(actual.G - g) < 40 && Math.Abs(actual.B - b) < 40,
            $"expected about ({r},{g},{b}), got {actual}");

    private static void AssertTile((byte R, byte G, byte B) actual) =>
        AssertColour(actual, MapCompositor.TileColour.R, MapCompositor.TileColour.G, MapCompositor.TileColour.B);

    [Fact]
    public void Render_FillsWithTheTileColourOutsideTheCameraRectangle()
    {
        using var tmp = new TempDir();
        var level = Level(new CameraBounds(0, 0, 100, 100), null);

        var bitmap = MapCompositor.Render(level, 200, 100, new AssetSources(tmp.Path));

        AssertTile(SyntheticImage.PixelAt(bitmap, 2, 50));
    }

    [Fact]
    public void Render_StretchesTheBackgroundOverTheCameraBounds()
    {
        using var tmp = new TempDir();
        Solid(tmp, @"Backgrounds\BG_Grove.jpg", 0, 0, 255);
        var level = Level(new CameraBounds(0, 0, 100, 50), "BG_Grove.jpg");

        var bitmap = MapCompositor.Render(level, 200, 100, new AssetSources(tmp.Path));

        AssertColour(SyntheticImage.PixelAt(bitmap, 100, 50), 0, 0, 255);
    }

    [Fact]
    public void Render_DrawsAnAssetAtItsCameraSpaceRectangle()
    {
        using var tmp = new TempDir();
        Solid(tmp, @"Grove\plat.png", 255, 0, 0);
        var level = Level(new CameraBounds(0, 0, 100, 100), null,
            Node(0, 0, null, new LevelAsset("plat.png", 0, 0, 50, 50)));

        var bitmap = MapCompositor.Render(level, 100, 100, new AssetSources(tmp.Path));

        AssertColour(SyntheticImage.PixelAt(bitmap, 25, 25), 255, 0, 0);
        AssertTile(SyntheticImage.PixelAt(bitmap, 75, 75));
    }

    [Fact]
    public void Render_DrawsAnAssetOwnedByThePlatformAtTheNodeOrigin()
    {
        using var tmp = new TempDir();
        Solid(tmp, @"Grove\plat.png", 255, 0, 0);

        // Built from markup: the shape under test is an AssetName on the Platform element itself.
        var level = LevelDescParser.Parse(LevelXml.Level("Grove", "Grove", LevelXml.Camera(0, 0, 100, 100)
            + "<Platform InstanceName=\"am_Midground1\" AssetName=\"plat.png\" X=\"20\" Y=\"20\" W=\"50\" H=\"50\" />"));

        var bitmap = MapCompositor.Render(level, 100, 100, new AssetSources(tmp.Path));

        AssertColour(SyntheticImage.PixelAt(bitmap, 45, 45), 255, 0, 0);
        AssertTile(SyntheticImage.PixelAt(bitmap, 15, 15));
        AssertTile(SyntheticImage.PixelAt(bitmap, 75, 75));
    }

    [Fact]
    public void Render_SkipsAThemedPlatformThatCarriesItsOwnAsset()
    {
        using var tmp = new TempDir();
        Solid(tmp, @"Grove\plat.png", 255, 0, 0);
        var level = LevelDescParser.Parse(LevelXml.Level("Grove", "Grove", LevelXml.Camera(0, 0, 100, 100)
            + "<Platform Theme=\"Halloween\" AssetName=\"plat.png\" X=\"0\" Y=\"0\" W=\"100\" H=\"100\" />"));

        var bitmap = MapCompositor.Render(level, 100, 100, new AssetSources(tmp.Path));

        AssertTile(SyntheticImage.PixelAt(bitmap, 50, 50));
    }

    [Fact]
    public void Render_MirrorsAnAssetInPlaceForANegativeWidth()
    {
        using var tmp = new TempDir();
        SyntheticImage.SavePng(Path.Combine(tmp.Path, @"Grove\half.png"), 32, 32,
            (x, _) => x < 16 ? SyntheticImage.Rgb(255, 0, 0) : SyntheticImage.Rgb(0, 255, 0));
        var sources = new AssetSources(tmp.Path);
        var camera = new CameraBounds(0, 0, 100, 100);

        var upright = MapCompositor.Render(
            Level(camera, null, Node(0, 0, null, new LevelAsset("half.png", 0, 0, 100, 100))), 100, 100, sources);
        var mirrored = MapCompositor.Render(
            Level(camera, null, Node(0, 0, null, new LevelAsset("half.png", 0, 0, -100, 100))), 100, 100, sources);

        AssertColour(SyntheticImage.PixelAt(upright, 10, 50), 255, 0, 0);
        AssertColour(SyntheticImage.PixelAt(mirrored, 10, 50), 0, 255, 0);
        AssertColour(SyntheticImage.PixelAt(mirrored, 90, 50), 255, 0, 0);
    }

    [Fact]
    public void Render_AppliesNestedScaleAndTranslationFromTheTransformStack()
    {
        using var tmp = new TempDir();
        Solid(tmp, @"Grove\plat.png", 255, 0, 0);
        var inner = Node(0, 0, null, new LevelAsset("plat.png", 0, 0, 10, 10));
        var outer = new PlatformNode(10, 10, 2, 1, 1, 0, null, [], [inner]);
        var level = Level(new CameraBounds(0, 0, 100, 100), null, outer);

        var bitmap = MapCompositor.Render(level, 100, 100, new AssetSources(tmp.Path));

        AssertColour(SyntheticImage.PixelAt(bitmap, 20, 20), 255, 0, 0);
        AssertTile(SyntheticImage.PixelAt(bitmap, 5, 5));
        AssertTile(SyntheticImage.PixelAt(bitmap, 40, 40));
    }

    [Fact]
    public void Render_RotatesAnAssetAroundTheNodeOrigin()
    {
        using var tmp = new TempDir();
        Solid(tmp, @"Grove\bar.png", 255, 0, 0);
        var node = new PlatformNode(50, 10, 1, 1, 1, 90, null, [new LevelAsset("bar.png", 0, 0, 40, 10)], []);
        var level = Level(new CameraBounds(0, 0, 100, 100), null, node);

        var bitmap = MapCompositor.Render(level, 100, 100, new AssetSources(tmp.Path));

        AssertColour(SyntheticImage.PixelAt(bitmap, 45, 30), 255, 0, 0);
        AssertTile(SyntheticImage.PixelAt(bitmap, 80, 15));
    }

    [Fact]
    public void Render_SkipsThemedPlatformsAndTheirChildren()
    {
        using var tmp = new TempDir();
        Solid(tmp, @"Grove\plat.png", 255, 0, 0);
        var child = Node(0, 0, null, new LevelAsset("plat.png", 0, 0, 100, 100));
        var themed = new PlatformNode(0, 0, 1, 1, 1, 0, "Winter", [], [child]);
        var level = Level(new CameraBounds(0, 0, 100, 100), null, themed);

        var bitmap = MapCompositor.Render(level, 100, 100, new AssetSources(tmp.Path));

        AssertTile(SyntheticImage.PixelAt(bitmap, 50, 50));
    }

    [Fact]
    public void Render_ResolvesParentFolderAssetReferences()
    {
        using var tmp = new TempDir();
        Solid(tmp, @"Snow\Snow1.png", 0, 255, 0);
        var level = Level(new CameraBounds(0, 0, 100, 100), null,
            Node(0, 0, null, new LevelAsset("../Snow/Snow1.png", 0, 0, 100, 100)));

        var bitmap = MapCompositor.Render(level, 100, 100, new AssetSources(tmp.Path));

        AssertColour(SyntheticImage.PixelAt(bitmap, 50, 50), 0, 255, 0);
    }

    [Fact]
    public void ResolveAsset_PrefersThePackButFallsBackWhenThePackFileIsFullyTransparent()
    {
        using var game = new TempDir();
        using var pack = new TempDir();
        var gameA = Solid(game, @"Grove\a.png", 255, 0, 0);
        Solid(game, @"Grove\b.png", 255, 0, 0);
        SyntheticImage.SavePng(Path.Combine(pack.Path, @"Grove\a.png"), 16, 16, (_, _) => (0, 0, 0, 0));
        var packB = Solid(pack, @"Grove\b.png", 0, 255, 0);
        var sources = new AssetSources(game.Path, pack.Path);

        Assert.Equal(gameA, sources.ResolveAsset(@"Grove\a.png"));
        Assert.Equal(packB, sources.ResolveAsset(@"Grove\b.png"));
        Assert.Null(sources.ResolveAsset(@"Grove\missing.png"));
    }

    [Fact]
    public void Render_SkipsMissingAssetsInsteadOfThrowing()
    {
        using var tmp = new TempDir();
        var level = Level(new CameraBounds(0, 0, 100, 100), "BG_Gone.jpg",
            Node(0, 0, null, new LevelAsset("gone.png", 0, 0, 100, 100)));

        var bitmap = MapCompositor.Render(level, 100, 100, new AssetSources(tmp.Path));

        Assert.Equal(100, bitmap.PixelWidth);
        Assert.Equal(100, bitmap.PixelHeight);
        Assert.True(bitmap.IsFrozen);
        Assert.Equal(PixelFormats.Bgr24, bitmap.Format);
    }

    [Fact]
    public void CollectInputs_ListsTheBackgroundFirstThenEveryExistingAssetInDrawOrder()
    {
        using var tmp = new TempDir();
        var background = Solid(tmp, @"Backgrounds\BG_Grove.jpg", 0, 0, 255);
        var a = Solid(tmp, @"Grove\a.png", 255, 0, 0);
        var snow = Solid(tmp, @"Snow\Snow1.png", 0, 255, 0);
        var level = Level(new CameraBounds(0, 0, 100, 100), "BG_Grove.jpg",
            Node(0, 0, null,
                new LevelAsset("a.png", 0, 0, 10, 10),
                new LevelAsset("../Snow/Snow1.png", 0, 0, 10, 10),
                new LevelAsset("gone.png", 0, 0, 10, 10)));

        var inputs = MapCompositor.CollectInputs(level, new AssetSources(tmp.Path));

        Assert.Equal(new[] { background, a, snow }, inputs);
    }

    [Fact]
    public void CollectInputs_ListsNothingForALevelWithNoCameraBounds()
    {
        using var tmp = new TempDir();
        Solid(tmp, @"Backgrounds\BG_Grove.jpg", 0, 0, 255);
        Solid(tmp, @"Grove\a.png", 255, 0, 0);
        var level = Level(new CameraBounds(0, 0, 0, 0), "BG_Grove.jpg",
            Node(0, 0, null, new LevelAsset("a.png", 0, 0, 10, 10)));

        Assert.Empty(MapCompositor.CollectInputs(level, new AssetSources(tmp.Path)));
    }

    [Fact]
    public void Render_DrawsAnAssetOutsideTheFocusSetAtGhostOpacity()
    {
        using var tmp = new TempDir();
        Solid(tmp, @"Backgrounds\BG_Grove.jpg", 0, 0, 0);
        Solid(tmp, @"Grove\a.png", 255, 255, 255);
        Solid(tmp, @"Grove\b.png", 255, 255, 255);
        var level = Level(
            new CameraBounds(0, 0, 100, 100),
            "BG_Grove.jpg",
            Node(0, 0, null, new LevelAsset("a.png", 0, 0, 40, 100)),
            Node(0, 0, null, new LevelAsset("b.png", 60, 0, 40, 100)));
        var focus = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AssetPath.Resolve("Grove", "a.png"),
        };

        var bitmap = MapCompositor.Render(level, 100, 100, new AssetSources(tmp.Path), null, focus);

        var focused = SyntheticImage.PixelAt(bitmap, 20, 50);
        var ghosted = SyntheticImage.PixelAt(bitmap, 80, 50);
        Assert.True(focused.R > 240, $"the focused piece was dimmed: {focused}");
        Assert.InRange(ghosted.R, 25, 52);
    }
}
