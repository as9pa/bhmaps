using System.Windows.Media;
using System.Windows.Media.Imaging;
using BhMaps.Core.Imaging;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public sealed class PieceFitterTests
{
    private static void AssertRed((byte R, byte G, byte B, byte A) p) { Assert.True(p.R > 200 && p.G < 60 && p.B < 60, $"expected red, got {p}"); }
    private static void AssertGreen((byte R, byte G, byte B, byte A) p) { Assert.True(p.G > 200 && p.R < 60 && p.B < 60, $"expected green, got {p}"); }
    private static void AssertBlue((byte R, byte G, byte B, byte A) p) { Assert.True(p.B > 200 && p.R < 60 && p.G < 60, $"expected blue, got {p}"); }
    private static void AssertYellow((byte R, byte G, byte B, byte A) p) { Assert.True(p.R > 200 && p.G > 200 && p.B < 60, $"expected yellow, got {p}"); }

    [Fact]
    public void Fit_WiderSource_CoversAndCentres()
    {
        using var dir = new TempDir();
        var source = BackgroundFitter.LoadSource(SyntheticImage.SaveQuadrants(dir.Sub("src.png"), 800, 200));
        var piece = BackgroundFitter.LoadSource(SyntheticImage.SavePng(dir.Sub("piece.png"), 200, 100, (_, _) => (0, 0, 0, 255)));

        var fitted = PieceFitter.Fit(source, piece);

        Assert.Equal(200, fitted.PixelWidth);
        Assert.Equal(100, fitted.PixelHeight);
        Assert.Equal(PixelFormats.Bgra32, fitted.Format);
        Assert.True(fitted.IsFrozen);
        AssertRed(SyntheticImage.PixelRgbaAt(fitted, 10, 10));
        AssertGreen(SyntheticImage.PixelRgbaAt(fitted, 190, 10));
        AssertBlue(SyntheticImage.PixelRgbaAt(fitted, 10, 90));
        AssertYellow(SyntheticImage.PixelRgbaAt(fitted, 190, 90));
    }

    [Fact]
    public void Fit_TallerSource_CoversAndCentres()
    {
        // The source is taller than the piece, so the cover fit keeps its full width and crops top and bottom:
        // the middle band lands on the piece, its top half the source's top quadrants and its bottom half the
        // source's bottom quadrants.
        using var dir = new TempDir();
        var source = BackgroundFitter.LoadSource(SyntheticImage.SaveQuadrants(dir.Sub("src.png"), 200, 800));
        var piece = BackgroundFitter.LoadSource(SyntheticImage.SavePng(dir.Sub("piece.png"), 200, 100, (_, _) => (0, 0, 0, 255)));

        var fitted = PieceFitter.Fit(source, piece);

        Assert.Equal(200, fitted.PixelWidth);
        Assert.Equal(100, fitted.PixelHeight);
        AssertRed(SyntheticImage.PixelRgbaAt(fitted, 10, 10));
        AssertGreen(SyntheticImage.PixelRgbaAt(fitted, 190, 10));
        AssertBlue(SyntheticImage.PixelRgbaAt(fitted, 10, 90));
        AssertYellow(SyntheticImage.PixelRgbaAt(fitted, 190, 90));
    }

    [Fact]
    public void Fit_AlphaIsTheProduct()
    {
        using var dir = new TempDir();
        var source = BackgroundFitter.LoadSource(SyntheticImage.SavePng(dir.Sub("src.png"), 200, 100, (x, _) => (255, 0, 0, x < 100 ? (byte)255 : (byte)128)));
        var piece = BackgroundFitter.LoadSource(SyntheticImage.SavePng(dir.Sub("piece.png"), 200, 100, (_, y) => (0, 0, 0, y < 30 ? (byte)0 : y < 60 ? (byte)200 : (byte)255)));

        var fitted = PieceFitter.Fit(source, piece);

        // The fitted draw goes through a premultiplied render target, so the scaled alphas are allowed to be a
        // byte out; 0 and 255 come back exactly.
        Assert.Equal(0, SyntheticImage.PixelRgbaAt(fitted, 50, 10).A);
        Assert.Equal(0, SyntheticImage.PixelRgbaAt(fitted, 150, 10).A);
        Assert.Equal(200, SyntheticImage.PixelRgbaAt(fitted, 50, 45).A);
        Assert.InRange(SyntheticImage.PixelRgbaAt(fitted, 150, 45).A, 99, 101);
        Assert.Equal(255, SyntheticImage.PixelRgbaAt(fitted, 50, 80).A);
        Assert.InRange(SyntheticImage.PixelRgbaAt(fitted, 150, 80).A, 127, 129);
        Assert.True(SyntheticImage.PixelRgbaAt(fitted, 50, 80).R > 200, "expected the opaque half of the source to stay red");
    }

    [Fact]
    public void Fit_TransparentPiece_GivesTransparentResult()
    {
        using var dir = new TempDir();
        var source = BackgroundFitter.LoadSource(SyntheticImage.SaveQuadrants(dir.Sub("src.png"), 400, 400));
        var piece = BackgroundFitter.LoadSource(SyntheticImage.SavePng(dir.Sub("piece.png"), 100, 50, (_, _) => (0, 0, 0, 0)));

        var fitted = PieceFitter.Fit(source, piece);

        Assert.True(TransparentPng.IsFullyTransparent(fitted));
    }

    [Fact]
    public void Fit_FromPaths_MatchesBitmaps()
    {
        using var dir = new TempDir();
        var sourcePath = SyntheticImage.SaveQuadrants(dir.Sub("src.png"), 800, 200);
        var piecePath = SyntheticImage.SavePng(dir.Sub("piece.png"), 200, 100, (_, _) => (0, 0, 0, 255));

        var fromPaths = PieceFitter.Fit(sourcePath, piecePath);
        var fromBitmaps = PieceFitter.Fit(BackgroundFitter.LoadSource(sourcePath), BackgroundFitter.LoadSource(piecePath));

        Assert.Equal(fromBitmaps.PixelWidth, fromPaths.PixelWidth);
        Assert.Equal(fromBitmaps.PixelHeight, fromPaths.PixelHeight);
        Assert.Equal(Bytes(fromBitmaps), Bytes(fromPaths));
    }

    // 3.2 F1: a 200 x 100 green piece whose top 20 rows are outside its shape, and a 100 x 100 red picture. Fit
    // lands the picture at x 50 to 150 and Center keeps its own 100 x 100 there too, so the sides are left bare
    // and must show the piece's own green. Fill and Stretch reach every pixel and stay all red.
    [Theory]
    [InlineData(FitMode.Cover, false)]
    [InlineData(FitMode.Stretch, false)]
    [InlineData(FitMode.Contain, true)]
    [InlineData(FitMode.Center, true)]
    public void Fit_EveryMode_KeepsTheShape_AndTheOriginalWhereUncovered(FitMode mode, bool leavesSides)
    {
        using var dir = new TempDir();
        var source = BackgroundFitter.LoadSource(SyntheticImage.SavePng(dir.Sub("src.png"), 100, 100, (_, _) => (255, 0, 0, 255)));
        var piece = BackgroundFitter.LoadSource(SyntheticImage.SavePng(dir.Sub("piece.png"), 200, 100, (_, y) => (0, 255, 0, y < 20 ? (byte)0 : (byte)255)));

        var fitted = PieceFitter.Fit(source, piece, new FitOptions(mode));

        Assert.Equal(200, fitted.PixelWidth);
        Assert.Equal(100, fitted.PixelHeight);
        AssertShapeFilled(fitted, piece);
        AssertRed(SyntheticImage.PixelRgbaAt(fitted, 100, 60));
        if (leavesSides)
        {
            AssertGreen(SyntheticImage.PixelRgbaAt(fitted, 10, 60));
            AssertGreen(SyntheticImage.PixelRgbaAt(fitted, 190, 60));
        }
        else
        {
            AssertRed(SyntheticImage.PixelRgbaAt(fitted, 10, 60));
            AssertRed(SyntheticImage.PixelRgbaAt(fitted, 190, 60));
        }
    }

    [Fact]
    public void Fit_UncoveredArea_IsTheOriginalPassedIn_WithThePiecesShape()
    {
        // The piece being edited can be a working copy; what shows where the picture does not reach is the art the
        // caller names as the original, still cut to the piece's shape.
        using var dir = new TempDir();
        var source = BackgroundFitter.LoadSource(SyntheticImage.SavePng(dir.Sub("src.png"), 100, 100, (_, _) => (255, 0, 0, 255)));
        var piece = BackgroundFitter.LoadSource(SyntheticImage.SavePng(dir.Sub("piece.png"), 200, 100, (_, y) => (0, 255, 0, y < 20 ? (byte)0 : (byte)255)));
        var original = BackgroundFitter.LoadSource(SyntheticImage.SavePng(dir.Sub("original.png"), 200, 100, (_, _) => (0, 0, 255, 255)));

        var fitted = PieceFitter.Fit(source, piece, new FitOptions(FitMode.Contain), original);

        AssertBlue(SyntheticImage.PixelRgbaAt(fitted, 10, 60));
        AssertRed(SyntheticImage.PixelRgbaAt(fitted, 100, 60));
        Assert.Equal(0, SyntheticImage.PixelRgbaAt(fitted, 10, 10).A);
    }

    /// <summary>Every pixel keeps the piece's alpha: nothing inside the shape is left empty and nothing outside it
    /// is drawn. A picture edge that falls between pixels may be a byte or two out.</summary>
    internal static void AssertShapeFilled(BitmapSource fitted, BitmapSource piece)
    {
        for (var y = 0; y < piece.PixelHeight; y++)
        {
            for (var x = 0; x < piece.PixelWidth; x++)
            {
                var want = SyntheticImage.PixelRgbaAt(piece, x, y).A;
                var got = SyntheticImage.PixelRgbaAt(fitted, x, y).A;
                Assert.True(Math.Abs(want - got) <= 3, $"alpha at {x},{y} was {got}, the piece's is {want}");
            }
        }
    }

    private static byte[] Bytes(BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        return pixels;
    }
}
