using System.Windows.Media;
using System.Windows.Media.Imaging;
using BhMaps.Core.Imaging;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class BackgroundFitterTests
{
    private const int W = BackgroundFitter.OutputWidth;
    private const int H = BackgroundFitter.OutputHeight;

    private static void AssertRed((byte R, byte G, byte B) p) { Assert.True(p.R > 200 && p.G < 60 && p.B < 60, $"expected red, got {p}"); }
    private static void AssertGreen((byte R, byte G, byte B) p) { Assert.True(p.G > 200 && p.R < 60 && p.B < 60, $"expected green, got {p}"); }
    private static void AssertBlue((byte R, byte G, byte B) p) { Assert.True(p.B > 200 && p.R < 60 && p.G < 60, $"expected blue, got {p}"); }
    private static void AssertYellow((byte R, byte G, byte B) p) { Assert.True(p.R > 200 && p.G > 200 && p.B < 60, $"expected yellow, got {p}"); }
    private static void AssertBlack((byte R, byte G, byte B) p) { Assert.True(p.R < 8 && p.G < 8 && p.B < 8, $"expected black, got {p}"); }

    [Theory]
    [InlineData(FitMode.Cover)]
    [InlineData(FitMode.Contain)]
    [InlineData(FitMode.Stretch)]
    public void Fit_ProducesJpegOfOutputSizeForEveryMode(FitMode mode)
    {
        using var tmp = new TempDir();
        var src = SyntheticImage.SaveQuadrants(tmp.Sub("src.png"), 800, 200);

        var bytes = BackgroundFitter.Fit(src, new FitOptions(Mode: mode));

        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xD8, bytes[1]);
        var decoded = SyntheticImage.DecodeJpeg(bytes);
        Assert.Equal(W, decoded.PixelWidth);
        Assert.Equal(H, decoded.PixelHeight);
    }

    [Fact]
    public void Render_ReturnsFrozenBgr24OfRequestedSize()
    {
        using var tmp = new TempDir();
        var src = SyntheticImage.SaveQuadrants(tmp.Sub("src.png"), 800, 200);

        var bitmap = BackgroundFitter.Render(src, new FitOptions(), 320, 180);

        Assert.Equal(320, bitmap.PixelWidth);
        Assert.Equal(180, bitmap.PixelHeight);
        Assert.True(bitmap.IsFrozen);
        Assert.Equal(PixelFormats.Bgr24, bitmap.Format);
    }

    [Fact]
    public void Cover_WideSourceLosesWidthAndPanXSelectsEdge()
    {
        using var tmp = new TempDir();
        var src = SyntheticImage.SaveQuadrants(tmp.Sub("src.png"), 800, 200);

        var rect = BackgroundFitter.DestinationRect(800, 200, new FitOptions(FitMode.Cover, PanX: 0.5, PanY: 0.5), W, H);
        Assert.Equal(H, rect.Height, 0.01);
        Assert.Equal(4604, rect.Width, 1.0);
        Assert.Equal(0, rect.Y, 0.01);
        Assert.Equal(-(rect.Width - W) / 2, rect.X, 0.01);

        var left = BackgroundFitter.Render(src, new FitOptions(FitMode.Cover, PanX: 0), 512, 288);
        var right = BackgroundFitter.Render(src, new FitOptions(FitMode.Cover, PanX: 1), 512, 288);

        AssertRed(SyntheticImage.PixelAt(left, 5, 5));
        AssertBlue(SyntheticImage.PixelAt(left, 5, 282));
        AssertGreen(SyntheticImage.PixelAt(right, 506, 5));
        AssertYellow(SyntheticImage.PixelAt(right, 506, 282));
    }

    [Fact]
    public void Cover_TallSourceLosesHeightAndPanYSelectsEdge()
    {
        using var tmp = new TempDir();
        var src = SyntheticImage.SaveQuadrants(tmp.Sub("src.png"), 200, 800);

        var rect = BackgroundFitter.DestinationRect(200, 800, new FitOptions(FitMode.Cover, PanY: 0), W, H);
        Assert.Equal(W, rect.Width, 0.01);
        Assert.Equal(8192, rect.Height, 1.0);
        Assert.Equal(0, rect.X, 0.01);
        Assert.Equal(0, rect.Y, 0.01);

        var top = BackgroundFitter.Render(src, new FitOptions(FitMode.Cover, PanY: 0), 512, 288);
        var bottom = BackgroundFitter.Render(src, new FitOptions(FitMode.Cover, PanY: 1), 512, 288);

        AssertRed(SyntheticImage.PixelAt(top, 5, 5));
        AssertGreen(SyntheticImage.PixelAt(top, 506, 5));
        AssertBlue(SyntheticImage.PixelAt(bottom, 5, 282));
        AssertYellow(SyntheticImage.PixelAt(bottom, 506, 282));
    }

    [Fact]
    public void Contain_AddsBlackBarsAndKeepsWholeImage()
    {
        using var tmp = new TempDir();
        var src = SyntheticImage.SaveQuadrants(tmp.Sub("src.png"), 800, 200);

        var bitmap = BackgroundFitter.Render(src, new FitOptions(FitMode.Contain), W, H);

        AssertBlack(SyntheticImage.PixelAt(bitmap, 0, 0));
        AssertBlack(SyntheticImage.PixelAt(bitmap, W - 1, H - 1));
        AssertBlack(SyntheticImage.PixelAt(bitmap, W / 2, 100));
        AssertRed(SyntheticImage.PixelAt(bitmap, 512, 450));
        AssertGreen(SyntheticImage.PixelAt(bitmap, 1536, 450));
        AssertBlue(SyntheticImage.PixelAt(bitmap, 512, 700));
        AssertYellow(SyntheticImage.PixelAt(bitmap, 1536, 700));
    }

    [Fact]
    public void Stretch_KeepsCornerColors()
    {
        using var tmp = new TempDir();
        var src = SyntheticImage.SaveQuadrants(tmp.Sub("src.png"), 800, 200);

        var bitmap = BackgroundFitter.Render(src, new FitOptions(FitMode.Stretch), W, H);

        AssertRed(SyntheticImage.PixelAt(bitmap, 5, 5));
        AssertGreen(SyntheticImage.PixelAt(bitmap, W - 6, 5));
        AssertBlue(SyntheticImage.PixelAt(bitmap, 5, H - 6));
        AssertYellow(SyntheticImage.PixelAt(bitmap, W - 6, H - 6));
    }

    [Fact]
    public void Darken_ScalesMeanLuminance()
    {
        using var tmp = new TempDir();
        var src = SyntheticImage.SaveQuadrants(tmp.Sub("src.png"), 800, 200);

        var normal = SyntheticImage.MeanLuminance(BackgroundFitter.Render(src, new FitOptions(FitMode.Stretch), 512, 288));
        var half = SyntheticImage.MeanLuminance(BackgroundFitter.Render(src, new FitOptions(FitMode.Stretch, Darken: 0.5), 512, 288));
        var black = SyntheticImage.MeanLuminance(BackgroundFitter.Render(src, new FitOptions(FitMode.Stretch, Darken: 1.0), 512, 288));

        Assert.InRange(normal, 95, 120);
        Assert.InRange(half / normal, 0.45, 0.55);
        Assert.InRange(black, 0, 2);
    }

    [Fact]
    public void TransparentPixelsAreCompositedOverBlack()
    {
        using var tmp = new TempDir();
        var clear = SyntheticImage.SavePng(tmp.Sub("clear.png"), 100, 100, (_, _) => (255, 255, 255, 0));
        var half = SyntheticImage.SavePng(tmp.Sub("half.png"), 100, 100, (_, _) => (255, 255, 255, 128));

        var clearOut = BackgroundFitter.Render(clear, new FitOptions(FitMode.Stretch), 200, 100);
        var halfOut = BackgroundFitter.Render(half, new FitOptions(FitMode.Stretch), 200, 100);

        AssertBlack(SyntheticImage.PixelAt(clearOut, 10, 10));
        var p = SyntheticImage.PixelAt(halfOut, 10, 10);
        Assert.InRange(p.R, 118, 138);
        Assert.InRange(p.G, 118, 138);
        Assert.InRange(p.B, 118, 138);
    }

    private static double MeanAbsoluteDifference(BitmapSource a, BitmapSource b)
    {
        Assert.Equal(a.PixelWidth, b.PixelWidth);
        Assert.Equal(a.PixelHeight, b.PixelHeight);
        var stride = a.PixelWidth * 4;
        var one = new byte[stride * a.PixelHeight];
        var two = new byte[stride * b.PixelHeight];
        new FormatConvertedBitmap(a, PixelFormats.Bgra32, null, 0).CopyPixels(one, stride, 0);
        new FormatConvertedBitmap(b, PixelFormats.Bgra32, null, 0).CopyPixels(two, stride, 0);
        double sum = 0;
        var counted = 0;
        for (var i = 0; i < one.Length; i++)
        {
            if (i % 4 == 3)
            {
                continue;
            }

            sum += Math.Abs(one[i] - two[i]);
            counted++;
        }

        return sum / counted;
    }

    [Fact]
    public void LoadWorkingSource_ScalesDownToWhatTheCanvasNeedsAndNeverUp()
    {
        using var tmp = new TempDir();
        var big = SyntheticImage.SaveQuadrants(tmp.Sub("big.png"), 1600, 900);
        var small = SyntheticImage.SaveQuadrants(tmp.Sub("small.png"), 100, 50);

        var scaled = BackgroundFitter.LoadWorkingSource(big, 640, 360);
        var kept = BackgroundFitter.LoadWorkingSource(small, 640, 360);

        Assert.Equal(640, scaled.PixelWidth);
        Assert.Equal(360, scaled.PixelHeight);
        Assert.True(scaled.IsFrozen);
        Assert.Equal(100, kept.PixelWidth);
        Assert.Equal(50, kept.PixelHeight);
    }

    [Theory]
    [InlineData(FitMode.Cover, 0.25)]
    [InlineData(FitMode.Cover, 0.5)]
    [InlineData(FitMode.Contain, 0.5)]
    [InlineData(FitMode.Stretch, 0.5)]
    public void WorkingSourceFit_MatchesTheFullFitDownscaled(FitMode mode, double panX)
    {
        using var tmp = new TempDir();
        var src = SyntheticImage.SaveQuadrants(tmp.Sub("src.png"), 1600, 900);
        var options = new FitOptions(mode, PanX: panX, Darken: 0.2);

        var full = BackgroundFitter.Render(src, options, W, H);
        var reference = BackgroundFitter.Render(full, new FitOptions(FitMode.Stretch), 640, 360);
        var preview = BackgroundFitter.Render(BackgroundFitter.LoadWorkingSource(src, 640, 360), options, 640, 360);

        Assert.True(MeanAbsoluteDifference(reference, preview) < 6, "preview drifted from the full fit");
    }
}
