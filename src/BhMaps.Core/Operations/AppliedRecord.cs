using System.Text.Json;
using BhMaps.Core.Hashing;
using BhMaps.Core.Model;
using BhMaps.Core.Storage;

namespace BhMaps.Core.Operations;

/// <summary>What the app itself wrote into one game file: the source it came from, the pack that source belongs to,
/// the hash of the bytes that landed and the hash the source file had at the time. The two differ whenever the write
/// changed the bytes on the way in, which a picture fitted to the background size does. Source is relative to the
/// library folder when it lies under it, which packs and My Backgrounds both do, and the full path otherwise. Pack is
/// null for a picture of the owner's own. SourceHash is null in a record written before it was kept.</summary>
public sealed record AppliedEntry(string Source, string? Pack, string Hash, string? SourceHash, DateTimeOffset At);

/// <summary>One file a game write is about to lay down: where it lands, relative to the game folder, and the
/// library file the bytes come from.</summary>
public sealed record AppliedSource(string GameRelativePath, string SourceFullPath, string? PackName);

public static class AppliedSources
{
    /// <summary>A pack mirrors the game layout, so the source of every path a pack write touches sits at the same
    /// relative path inside the pack.</summary>
    public static IReadOnlyList<AppliedSource> FromPack(Pack pack, IEnumerable<string> gameRelativePaths) =>
        [.. gameRelativePaths.Select(
            relativePath => new AppliedSource(
                relativePath, Path.Combine(pack.FullPath, relativePath), pack.Name))];
}

/// <summary>Keeps what the app wrote into the game in &lt;appDataDir&gt;\applied.json, keyed by game-relative path.
/// Hashing the game against the packs cannot see a pack file edited outside the app: the game copy then matches
/// nothing and reads as custom. This record is the app's own memory of every write it made, so a later read can
/// tell an edited pack file from a picture the user dropped in by hand.</summary>
public sealed class AppliedRecord
{
    /// <summary>Bumped whenever a shape change makes a file written by an older build wrong. A file stamped with
    /// any other version reads as an empty record, the same as no file at all.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Not indented: nothing reads this file by eye, and it holds one line per game file.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>JSON shape of the record file. A file written before the version existed reads back as 0.</summary>
    private sealed record RecordFile(int Version, Dictionary<string, AppliedEntry> Files);

    private readonly Dictionary<string, AppliedEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public static string PathFor(string appDataDir) => Path.Combine(appDataDir, "applied.json");

    /// <summary>What the app wrote, keyed by game-relative path, compared ignoring case as Windows does.</summary>
    public IReadOnlyDictionary<string, AppliedEntry> Entries => _entries;

    /// <summary>A missing, unreadable, malformed or older-version file loads as an empty record. The record is a
    /// hint about what is on, never the truth about it, so losing it costs nothing a rescan cannot rebuild.</summary>
    public static AppliedRecord Load(string path)
    {
        var record = new AppliedRecord();
        try
        {
            var file = JsonSerializer.Deserialize<RecordFile>(File.ReadAllText(path), JsonOptions);
            if (file is not { Version: SchemaVersion, Files: not null })
            {
                return record;
            }

            foreach (var (gameRelativePath, entry) in file.Files)
            {
                if (entry is not null)
                {
                    record._entries[gameRelativePath] = entry;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            // A record the app cannot read is an empty record, and the next write starts it again.
            return new AppliedRecord();
        }

        return record;
    }

    public void Save(string path) =>
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(new RecordFile(SchemaVersion, _entries), JsonOptions));

    /// <summary>Records the sources whose write really landed. It landed when the game file now holds the source's
    /// own bytes, and also when it holds something other than what was there before the write: a picture fitted to
    /// the background size lands as neither the source nor the old file. A game file that is neither the source nor
    /// changed is a write that never ran, so its old entry stays; one whose game file has gone, which is what a
    /// reset leaves behind, loses its entry. <paramref name="previousHash"/> gives the hash of a game-relative path
    /// as it was before the write, or null when it held nothing, and null for every path makes any file that is
    /// there now count as written. These are the few files just written, so they are hashed straight rather than
    /// through a cache.</summary>
    public static void Note(
        string recordPath,
        string gamePath,
        string libraryPath,
        IReadOnlyList<AppliedSource> sources,
        DateTimeOffset now,
        Func<string, string?> previousHash)
    {
        var record = Load(recordPath);
        var changed = false;
        foreach (var source in sources)
        {
            var gameFile = Path.Combine(gamePath, source.GameRelativePath);
            try
            {
                if (!File.Exists(gameFile))
                {
                    changed |= record._entries.Remove(source.GameRelativePath);
                    continue;
                }

                if (!File.Exists(source.SourceFullPath))
                {
                    continue;
                }

                var hash = FileHasher.Hash(gameFile);
                var sourceHash = FileHasher.Hash(source.SourceFullPath);
                if (!hash.Equals(sourceHash, StringComparison.OrdinalIgnoreCase)
                    && hash.Equals(previousHash(source.GameRelativePath), StringComparison.OrdinalIgnoreCase))
                {
                    // Not the source's bytes and not one byte different from what was there: nothing was written.
                    continue;
                }

                record._entries[source.GameRelativePath] = new AppliedEntry(
                    RelativeToLibrary(source.SourceFullPath, libraryPath), source.PackName, hash, sourceHash, now);
                changed = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file we cannot read leaves its entry as it was: the record is bookkeeping, and a write that
                // succeeded must not be reported as failed because the note after it could not be taken.
            }
        }

        if (changed)
        {
            SaveQuietly(record, recordPath);
        }
    }

    /// <summary>Points every entry that came from <paramref name="fromFullPath"/> at <paramref name="toFullPath"/>,
    /// for a library file that has been renamed. The game files keep their bytes and their entries: the write did
    /// come from this file, and only its name changed, so forgetting the entries instead would make every map the
    /// picture is on read as art nobody applied.</summary>
    public static void Renamed(string recordPath, string libraryPath, string fromFullPath, string toFullPath)
    {
        var record = Load(recordPath);
        var from = RelativeToLibrary(fromFullPath, libraryPath);
        var to = RelativeToLibrary(toFullPath, libraryPath);
        var changed = false;
        foreach (var (gameRelativePath, entry) in record._entries.ToList())
        {
            if (entry.Source.Equals(from, StringComparison.OrdinalIgnoreCase))
            {
                record._entries[gameRelativePath] = entry with { Source = to };
                changed = true;
            }
        }

        if (changed)
        {
            SaveQuietly(record, recordPath);
        }
    }

    /// <summary>Drops these game-relative paths from the record, for a caller that knows the files have gone.</summary>
    public static void Forget(string recordPath, IEnumerable<string> gameRelativePaths)
    {
        var record = Load(recordPath);
        var changed = false;
        foreach (var gameRelativePath in gameRelativePaths)
        {
            changed |= record._entries.Remove(gameRelativePath);
        }

        if (changed)
        {
            SaveQuietly(record, recordPath);
        }
    }

    private static void SaveQuietly(AppliedRecord record, string recordPath)
    {
        try
        {
            record.Save(recordPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A record the app cannot write is a record it reads back empty next time, which is a miss, not a
            // failure the user did anything to cause.
        }
    }

    /// <summary>The source as the record stores it: relative to the library when it lies under it, so moving the
    /// library does not orphan every entry, and the full path when it came from somewhere else.</summary>
    private static string RelativeToLibrary(string fullPath, string libraryPath)
    {
        var relative = Path.GetRelativePath(libraryPath, fullPath);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? fullPath
            : relative;
    }
}
