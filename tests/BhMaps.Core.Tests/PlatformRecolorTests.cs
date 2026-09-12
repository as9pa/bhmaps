using BhMaps.Core.Imaging;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class PlatformRecolorTests
{
    [Fact]
    public void Pixel_Identity_ReturnsInput()
    {
        Bgra[] inputs =
        [
            new Bgra(0, 0, 255, 255),
            new Bgra(40, 90, 200, 255),
            new Bgra(128, 128, 128, 255),
            new Bgra(7, 3, 1, 17),
            new Bgra(255, 255, 255, 0),
        ];

        foreach (var input in inputs)
        {
            Assert.Equal(input, PlatformRecolor.Pixel(input, 1, 0));
        }
    }

    [Fact]
    public void Pixel_HalfOpacity_HalvesAlphaOnly()
    {
        Assert.Equal(new Bgra(40, 90, 200, 100), PlatformRecolor.Pixel(new Bgra(40, 90, 200, 200), 0.5, 0));

        // 127.5 rounds half away from zero.
        Assert.Equal(new Bgra(40, 90, 200, 128), PlatformRecolor.Pixel(new Bgra(40, 90, 200, 255), 0.5, 0));
    }

    [Fact]
    public void Pixel_Hue_LeavesGreyAlone()
    {
        var grey = new Bgra(128, 128, 128, 255);

        Assert.Equal(grey, PlatformRecolor.Pixel(grey, 1, 90));
    }

    [Fact]
    public void Pixel_RedPlus120_IsGreen() =>
        Assert.Equal(new Bgra(0, 255, 0, 255), PlatformRecolor.Pixel(new Bgra(0, 0, 255, 255), 1, 120));

    [Fact]
    public void Pixel_RedPlus240_IsBlue() =>
        Assert.Equal(new Bgra(255, 0, 0, 255), PlatformRecolor.Pixel(new Bgra(0, 0, 255, 255), 1, 240));

    [Fact]
    public void Pixel_HueMinus120_EqualsPlus240()
    {
        var input = new Bgra(40, 90, 200, 255);

        Assert.Equal(PlatformRecolor.Pixel(input, 1, 240), PlatformRecolor.Pixel(input, 1, -120));
    }

    [Fact]
    public void Pixel_TransparentStaysTransparent()
    {
        // Cleared platform pixels are black with no alpha; a grey keeps its colour bytes under any hue.
        Assert.Equal(new Bgra(0, 0, 0, 0), PlatformRecolor.Pixel(new Bgra(0, 0, 0, 0), 0.5, 90));
        Assert.Equal(new Bgra(0, 0, 255, 0), PlatformRecolor.Pixel(new Bgra(0, 0, 255, 0), 0.5, 0));
    }

    [Fact]
    public void Pixel_HueKeepsLightnessAndSaturation()
    {
        var input = new Bgra(40, 90, 200, 255);
        var (_, s, l) = PlatformRecolor.ToHsl(input.R, input.G, input.B);

        var output = PlatformRecolor.Pixel(input, 1, 37);

        var (_, shiftedS, shiftedL) = PlatformRecolor.ToHsl(output.R, output.G, output.B);
        Assert.Equal(s, shiftedS, 1.0 / 255);
        Assert.Equal(l, shiftedL, 1.0 / 255);
    }

    [Fact]
    public void Apply_WritesRecolouredPng()
    {
        using var tmp = new TempDir();
        (byte R, byte G, byte B, byte A)[,] source =
        {
            { (255, 0, 0, 255), (0, 255, 0, 200) },
            { (128, 128, 128, 255), (0, 0, 0, 0) },
        };
        var path = SyntheticImage.SavePng(tmp.Sub("set", "piece.png"), 2, 2, (x, y) => source[y, x]);
        var dest = Path.Combine(tmp.Path, "out", "piece.png");

        PlatformRecolor.Apply(path, dest, 0.5, 120);

        var written = SyntheticImage.DecodePng(dest);
        Assert.Equal((0, 255, 0, 128), SyntheticImage.PixelRgbaAt(written, 0, 0));
        Assert.Equal((0, 0, 255, 100), SyntheticImage.PixelRgbaAt(written, 1, 0));
        Assert.Equal((128, 128, 128, 128), SyntheticImage.PixelRgbaAt(written, 0, 1));
        Assert.Equal((0, 0, 0, 0), SyntheticImage.PixelRgbaAt(written, 1, 1));
    }

    [Fact]
    public void Apply_OverwritesAnExistingFile()
    {
        using var tmp = new TempDir();
        var path = SyntheticImage.SavePng(tmp.Sub("set", "piece.png"), 2, 2, (_, _) => (255, 0, 0, 255));
        var dest = SyntheticImage.SavePng(tmp.Sub("out", "piece.png"), 4, 4, (_, _) => (0, 0, 255, 255));

        PlatformRecolor.Apply(path, dest, 1, 120);

        var written = SyntheticImage.DecodePng(dest);
        Assert.Equal(2, written.PixelWidth);
        Assert.Equal((0, 255, 0, 255), SyntheticImage.PixelRgbaAt(written, 0, 0));
    }
}
