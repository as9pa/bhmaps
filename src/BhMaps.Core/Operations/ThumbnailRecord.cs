using System.Text.Json;
using BhMaps.Core.Hashing;
using BhMaps.Core.Storage;

namespace BhMaps.Core.Operations;

/// <summary>One pack picture on the game: the map folder, the pack it came from, the game file name, and the
/// SHA-256 of the jpg as it was copied.</summary>
public sealed record ThumbnailRecordEntry(string Map, string Pack, string File, string Sha256);

/// <summary>3.7: thumbnails.bhmaps.json in the app's data folder. It names every map-select picture a pack put on
/// the game, so a later save keeps that picture rather than rendering over it. An entry holds only while the game's
/// jpg still has the hash it was copied with; Reset and Undo drop it.</summary>
public static class ThumbnailRecord
{
    public const string FileName = "thumbnails.bhmaps.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>appDataDir\thumbnails.bhmaps.json.</summary>
    public static string PathFor(string appDataDir) => Path.Combine(appDataDir, FileName);

    /// <summary>The entries on disk; none when the file is missing or cannot be read.</summary>
    public static IReadOnlyList<ThumbnailRecordEntry> Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<List<ThumbnailRecordEntry>>(File.ReadAllText(path), JsonOptions) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // A record we cannot read keeps nothing: every picture renders as it did before 3.7.
            return [];
        }
    }

    /// <summary>Records the pack picture now on the game for this map's file, replacing any earlier entry for it.</summary>
    public static void Note(string path, string map, string pack, string file, string sha256) =>
        Save(path, [.. Load(path).Where(e => !Same(e, map, file)), new ThumbnailRecordEntry(map, pack, file, sha256)]);

    /// <summary>Forgets the map's entry for this file, or every entry of the map when file is null.</summary>
    public static void Drop(string path, string map, string? file = null)
    {
        var entries = Load(path);
        var kept = entries
            .Where(e => !(e.Map.Equals(map, StringComparison.OrdinalIgnoreCase) && (file is null || Same(e, map, file))))
            .ToList();
        if (kept.Count != entries.Count)
        {
            Save(path, kept);
        }
    }

    /// <summary>True when the record names this map's file and the game's jpg at targetPath still has the hash it
    /// was copied with, so the save leaves the pack picture where it is.</summary>
    public static bool Keeps(IReadOnlyList<ThumbnailRecordEntry> entries, string map, string file, string targetPath)
    {
        var entry = entries.FirstOrDefault(e => Same(e, map, file));
        return entry is not null
            && System.IO.File.Exists(targetPath)
            && FileHasher.Hash(targetPath).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Drops every entry whose game jpg is gone or no longer has the recorded hash: after an Undo has put
    /// back the picture the pack's copy replaced.</summary>
    public static void Prune(string path, string thumbnailsDir)
    {
        var entries = Load(path);
        var kept = entries.Where(e => Keeps(entries, e.Map, e.File, Path.Combine(thumbnailsDir, e.File))).ToList();
        if (kept.Count != entries.Count)
        {
            Save(path, kept);
        }
    }

    private static bool Same(ThumbnailRecordEntry e, string map, string file) =>
        e.Map.Equals(map, StringComparison.OrdinalIgnoreCase) && e.File.Equals(file, StringComparison.OrdinalIgnoreCase);

    /// <summary>Writes the entries, or deletes the file when there are none left.</summary>
    private static void Save(string path, IReadOnlyList<ThumbnailRecordEntry> entries)
    {
        if (entries.Count == 0)
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }

            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(entries, JsonOptions));
    }
}
