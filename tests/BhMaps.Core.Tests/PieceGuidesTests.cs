using System.Windows;
using BhMaps.Core.Imaging;
using BhMaps.Core.LevelData;

namespace BhMaps.Core.Tests;

public class PieceGuidesTests
{
    private static LevelDesc Level(params PlatformNode[] platforms) =>
        new("Test", "Grove", new CameraBounds(0, 0, 1600, 900), [], platforms);

    private static PlatformNode Node(double x, double y, double rotation, bool moving, params LevelAsset[] assets) =>
        new(x, y, 1, 1, 1, rotation, null, assets, [], moving);

    private static PlatformNode Still(params LevelAsset[] assets) => Node(0, 0, 0, false, assets);

    /// <summary>Every file is 100 by 50 pixels except the ones named missing.</summary>
    private static Func<string, (int Width, int Height)?> Sizes(params string[] missing) =>
        path => missing.Contains(path, StringComparer.OrdinalIgnoreCase) ? null : (100, 50);

    private static void AssertPoint(Point actual, double x, double y) =>
        Assert.True(Math.Abs(actual.X - x) < 1e-9 && Math.Abs(actual.Y - y) < 1e-9, $"expected ({x},{y}), got {actual}");

    private static double Area(IReadOnlyList<Point> polygon)
    {
        var sum = 0.0;
        for (var k = 0; k < polygon.Count; k++)
        {
            var a = polygon[k];
            var b = polygon[(k + 1) % polygon.Count];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }

        return Math.Abs(sum / 2);
    }

    [Fact]
    public void Build_TwoOverlappingRectanglesGiveTheirSharedRectangleAndWidth()
    {
        var level = Level(Still(new LevelAsset("a.png", 0, 0, 100, 50), new LevelAsset("b.png", 60, 10, 100, 50)));

        var guides = PieceGuides.Build(level, Sizes());

        Assert.Equal(2, guides.Pieces.Count);
        var overlap = Assert.Single(guides.Overlaps);
        Assert.Equal(@"Grove\a.png", overlap.Lower.RelativePath);
        Assert.Equal(@"Grove\b.png", overlap.Upper.RelativePath);
        Assert.Equal(40, overlap.Width, 9);
        Assert.Equal(40, overlap.Height, 9);
        Assert.Equal(4, overlap.Polygon.Count);
        Assert.Equal(1600, Area(overlap.Polygon), 9);
        Assert.All(overlap.Polygon, p => Assert.True(p.X is >= 60 - 1e-9 and <= 100 + 1e-9 && p.Y is >= 10 - 1e-9 and <= 50 + 1e-9));
    }

    [Fact]
    public void Outlines_FollowRotationAndFlip()
    {
        // Pixels 0..100 by 0..50 stretched to 20 by 10 and flipped on X, then the node turns 90 degrees and moves.
        var level = Level(Node(100, 50, 90, false, new LevelAsset("a.png", 0, 0, -20, 10)));

        var piece = Assert.Single(PieceGuides.Outlines(level, Sizes()));

        AssertPoint(piece.Corners[0], 100, 70);
        AssertPoint(piece.Corners[1], 100, 50);
        AssertPoint(piece.Corners[2], 90, 50);
        AssertPoint(piece.Corners[3], 90, 70);
    }

    [Fact]
    public void Overlaps_RotatedPieceClipsToADiamondInsideTheOther()
    {
        // A 100 by 100 square turned 45 degrees about its centre (50,50) over an axis-aligned one: the shared area
        // is the square with its four corners cut off, centred on the same point.
        var square = new LevelAsset("a.png", 0, 0, 100, 100);
        var turned = new PlatformNode(50, 50, 1, 1, 1, 45, null, [], [Still(new LevelAsset("b.png", -50, -50, 100, 100))]);
        var level = Level(Still(square), turned);

        var overlap = Assert.Single(PieceGuides.Build(level, Sizes()).Overlaps);

        Assert.Equal(8, overlap.Polygon.Count);
        Assert.Equal(100, overlap.Width, 9);
        Assert.Equal(100, overlap.Height, 9);
        var half = 50 * Math.Sqrt(2);
        var cut = (half - 50) * (half - 50);
        Assert.Equal(10000 - (4 * cut), Area(overlap.Polygon), 6);
    }

