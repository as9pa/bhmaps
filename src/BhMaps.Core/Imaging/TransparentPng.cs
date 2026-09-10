using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BhMaps.Core.Imaging;

/// <summary>Spots platform art that changes nothing in game because every pixel is clear. Safe to call from any thread.</summary>
public static class TransparentPng
{
    /// <summary>True only for a decodable image whose every pixel has alpha 0.
    /// False for anything else, including unreadable files.</summary>
    public static bool IsFullyTransparent(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return IsFullyTransparent(decoder.Frames[0]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException or OverflowException)
        {
            // A file we cannot read or decode is not a proven-transparent file, and removal is only ever an offer.
            return false;
        }
    }

    /// <summary>True only when every pixel of the bitmap has alpha 0.
    /// False for anything else, including a bitmap whose pixels cannot be read.</summary>
    public static bool IsFullyTransparent(BitmapSource bitmap)
    {
        try
        {
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var stride = bitmap.PixelWidth * 4;
            var pixels = new byte[stride * bitmap.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            for (var i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0)
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException or OverflowException)
        {
            // A bitmap that will not convert, or is too large to buffer, is not a proven-transparent image either.
            return false;
        }
    }
}
