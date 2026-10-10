using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BhMaps.Core.Imaging;

/// <summary>One straight-alpha pixel, in the byte order Bgra32 stores.</summary>
public readonly record struct Bgra(byte B, byte G, byte R, byte A);

/// <summary>The background editor's tone controls as a piece carries them: Saturation and Contrast are -1..1,
/// Darken and Blur 0..1, and all zero leaves the piece alone.</summary>
public readonly record struct PieceTone(double Saturation = 0, double Contrast = 0, double Darken = 0, double Blur = 0)
{
    public bool IsNeutral => Saturation == 0 && Contrast == 0 && Darken == 0 && Blur == 0;
}

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
    public static void Apply(BitmapSource source, string destPng, double opacity, double hueDegrees, float[]? alphaScale) =>
        Apply(source, destPng, opacity, hueDegrees, alphaScale, default);

    /// <summary>The same, with the tone controls run over the piece first (<see cref="Toned"/>).</summary>
    public static void Apply(string sourcePng, string destPng, double opacity, double hueDegrees, float[]? alphaScale, PieceTone tone) =>
        Apply(Decode(sourcePng), destPng, opacity, hueDegrees, alphaScale, tone);

    /// <summary>The same, with the tone controls run over the piece first (<see cref="Toned"/>).</summary>
    public static void Apply(BitmapSource source, string destPng, double opacity, double hueDegrees, float[]? alphaScale, PieceTone tone)
    {
        if (!tone.IsNeutral)
        {
            source = Toned(source, tone);
        }

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

    /// <summary>The piece under the background editor's own tone pass: blurred the way a background is (40 px at
    /// full Blur, here in the piece's own pixels), then saturation, contrast and the Darken shade from
    /// <see cref="BackgroundFitter"/>. The piece keeps its own alpha, so a blur softens the picture inside the shape
    /// and never grows the shape. Frozen Bgra32 at the piece's size and DPI.</summary>
    public static BitmapSource Toned(BitmapSource source, PieceTone tone)
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

        var blur = Math.Clamp(tone.Blur, 0, 1);
        if (blur > 0 && width > 0 && height > 0)
        {
            // The blurred colour comes back premultiplied; converting it straightens it, which weighs each pixel's
            // colour by the shape around it, and the piece's own alpha then goes back on.
            var blurred = new byte[pixels.Length];
            new FormatConvertedBitmap(BackgroundFitter.Blurred(source, blur * 40, width, height), PixelFormats.Bgra32, null, 0)
                .CopyPixels(blurred, stride, 0);
            for (var i = 0; i < pixels.Length; i += 4)
            {
                if (blurred[i + 3] == 0)
                {
                    continue;
                }

                pixels[i] = blurred[i];
                pixels[i + 1] = blurred[i + 1];
                pixels[i + 2] = blurred[i + 2];
            }
        }

        BackgroundFitter.AdjustPixels(pixels, 0, tone.Saturation, tone.Contrast, tone.Darken);
        var target = new WriteableBitmap(width, height, source.DpiX, source.DpiY, PixelFormats.Bgra32, null);
        target.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        target.Freeze();
        return target;
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
