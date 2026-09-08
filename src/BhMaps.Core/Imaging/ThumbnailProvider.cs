using System.Collections.Concurrent;
using System.Windows.Media.Imaging;
using BhMaps.Core.Model;

namespace BhMaps.Core.Imaging;

/// <summary>Decodes small, frozen thumbnails off the calling thread and caches them by path plus mtime.</summary>
public sealed class ThumbnailProvider
{
    public const int DecodeWidth = 240;

    private readonly ConcurrentDictionary<string, BitmapSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The largest file by size, a cheap stand-in for the main platform art. Null for an empty folder.</summary>
    public static GameFile? PickRepresentative(GameFolder folder) =>
        folder.Files
            .OrderByDescending(f => f.Size)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    public Task<BitmapSource?> GetAsync(string fullPath, long mtimeTicks, CancellationToken ct = default)
    {
        var key = fullPath + "|" + mtimeTicks;
        if (_cache.TryGetValue(key, out var hit))
        {
            return Task.FromResult(hit);
        }

        return Task.Run(
            () =>
            {
                ct.ThrowIfCancellationRequested();
                var bitmap = Decode(fullPath);
                _cache[key] = bitmap;
                return bitmap;
            },
            ct);
    }

    /// <summary>Decodes at DecodeWidth pixels wide. Returns null for a missing or undecodable file.</summary>
    public static BitmapSource? Decode(string fullPath)
    {
        try
        {
            using var stream = File.OpenRead(fullPath);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = DecodeWidth;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            return null;
        }
    }
}
