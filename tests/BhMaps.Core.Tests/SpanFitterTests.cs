using System.Windows;
using System.Windows.Media.Imaging;
using BhMaps.Core.Imaging;
using BhMaps.Core.LevelData;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public sealed class SpanFitterTests
{
    private const string AssetDir = "Map";
    private const string PieceName = "a.png";

    [Fact]
    public void Two_pieces_side_by_side_continue_the_picture()
    {
        using var dir = new TempDir();
        var level = Level(Node(0, 0, Asset(0, 0, 200, 200), Asset(200, 0, 200, 200)));
        var picture = Picture(dir, 400, 200, (x, _) => ((byte)Math.Min(x, 255), (byte)0, (byte)0, (byte)255));
        var piece = Opaque(dir, 100, 100);
        var box = SpanFitter.Box(level);
        var placements = SpanFitter.Placements(level, Path(), piece.PixelWidth, piece.PixelHeight);

        Assert.NotNull(box);
        Assert.Equal(2, placements.Count);

        var left = SpanFitter.Cut(picture, box!.Value, new FitOptions(), placements[0], piece);
        var right = SpanFitter.Cut(picture, box.Value, new FitOptions(), placements[1], piece);

        // The left piece ends where the right piece starts, so the gradient runs on across the seam.
        Assert.Equal(199d, Red(left, 99, 50), 4d);
        Assert.Equal(201d, Red(right, 0, 50), 4d);

        // And the picture is not repeated inside the left piece: the red climbs from one end to the other.
        var previous = -1d;
        foreach (var x in new[] { 0, 20, 40, 60, 80, 99 })
        {
            var red = Red(left, x, 50);
            Assert.True(red > previous, $"red at {x} was {red}, which is not above {previous}");
            previous = red;
        }

        Assert.Equal(0d, Red(left, 0, 50), 4d);
    }

    [Fact]
    public void Flipped_piece_comes_back_mirrored()
    {
        using var dir = new TempDir();
        var level = Level(Node(200, 0, Asset(0, 0, 200, 200)) with { ScaleX = -1 });
        var picture = Picture(dir, 200, 200, (x, _) => ((byte)x, (byte)0, (byte)0, (byte)255));
        var piece = Opaque(dir, 100, 100);
        var box = SpanFitter.Box(level);
        var placement = SpanFitter.Largest(SpanFitter.Placements(level, Path(), 100, 100));

        Assert.NotNull(box);
        Assert.NotNull(placement);

        var cut = SpanFitter.Cut(picture, box!.Value, new FitOptions(), placement!, piece);

        // The node mirrors the piece, so the cut carries the picture back mirrored: red falls left to right.
        var previous = 256d;
        foreach (var x in new[] { 10, 30, 50, 70, 90 })
        {
            var red = Red(cut, x, 50);
            Assert.True(red < previous, $"red at {x} was {red}, which is not below {previous}");
            previous = red;
        }
    }

    [Fact]
    public void Rotated_piece_comes_back_rotated()
    {
        using var dir = new TempDir();
        var level = Level(Node(200, 0, Asset(0, 0, 200, 200)) with { Rotation = 90 });
        var picture = Picture(dir, 200, 200, (_, y) => ((byte)0, (byte)y, (byte)0, (byte)255));
        var piece = Opaque(dir, 100, 100);
        var box = SpanFitter.Box(level);
        var placement = SpanFitter.Largest(SpanFitter.Placements(level, Path(), 100, 100));

        Assert.NotNull(box);
        Assert.NotNull(placement);

        var cut = SpanFitter.Cut(picture, box!.Value, new FitOptions(), placement!, piece);

        // The quarter turn puts the picture's y axis along the piece's x axis, so the gradient reads across.
        Assert.True(Green(cut, 80, 50) - Green(cut, 20, 50) > 100, "the cut does not vary along its x axis");
        Assert.Equal(Green(cut, 50, 20), Green(cut, 50, 80), 4d);
    }

    [Fact]
    public void Piece_drawn_twice_lists_two_placements_and_largest_wins()
    {
        var level = Level(
            Node(0, 0, Asset(0, 0, 100, 100)),
            Node(500, 0, Asset(0, 0, 100, 100)) with { Scale = 2 });

        var placements = SpanFitter.Placements(level, Path(), 100, 100);
        var largest = SpanFitter.Largest(placements);

        Assert.Equal(2, placements.Count);
        Assert.NotNull(largest);
        Assert.Equal(40000, largest!.Area, 3);
        Assert.Equal(new Rect(500, 0, 200, 200), largest.Bounds);
    }

    [Fact]
    public void Unplaced_piece_has_no_placements()
    {
        var level = Level(Node(0, 0, Asset(0, 0, 100, 100)));

        Assert.Empty(SpanFitter.Placements(level, AssetPath.Resolve(AssetDir, "missing.png"), 10, 10));
        Assert.NotNull(SpanFitter.Box(level));
        Assert.Null(SpanFitter.Largest([]));
    }

    [Fact]
    public void Alpha_of_the_piece_masks_the_cut()
    {
        using var dir = new TempDir();
        var level = Level(Node(0, 0, Asset(0, 0, 200, 200)));
        var picture = Picture(dir, 200, 200, (_, _) => ((byte)255, (byte)255, (byte)255, (byte)255));
        var piece = Picture(dir, 100, 100, (x, _) => ((byte)0, (byte)0, (byte)0, x < 50 ? (byte)0 : (byte)255));
        var placement = SpanFitter.Largest(SpanFitter.Placements(level, Path(), 100, 100));

        Assert.NotNull(placement);

        var cut = SpanFitter.Cut(picture, SpanFitter.Box(level)!.Value, new FitOptions(), placement!, piece);

        Assert.Equal(0, SyntheticImage.PixelRgbaAt(cut, 10, 50).A);
        Assert.Equal(255, SyntheticImage.PixelRgbaAt(cut, 90, 50).A);
    }

    [Fact]
    public void Box_is_the_plain_union_without_padding()
    {
        var level = Level(Node(0, 0, Asset(10, 20, 100, 50), Asset(200, 20, 100, 50)));

        Assert.Equal(new Rect(10, 20, 290, 50), SpanFitter.Box(level)!.Value);
    }

    [Fact]
    public void Fitted_is_the_box_at_the_asked_for_scale()
    {
        using var dir = new TempDir();
        var level = Level(Node(0, 0, Asset(0, 0, 400, 200)));
        var picture = Picture(dir, 400, 200, (x, _) => ((byte)Math.Min(x, 255), (byte)0, (byte)0, (byte)255));

        var fitted = SpanFitter.Fitted(picture, SpanFitter.Box(level)!.Value, new FitOptions(), 0.5);

        Assert.Equal(200, fitted.PixelWidth);
        Assert.Equal(100, fitted.PixelHeight);
        Assert.True(fitted.IsFrozen);
        Assert.Equal(0d, Red(fitted, 0, 50), 4d);
        Assert.Equal(255d, Red(fitted, 199, 50), 4d);
    }

    private static string Path() => AssetPath.Resolve(AssetDir, PieceName);

    // 3.2 F1: two 200-unit pieces side by side make a 400 x 200 stage and a 200 x 200 red picture. Fit and Center
    // both put it at x 100 to 300, so the outer half of each piece is bare and keeps the piece's own green; Fill
    // and Stretch cover the whole stage.
    [Theory]
    [InlineData(FitMode.Cover, false)]
    [InlineData(FitMode.Stretch, false)]
    [InlineData(FitMode.Contain, true)]
    [InlineData(FitMode.Center, true)]
    public void Every_mode_keeps_the_shape_and_the_original_where_uncovered(FitMode mode, bool leavesSides)
    {
        using var dir = new TempDir();
        var level = Level(Node(0, 0, Asset(0, 0, 200, 200), Asset(200, 0, 200, 200)));
        var picture = Picture(dir, 200, 200, (_, _) => ((byte)255, (byte)0, (byte)0, (byte)255));
        var piece = Picture(dir, 100, 100, (_, y) => ((byte)0, (byte)255, (byte)0, y < 10 ? (byte)0 : (byte)255));
        var placements = SpanFitter.Placements(level, Path(), 100, 100);
        var box = SpanFitter.Box(level)!.Value;

        var left = SpanFitter.Cut(picture, box, new FitOptions(mode), placements[0], piece);
        var right = SpanFitter.Cut(picture, box, new FitOptions(mode), placements[1], piece);

        foreach (var cut in new[] { left, right })
        {
            Assert.Equal(100, cut.PixelWidth);
            Assert.Equal(100, cut.PixelHeight);
            PieceFitterTests.AssertShapeFilled(cut, piece);
        }

        Assert.True(Red(left, 75, 50) > 200, "the picture covers the inner half of the left piece");
        Assert.True(Red(right, 25, 50) > 200, "the picture covers the inner half of the right piece");
        if (leavesSides)
        {
            Assert.True(Green(left, 25, 50) > 200 && Red(left, 25, 50) < 60, "the bare half keeps the original");
            Assert.True(Green(right, 75, 50) > 200 && Red(right, 75, 50) < 60, "the bare half keeps the original");
        }
        else
        {
            Assert.True(Red(left, 25, 50) > 200, "Fill and Stretch cover the whole piece");
            Assert.True(Red(right, 75, 50) > 200, "Fill and Stretch cover the whole piece");
        }
    }

    [Fact]
    public void Uncovered_part_is_the_original_passed_in()
    {
        using var dir = new TempDir();
        var level = Level(Node(0, 0, Asset(0, 0, 200, 200), Asset(200, 0, 200, 200)));
        var picture = Picture(dir, 200, 200, (_, _) => ((byte)255, (byte)0, (byte)0, (byte)255));
        var piece = Opaque(dir, 100, 100);
        var original = Picture(dir, 100, 100, (_, _) => ((byte)0, (byte)0, (byte)255, (byte)255));
        var placements = SpanFitter.Placements(level, Path(), 100, 100);

        var left = SpanFitter.Cut(picture, SpanFitter.Box(level)!.Value, new FitOptions(FitMode.Center), placements[0], piece, original);

        Assert.True(SyntheticImage.PixelRgbaAt(left, 25, 50).B > 200, "the bare half shows the original's blue");
        Assert.True(Red(left, 75, 50) > 200, "the covered half is the picture");
    }

    private static double Red(BitmapSource bitmap, int x, int y) => SyntheticImage.PixelRgbaAt(bitmap, x, y).R;

    private static double Green(BitmapSource bitmap, int x, int y) => SyntheticImage.PixelRgbaAt(bitmap, x, y).G;

    private static BitmapSource Picture(
        TempDir dir, int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel) =>
        BackgroundFitter.LoadSource(
            SyntheticImage.SavePng(dir.Sub(Guid.NewGuid().ToString("N") + ".png"), width, height, pixel));

    private static BitmapSource Opaque(TempDir dir, int width, int height) =>
        Picture(dir, width, height, (_, _) => (0, 0, 0, 255));

    private static LevelDesc Level(params PlatformNode[] platforms) =>
        new("Test", AssetDir, new CameraBounds(0, 0, 4000, 2000), [], platforms);

    private static PlatformNode Node(double x, double y, params LevelAsset[] assets) =>
        new(x, y, 1, 1, 1, 0, null, assets, []);

    private static LevelAsset Asset(double x, double y, double w, double h) => new(PieceName, x, y, w, h);
}
