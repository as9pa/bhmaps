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

        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            for (var i = row; i < row + stride; i += 4)
            {
                var recoloured = Pixel(new Bgra(pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]), opacity, hueDegrees);
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
        var shift = Wrap(hueDegrees);
        var (h, s, l) = ToHsl(p.R, p.G, p.B);
        if (shift == 0 || s == 0)
        {
            // Nothing to rotate, or a grey pixel that has no hue to rotate: the colour bytes pass through.
            return new Bgra(p.B, p.G, p.R, alpha);
        }

        var (r, g, b) = ToRgb((h + shift) % 360, s, l);
        return new Bgra(b, g, r, alpha);
    }

    /// <summary>RGB bytes to a hue in degrees (0..360) with saturation and lightness in 0..1.</summary>
    internal static (double H, double S, double L) ToHsl(byte r, byte g, byte b)
    {
        var rd = r / 255.0;
        var gd = g / 255.0;
        var bd = b / 255.0;
        var max = Math.Max(rd, Math.Max(gd, bd));
        var min = Math.Min(rd, Math.Min(gd, bd));
        var lightness = (max + min) / 2;
        var delta = max - min;
        if (delta == 0)
        {
            return (0, 0, lightness);
        }

        var saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);
        double hue;
        if (max == rd)
        {
            hue = ((gd - bd) / delta) + (gd < bd ? 6 : 0);
        }
        else if (max == gd)
        {
            hue = ((bd - rd) / delta) + 2;
        }
        else
        {
            hue = ((rd - gd) / delta) + 4;
        }

        return (hue * 60, saturation, lightness);
    }

    /// <summary>A hue in degrees (0..360) with saturation and lightness in 0..1 back to RGB bytes.</summary>
    internal static (byte R, byte G, byte B) ToRgb(double h, double s, double l)
    {
        if (s == 0)
        {
            var grey = Channel(l);
            return (grey, grey, grey);
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
        var p = (2 * l) - q;
        var turn = h / 360;
        return (Channel(FromTurn(p, q, turn + (1.0 / 3))), Channel(FromTurn(p, q, turn)), Channel(FromTurn(p, q, turn - (1.0 / 3))));
    }

    /// <summary>Negative shifts wrap up, so -120 and +240 name the same rotation.</summary>
    private static double Wrap(double degrees)
    {
        var wrapped = degrees % 360;
        return wrapped < 0 ? wrapped + 360 : wrapped;
    }

    /// <summary>One channel of the HSL to RGB inverse, at <paramref name="t"/> turns around the colour wheel.</summary>
    private static double FromTurn(double p, double q, double t)
    {
        if (t < 0)
        {
            t += 1;
        }
        else if (t > 1)
        {
            t -= 1;
        }

        if (t < 1.0 / 6)
        {
            return p + ((q - p) * 6 * t);
        }

        if (t < 1.0 / 2)
        {
            return q;
        }

        if (t < 2.0 / 3)
        {
            return p + ((q - p) * ((2.0 / 3) - t) * 6);
        }

        return p;
    }

    private static byte Channel(double value) =>
        (byte)Math.Round(Math.Clamp(value, 0, 1) * 255, MidpointRounding.AwayFromZero);
}
