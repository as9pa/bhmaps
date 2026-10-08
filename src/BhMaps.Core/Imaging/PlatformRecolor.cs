using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BhMaps.Core.Imaging;

/// <summary>One straight-alpha pixel, in the byte order Bgra32 stores.</summary>
public readonly record struct Bgra(byte B, byte G, byte R, byte A);

/// <summary>Fades and recolours platform art. Opacity scales alpha only; hue rotates the colour in HSL and
/// leaves saturation, lightness and alpha alone. Safe to call from any thread.</summary>
public static class PlatformRecolor
{
    /// <summary>Reads <paramref name="sourcePng"/>, runs <see cref="Pixel"/> over every pixel and writes the
    /// result to <paramref name="destPng"/>, creating its folder and replacing any file already there.</summary>
    public static void Apply(string sourcePng, string destPng, double opacity, double hueDegrees)
    {
        BitmapSource source;
        using (var stream = File.OpenRead(sourcePng))
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            source = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        }

        Apply(source, destPng, opacity, hueDegrees);
    }

    /// <summary>The same recolour over a bitmap already in memory, so a caller that has just built one (a picture
    /// fitted to a piece, say) does not have to write it out and read it back.</summary>
    public static void Apply(BitmapSource source, string destPng, double opacity, double hueDegrees) =>
        Apply(source, destPng, opacity, hueDegrees, null);

    /// <summary>Reads <paramref name="sourcePng"/> and recolours it with a per-pixel alpha factor, as
    /// <see cref="Apply(BitmapSource, string, double, double, float[])"/> does.</summary>
    public static void Apply(string sourcePng, string destPng, double opacity, double hueDegrees, float[]? alphaScale) =>
        Apply(Decode(sourcePng), destPng, opacity, hueDegrees, alphaScale);

    /// <summary>The source as straight-alpha Bgra32, decoded in full.</summary>
    public static BitmapSource Decode(string sourcePng)
    {
        using var stream = File.OpenRead(sourcePng);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var source = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        source.Freeze();
        return source;
    }

    /// <summary>The alpha byte of every pixel, row by row, which is what <see cref="SeamMask"/> reads.</summary>
    public static byte[] AlphaOf(BitmapSource source)
    {
        if (source.Format != PixelFormats.Bgra32)
        {
            source = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        }

        var pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
        source.CopyPixels(pixels, source.PixelWidth * 4, 0);
        var alpha = new byte[source.PixelWidth * source.PixelHeight];
        for (var i = 0; i < alpha.Length; i++)
        {
            alpha[i] = pixels[(i * 4) + 3];
        }

        return alpha;
    }

    /// <summary>Spec 3.2 O1: <paramref name="alphaScale"/>, when given, is the seam mask's factor for each pixel
    /// (row by row) and replaces <paramref name="opacity"/> pixel by pixel; the hue is the same everywhere.</summary>
    public static void Apply(BitmapSource source, string destPng, double opacity, double hueDegrees, float[]? alphaScale)
    {
        if (source.Format != PixelFormats.Bgra32)
        {
            source = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        }

        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);
        if (alphaScale is not null && alphaScale.Length < width * height)
        {
            alphaScale = null;
        }

        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            for (var i = row; i < row + stride; i += 4)
            {
                var strength = alphaScale is null ? opacity : alphaScale[i / 4];
                var recoloured = Pixel(new Bgra(pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]), strength, hueDegrees);
                pixels[i] = recoloured.B;
                pixels[i + 1] = recoloured.G;
                pixels[i + 2] = recoloured.R;
                pixels[i + 3] = recoloured.A;
            }
        }

        var target = new WriteableBitmap(width, height, source.DpiX, source.DpiY, PixelFormats.Bgra32, null);
        target.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        target.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        var folder = Path.GetDirectoryName(destPng);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        using var output = File.Create(destPng);
        encoder.Save(output);
    }

    /// <summary>Scales the pixel's alpha by <paramref name="opacity"/> (clamped to 0..1) and rotates its hue by
    /// <paramref name="hueDegrees"/>, which wraps modulo 360 in either direction.</summary>
    public static Bgra Pixel(Bgra p, double opacity, double hueDegrees)
    {
        var alpha = (byte)Math.Round(p.A * Math.Clamp(opacity, 0, 1), MidpointRounding.AwayFromZero);
        var shift = ColorMath.Wrap(hueDegrees);
        var (h, s, l) = ColorMath.ToHsl(p.R, p.G, p.B);
        if (shift == 0 || s == 0)
        {
            // Nothing to rotate, or a grey pixel that has no hue to rotate: the colour bytes pass through.
            return new Bgra(p.B, p.G, p.R, alpha);
        }

        var (r, g, b) = ColorMath.ToRgb((h + shift) % 360, s, l);
        return new Bgra(b, g, r, alpha);
    }
}
