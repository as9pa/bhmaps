using System.Windows.Media.Imaging;

namespace BhMaps.Core.Imaging;

/// <summary>The pixel size of an image file, read from its header without decoding the pixels. Pack detail's
/// drawer shows it beside each file (spec 5).</summary>
public static class ImageDimensions
{
    /// <summary>Null for a missing, locked or undecodable file: a file row without a size is a fact about the
    /// file, never an error dialog.</summary>
    public static (int Width, int Height)? Read(string fullPath)
    {
        try
        {
            using var stream = File.OpenRead(fullPath);

            // DelayCreation with no cache reads the header only; the frame is measured before the stream closes.
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException or OverflowException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
