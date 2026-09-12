using System.Windows.Media;
using BhMaps.Core.Imaging;

namespace BhMaps.App.Services;

/// <summary>One decode per file for a whole page of rows (addendum B, Performance). ThumbnailProvider caches a
/// finished decode, but sixty-seven rows offering the same custom picture ask for it before the first decode has
/// finished, and each of those calls would start its own. The task is shared instead, so the file is read once
/// and every row waits on the same result. Cleared by the shell after each scan, which is the only moment a file
/// on disk may have become a different picture.</summary>
public sealed class ThumbnailCache
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Task<ImageSource?>> _loads = new(StringComparer.OrdinalIgnoreCase);
    private readonly ThumbnailProvider _provider;

    public ThumbnailCache(ThumbnailProvider provider)
    {
        _provider = provider;
    }

    /// <summary>The picture at that path, or null when it could not be read. The token belongs to the caller
    /// alone: it is awaited through WaitAsync, so a row that scrolls away stops waiting without cancelling the
    /// decode every other row is waiting on.</summary>
    public async Task<ImageSource?> GetAsync(string fullPath, CancellationToken ct)
    {
        if (fullPath.Length == 0)
        {
            return null;
        }

        Task<ImageSource?> load;
        lock (_gate)
        {
            if (_loads.TryGetValue(fullPath, out var existing))
            {
                load = existing;
            }
            else
            {
                load = LoadAsync(fullPath);
                _loads[fullPath] = load;
            }
        }

        return await load.WaitAsync(ct);
    }

    /// <summary>Forgets every decode. The shell calls it before each scan's page refresh.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _loads.Clear();
        }
    }

    /// <summary>Never throws: a file that vanished between the scan and the decode leaves the tile blank, which
    /// is what every other thumbnail path in the app does with the same failure.</summary>
    private async Task<ImageSource?> LoadAsync(string fullPath)
    {
        try
        {
            var mtimeTicks = await Task.Run(() => File.GetLastWriteTimeUtc(fullPath).Ticks);
            return await _provider.GetAsync(fullPath, mtimeTicks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