    [Fact]
    public void Overlaps_NoneForApartOrEdgeTouchingPieces()
    {
        var level = Level(Still(
            new LevelAsset("a.png", 0, 0, 100, 50),
            new LevelAsset("b.png", 100, 0, 100, 50),
            new LevelAsset("c.png", 400, 400, 100, 50)));

        Assert.Empty(PieceGuides.Build(level, Sizes()).Overlaps);
    }

    [Fact]
    public void Overlaps_PairAMovingPlatformAtRestWithStillArt()
    {
        var level = Level(
            Still(new LevelAsset("a.png", 0, 0, 100, 50)),
            Node(0, 0, 0, true, new LevelAsset("b.png", 10, 10, 100, 50)));

        var overlap = Assert.Single(PieceGuides.Build(level, Sizes()).Overlaps);
        Assert.Equal(90, overlap.Width, 3);
        Assert.Equal(40, overlap.Height, 3);
    }

    [Fact]
    public void Build_LeavesOutPiecesWhoseFileIsMissing()
    {
        var level = Level(Still(new LevelAsset("a.png", 0, 0, 100, 50), new LevelAsset("b.png", 10, 10, 100, 50)));

        var guides = PieceGuides.Build(level, Sizes(@"Grove\b.png"));

        var piece = Assert.Single(guides.Pieces);
        Assert.Equal(@"Grove\a.png", piece.RelativePath);
        Assert.Empty(guides.Overlaps);
    }

    [Fact]
    public void Build_SelectedOnlyKeepsTheirBoxesAndEveryOverlapTouchingThem()
    {
        var level = Level(Still(
            new LevelAsset("a.png", 0, 0, 100, 50),
            new LevelAsset("b.png", 50, 0, 100, 50),
            new LevelAsset("c.png", 120, 0, 100, 50)));
        var only = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"grove\A.png" };

        var all = PieceGuides.Build(level, Sizes());
        var selected = PieceGuides.Build(level, Sizes(), only);

        Assert.Equal(2, all.Overlaps.Count);
        var piece = Assert.Single(selected.Pieces);
        Assert.Equal(@"Grove\a.png", piece.RelativePath);
        var overlap = Assert.Single(selected.Overlaps);
        Assert.Equal(@"Grove\b.png", overlap.Upper.RelativePath);
        Assert.Equal(50, overlap.Width, 9);
    }

    [Fact]
    public void Build_PassesCollisionLinesThroughWhateverTheFilter()
    {
        CollisionLine[] lines =
        [
            new(CollisionKind.Hard, 0, 100, 500, 100),
            new(CollisionKind.Soft, 10, 20, 30, 40, "7", 15, 25),
        ];
        var level = Level(Still(new LevelAsset("a.png", 0, 0, 100, 50))) with { Collisions = lines };

        var guides = PieceGuides.Build(level, Sizes(), new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Empty(guides.Pieces);
        Assert.Equal(lines, guides.Collisions);
        Assert.Equal(15, guides.Collisions[1].AnchorX);
    }

    [Fact]
    public void StageTransform_MapsTheCameraCornersToThePanelCorners()
    {
        var stage = MapCompositor.StageTransform(
            new CameraBounds(-100, -50, 1600, 900), MapCompositor.PanelWidth, MapCompositor.PanelHeight);

        AssertPoint(stage.Transform(new Point(-100, -50)), 0, 0);
        AssertPoint(stage.Transform(new Point(1500, 850)), MapCompositor.PanelWidth, MapCompositor.PanelHeight);
    }

    [Fact]
    public void StageTransform_CentresAWideCameraBetweenEvenBands()
    {
        // 400 by 100 fits at scale 3.2, 320 high, leaving 200 above and below.
        var stage = MapCompositor.StageTransform(
            new CameraBounds(-100, -50, 400, 100), MapCompositor.PanelWidth, MapCompositor.PanelHeight);

        AssertPoint(stage.Transform(new Point(-100, -50)), 0, 200);
        AssertPoint(stage.Transform(new Point(300, 50)), 1280, 520);
    }
}
