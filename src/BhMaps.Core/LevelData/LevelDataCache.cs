using System.Text.Json;
using BhMaps.Core.Storage;

namespace BhMaps.Core.LevelData;

/// <summary>Keeps the parsed model in &lt;appDataDir&gt;\leveldata.json next to the stamp it was read from, so an
/// unchanged game skips the scan and a game update re-reads (spec 3.5).</summary>
public static class LevelDataCache
{
    /// <summary>Bumped whenever a parser change makes a model written by an older build wrong. A cache stamped
    /// with any other version is a cache miss, because the stamp alone only notices the game changing.</summary>
    public const int SchemaVersion = 2;

    /// <summary>Not indented: this file is a few megabytes.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>JSON shape of the cache file. A file written before the version existed reads back as 0.</summary>
    private sealed record CacheFile(int Version, LevelDataStamp Stamp, LevelDataModel Model);

    public static string PathFor(string appDataDir) => Path.Combine(appDataDir, "leveldata.json");

    /// <summary>A missing, unreadable, corrupt or older-schema cache loads as null, which is just a cache
    /// miss.</summary>
    public static (LevelDataModel Model, LevelDataStamp Stamp)? Load(string cachePath)
    {
        try
        {
            var file = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(cachePath), JsonOptions);
            return file is { Version: SchemaVersion, Model: not null, Stamp.Files: not null }
                ? (file.Model, file.Stamp)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            // Spec 3.5: a cache the app cannot read is a cache miss, and the reader re-reads the game.
            return null;
        }
    }

    public static void Save(string cachePath, LevelDataModel model, LevelDataStamp stamp) =>
        AtomicFile.WriteAllText(
            cachePath, JsonSerializer.Serialize(new CacheFile(SchemaVersion, stamp, model), JsonOptions));
}
