using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using BhMaps.Core.Hashing;
using BhMaps.Core.LevelData;

namespace BhMaps.Core.Imaging;

/// <summary>Composed previews kept as JPEG files, keyed by every file the render reads.
/// A miss renders on the STA queue; a hit only touches the file so the sweep keeps it.</summary>
public sealed class PreviewCache
{
    public const int JpegQuality = 88;

    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    private readonly HashCache _hashes;
    private readonly RenderQueue _queue;

    public PreviewCache(string appDataDir, HashCache hashes, RenderQueue queue)
    {
        Root = Path.Combine(appDataDir, "previews");
        _hashes = hashes;
        _queue = queue;
    }

    /// <summary>&lt;appDataDir&gt;\previews. Created on the first write, so a fresh install has nothing to sweep.</summary>
    public string Root { get; }

    /// <summary>sha256 of level name, size and every input file hash, lowercase hex.</summary>
    public string KeyFor(string levelName, int width, int height, IReadOnlyList<string> inputs)
    {
        // The inputs stay an ordered list: draw order changes the picture, and one file named by two
        // slots must count twice, so folding them into a set would collide two different renders.
        var text = new StringBuilder(levelName).Append('|').Append(width).Append('x').Append(height);
        foreach (var input in inputs)
        {
            var info = new FileInfo(input);
            text.Append('|')
                .Append(Path.GetFileName(input))
                .Append(':')
                .Append(_hashes.GetOrCompute(input, info.Length, info.LastWriteTimeUtc.Ticks));
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>The cached JPEG path. Renders on the STA queue on a miss; touches the file on a hit.
    /// One AssetSources instance serves one call at a time: this reads it on the caller's thread and then
    /// hands it to the render thread, so the instance is never touched by both at once.</summary>
    public async Task<string> GetOrRenderAsync(
        LevelDesc level, int width, int height, AssetSources sources, CancellationToken ct = default)
    {
        var inputs = MapCompositor.CollectInputs(level, sources);
        var key = KeyFor(level.LevelName, width, height, inputs);
        var path = Path.Combine(Root, key + ".jpg");
        if (File.Exists(path))
        {
            try
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The touch only feeds the sweep, so a locked preview is not worth failing the call over.
            }

            return path;
        }

        var jpeg = await _queue
            .RunAsync(() => Encode(MapCompositor.Render(level, width, height, sources)), ct)
            .ConfigureAwait(false);

        Directory.CreateDirectory(Root);
        var temp = Path.Combine(Root, $".{key}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temp, jpeg);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        return path;
    }

    /// <summary>Deletes cache files whose LastWriteTimeUtc is older than MaxAge. Call once at startup.</summary>
    public int Sweep(DateTimeOffset now)
    {
        if (!Directory.Exists(Root))
        {
            return 0;
        }

        var cutoff = now.UtcDateTime - MaxAge;
        var deleted = 0;
        foreach (var path in Directory.GetFiles(Root, "*.jpg"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                {
                    File.Delete(path);
                    deleted++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A preview still open somewhere stays until the next startup; the sweep is housekeeping.
            }
        }

        return deleted;
    }

    private static byte[] Encode(BitmapSource bitmap)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
