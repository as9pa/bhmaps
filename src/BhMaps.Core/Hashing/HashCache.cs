using System.Text.Json;
using BhMaps.Core.Model;
using BhMaps.Core.Storage;

namespace BhMaps.Core.Hashing;

/// <summary>JSON shape of one cache entry, keyed by full path in the containing dictionary.</summary>
public sealed record HashEntry(long Size, long MtimeTicks, string Sha256);

/// <summary>Remembers SHA-256 hashes by full path, valid while size and mtime are unchanged. Thread-safe.</summary>
public sealed class HashCache
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _cachePath;
    private readonly Dictionary<string, HashEntry> _entries;
    private readonly object _lock = new();

    private HashCache(string cachePath, Dictionary<string, HashEntry> entries)
    {
        _cachePath = cachePath;
        _entries = entries;
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Loads the cache file. A missing or unreadable file yields an empty cache.</summary>
    public static HashCache Load(string cachePath)
    {
        Dictionary<string, HashEntry>? loaded = null;
        if (File.Exists(cachePath))
        {
            try
            {
                loaded = JsonSerializer.Deserialize<Dictionary<string, HashEntry>>(File.ReadAllText(cachePath), JsonOptions);
            }
            catch (JsonException)
            {
                loaded = null;
            }
        }

        var entries = loaded is null
            ? new Dictionary<string, HashEntry>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, HashEntry>(loaded, StringComparer.OrdinalIgnoreCase);
        return new HashCache(cachePath, entries);
    }

    public string GetOrCompute(GameFile file) => GetOrCompute(file.FullPath, file.Size, file.MtimeTicks);

    public string GetOrCompute(string path, long size, long mtimeTicks)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(path, out var entry) && entry.Size == size && entry.MtimeTicks == mtimeTicks)
            {
                return entry.Sha256;
            }
        }

        var hash = FileHasher.Hash(path);
        lock (_lock)
        {
            _entries[path] = new HashEntry(size, mtimeTicks, hash);
        }

        return hash;
    }

    /// <summary>Drops entries for files that no longer exist, then writes the cache atomically.</summary>
    public void Save()
    {
        Dictionary<string, HashEntry> snapshot;
        lock (_lock)
        {
            foreach (var missing in _entries.Keys.Where(k => !File.Exists(k)).ToList())
            {
                _entries.Remove(missing);
            }

            snapshot = new Dictionary<string, HashEntry>(_entries, StringComparer.OrdinalIgnoreCase);
        }

        AtomicFile.WriteAllText(_cachePath, JsonSerializer.Serialize(snapshot, JsonOptions));
    }
}
