using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BhMaps.Core.Tests.Helpers;

/// <summary>Builds small PNGs in memory and reads pixels back. Everything here runs on the MTA test thread.</summary>
public static class SyntheticImage
{
    public static string SavePng(string path, int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        var stride = width * 4;
        var data = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                var i = (y * stride) + (x * 4);
                data[i] = b;
                data[i + 1] = g;
                data[i + 2] = r;
                data[i + 3] = a;
            }
        }

        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), data, stride, 0);
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    /// <summary>Red top-left, green top-right, blue bottom-left, yellow bottom-right. Fully opaque.</summary>
    public static string SaveQuadrants(string path, int width, int height) =>
        SavePng(path, width, height, (x, y) =>
        {
            var right = x >= width / 2;
            var bottom = y >= height / 2;
            if (!right && !bottom)
            {
                return Rgb(255, 0, 0);
            }

            if (right && !bottom)
            {
                return Rgb(0, 255, 0);
            }

            return !right ? Rgb(0, 0, 255) : Rgb(255, 255, 0);
        });

    public static (byte R, byte G, byte B, byte A) Rgb(byte r, byte g, byte b) => (r, g, b, 255);

    public static (byte R, byte G, byte B) PixelAt(BitmapSource bitmap, int x, int y)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var px = new byte[4];
        converted.CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
        return (px[2], px[1], px[0]);
    }

    /// <summary>Like <see cref="PixelAt"/> but keeps the alpha byte.</summary>
    public static (byte R, byte G, byte B, byte A) PixelRgbaAt(BitmapSource bitmap, int x, int y)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var px = new byte[4];
        converted.CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
        return (px[2], px[1], px[0], px[3]);
    }

    /// <summary>Mean of (R+G+B)/3 over every pixel, in 0..255.</summary>
    public static double MeanLuminance(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = bitmap.PixelWidth * 4;
        var data = new byte[stride * bitmap.PixelHeight];
        converted.CopyPixels(data, stride, 0);
        double sum = 0;
        for (var i = 0; i < data.Length; i += 4)
        {
            sum += (data[i] + data[i + 1] + data[i + 2]) / 3.0;
        }

        return sum / (bitmap.PixelWidth * (double)bitmap.PixelHeight);
    }

    public static BitmapSource DecodePng(string path)
    {
        using var stream = File.OpenRead(path);
        return new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
    }

    public static BitmapSource DecodeJpeg(byte[] bytes)
    {
        var decoder = new JpegBitmapDecoder(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }
}
