using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BhMaps.Core.Storage;

namespace BhMaps.Core.Settings;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>A hand-edited file may spell a key any way at all, so "GamePath" reads back as gamePath rather
    /// than as a property this version does not know.</summary>
    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Keys this version writes, plus the older keys it drops. A dropped key has to stay known, or
    /// Load would carry it into <see cref="AppSettings.Unknown"/> and Save would write it straight back.</summary>
    private static readonly string[] KnownKeys =
    [
        "gamePath", "libraryPath", "firstRunDone", "mapsTileSize", "backgroundsTileSize", "packTileSize",
        "platformsTileSize",
        // The 2.1 to 2.8 zoom keys. 3.0 reads each one once, as the tile size nearest to it, and stops writing
        // them; like the two keys below they have to stay known, or Save would carry them back as unknown.
        "mapsZoom", "backgroundsRowZoom", "packZoom", "platformsZoom",
        "welcomeDone", "homeZoom", "whileRunning", "backgroundsZoom", "packLastApplied",
        "backgroundsShowPictures", "platformPreviewIsolate",
        // 3.0 writes the game's map-select thumbnails always, so the 2.5 switch is read no more and written no
        // more; it stays known so an old file's copy of it is dropped rather than carried back.
        "writeGameThumbnails",
        "checkForUpdates", "lastUpdateCheck", "dismissedUpdate", "hiddenPacks",
    ];

    public static string DefaultAppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BhMaps");

    /// <summary>Missing file, unreadable file, corrupt file or missing fields all fall back to defaults. An old
    /// zoom key migrates once into the page's tile size, a 2.0 homeZoom loads as the Maps size, and whileRunning
    /// is dropped.</summary>
    public static AppSettings Load(string settingsPath)
    {
        JsonObject? obj = null;
        if (File.Exists(settingsPath))
        {
            try
            {
                obj = JsonNode.Parse(File.ReadAllText(settingsPath), NodeOptions) as JsonObject;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                obj = null;
            }
        }

        if (obj is null)
        {
            return AppSettings.Default;
        }

        var unknown = new JsonObject();
        foreach (var (key, value) in obj)
        {
            // Ignore case, matching the read below: a key Save is about to write itself must not also be carried
            // over as an unknown one, or the file ends up with both spellings of it.
            if (!KnownKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                unknown[key] = value?.DeepClone();
            }
        }

        // Spec 8: there is no restart flow any more, so an old whileRunning is read once, reported and dropped.
        if (obj["whileRunning"] is not null)
        {
            System.Diagnostics.Trace.WriteLine(
                "BhMaps settings: whileRunning is no longer used and was dropped; writes are always live.");
        }

        // Addendum B, q1: Backgrounds is a rows page whose zoom is a thumbnail height under backgroundsRowZoom.
        // The old backgroundsZoom was a 2.0 tile size and then a 2.1 column count; read as a height it would put
        // every upgrader on the largest rows, so it is reported once and dropped.
        if (obj["backgroundsZoom"] is not null)
        {
            System.Diagnostics.Trace.WriteLine(
                "BhMaps settings: backgroundsZoom is no longer used and was dropped; the Backgrounds rows start dense.");
        }

        // 2.0 stored the Maps zoom as homeZoom and 2.1 as mapsZoom. The new key wins where both are present;
        // otherwise the old one is read once as the tile size nearest to it, so an upgrade does not drop anyone
        // back to the middle size.
        var mapsZoom = obj["mapsZoom"] is not null ? obj["mapsZoom"] : obj["homeZoom"];

        return new AppSettings(
            Str(obj, "gamePath") is { Length: > 0 } g ? g : AppSettings.DefaultGamePath,
            Str(obj, "libraryPath") is { Length: > 0 } l ? l : AppSettings.DefaultLibraryPath,
            Bool(obj, "firstRunDone"),
            Tile(obj, "mapsTileSize", mapsZoom, FromGridZoom),

            // Addendum B: Backgrounds is a rows page, so its 2.8 value was a thumbnail height under its own key.
            Tile(obj, "backgroundsTileSize", obj["backgroundsRowZoom"], FromRowZoom),
            Tile(obj, "packTileSize", obj["packZoom"], FromGridZoom),
            Bool(obj, "welcomeDone"),
            Tile(obj, "platformsTileSize", obj["platformsZoom"], FromRowZoom),
            Stamps(obj),
            Bool(obj, "backgroundsShowPictures"),
            Bool(obj, "platformPreviewIsolate"),
            Bool(obj, "checkForUpdates", fallback: true),
            Time(obj, "lastUpdateCheck"),
            Str(obj, "dismissedUpdate") is { Length: > 0 } tag ? tag : null,
            Hidden(obj))
        {
            Unknown = unknown.Count == 0 ? null : unknown,
        };
    }

    /// <summary>Writes the known keys in a fixed order, then any properties a future version added back untouched.</summary>
    public static void Save(string settingsPath, AppSettings settings)
    {
        var obj = new JsonObject
        {
            ["gamePath"] = settings.GamePath,
            ["libraryPath"] = settings.LibraryPath,
            ["firstRunDone"] = settings.FirstRunDone,
            ["mapsTileSize"] = Name(settings.MapsTileSize),
            ["backgroundsTileSize"] = Name(settings.BackgroundsTileSize),
            ["packTileSize"] = Name(settings.PackTileSize),
            ["platformsTileSize"] = Name(settings.PlatformsTileSize),
            ["welcomeDone"] = settings.WelcomeDone,
            ["backgroundsShowPictures"] = settings.BackgroundsShowPictures,
            ["platformPreviewIsolate"] = settings.PlatformPreviewIsolate,
            ["checkForUpdates"] = settings.CheckForUpdates,
            ["lastUpdateCheck"] = settings.LastUpdateCheck?.ToString("o", CultureInfo.InvariantCulture),
            ["dismissedUpdate"] = settings.DismissedUpdate,
        };

        // Spec 8: a library nobody has applied from writes no key at all, rather than an empty object nobody reads.
        if (settings.LastApplied.Count > 0)
        {
            var stamps = new JsonObject();
            foreach (var (pack, stamp) in settings.LastApplied)
            {
                stamps[pack] = stamp.ToString("o", CultureInfo.InvariantCulture);
            }

            obj["packLastApplied"] = stamps;
        }

        // 2.8: a library with nothing hidden writes no key at all, the same way the stamps do.
        if (settings.HiddenPacks.Count > 0)
        {
            var hidden = new JsonArray();
            foreach (var pack in settings.HiddenPacks)
            {
                hidden.Add(pack);
            }

            obj["hiddenPacks"] = hidden;
        }

        if (settings.Unknown is { } extra)
        {
            foreach (var (key, value) in extra)
            {
                obj[key] = value?.DeepClone();
            }
        }

        AtomicFile.WriteAllText(settingsPath, obj.ToJsonString(JsonOptions));
    }

    /// <summary>The game path must exist, be readable, and contain at least one subfolder.</summary>
    public static bool ValidateGamePath(string? path, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Game path is empty.";
            return false;
        }

        if (!Directory.Exists(path))
        {
            error = $"Game path does not exist: {path}";
            return false;
        }

        bool hasMapFolder;
        try
        {
            hasMapFolder = Directory.EnumerateDirectories(path).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"Game folder cannot be read: {ex.Message}";
            return false;
        }

        if (!hasMapFolder)
        {
            error = $"Game path has no map folders inside it: {path}";
            return false;
        }

        return true;
    }

    /// <summary>The library path must be absolute and either exist or have an existing ancestor so it can be created.</summary>
    public static bool ValidateLibraryPath(string? path, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Library path is empty.";
            return false;
        }

        if (!Path.IsPathRooted(path))
        {
            error = "Library path must be an absolute path.";
            return false;
        }

        if (Directory.Exists(path))
        {
            return true;
        }

        var ancestor = Path.GetDirectoryName(Path.GetFullPath(path));
        while (ancestor is not null && !Directory.Exists(ancestor))
        {
            ancestor = Path.GetDirectoryName(ancestor);
        }

        if (ancestor is null)
        {
            error = $"No part of the library path exists, so it cannot be created: {path}";
            return false;
        }

        return true;
    }

    /// <summary>3.0's tile size, as large, medium or small. A value this version does not know, in any case at
    /// all, reads as Medium rather than failing the file. Where the key is absent and the page's old zoom key is
    /// there, <paramref name="zoom"/> is migrated once; the old key is dropped by the next Save.</summary>
    private static TileSize Tile(JsonObject obj, string key, JsonNode? zoom, Func<int, TileSize> fromZoom)
    {
        if (Str(obj, key) is { Length: > 0 } name)
        {
            return name.ToLowerInvariant() switch
            {
                "large" => TileSize.Large,
                "small" => TileSize.Small,
                _ => TileSize.Medium,
            };
        }

        return zoom is JsonValue value && value.TryGetValue<int>(out var steps) ? fromZoom(steps) : TileSize.Medium;
    }

    /// <summary>A 2 to 10 grid zoom as a tile size. The old step was a column count, so the small end of it is
    /// the many-small-cards end.</summary>
    private static TileSize FromGridZoom(int zoom) => zoom <= 4 ? TileSize.Large : zoom <= 7 ? TileSize.Medium : TileSize.Small;

    /// <summary>A 1 to 5 rows zoom as a tile size. The old step was a thumbnail height, so it runs the other way
    /// round from a grid's.</summary>
    private static TileSize FromRowZoom(int zoom) => zoom <= 3 ? TileSize.Small : zoom == 4 ? TileSize.Medium : TileSize.Large;

    /// <summary>The lowercase name the file carries, so a hand-edited settings file reads the way it looks.</summary>
    private static string Name(TileSize size) => size.ToString().ToLowerInvariant();

    /// <summary>Null when the key is absent or holds anything other than a JSON string, so a hand-edited file never throws.</summary>
    private static string? Str(JsonObject obj, string key) =>
        obj[key] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    /// <summary>False when the key is absent or holds anything other than a JSON boolean.</summary>
    private static bool Bool(JsonObject obj, string key) =>
        obj[key] is JsonValue value && value.TryGetValue<bool>(out var b) && b;

    /// <summary><paramref name="fallback"/> when the key is absent or holds anything other than a JSON boolean,
    /// so a setting that is on unless it was deliberately turned off stays on through a hand-edited file.</summary>
    private static bool Bool(JsonObject obj, string key, bool fallback) =>
        obj[key] is JsonValue value && value.TryGetValue<bool>(out var b) ? b : fallback;

    /// <summary>A round-trip timestamp, or null when the key is absent, null, or not a date this version reads.</summary>
    private static DateTimeOffset? Time(JsonObject obj, string key) =>
        obj[key]?.GetValueKind() == JsonValueKind.String
        && DateTimeOffset.TryParse(
            obj[key]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stamp)
            ? stamp
            : null;

    /// <summary><paramref name="fallback"/> when the key is absent or holds anything other than a JSON integer.</summary>
    private static int Int(JsonObject obj, string key, int fallback) =>
        obj[key] is JsonValue value && value.TryGetValue<int>(out var i) ? i : fallback;

    /// <summary>Spec 8's last-applied stamps, as { "&lt;pack&gt;": "&lt;round-trip date&gt;" }. An entry whose value is not
    /// a date this version can read is skipped rather than failing the whole file: a hand-edited stamp costs its
    /// own pack its place in the order and nothing else.</summary>
    private static IReadOnlyDictionary<string, DateTimeOffset>? Stamps(JsonObject obj)
    {
        if (obj["packLastApplied"] is not JsonObject stamps)
        {
            return null;
        }

        var read = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in stamps)
        {
            if (value?.GetValueKind() == JsonValueKind.String
                && DateTimeOffset.TryParse(
                    value.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stamp))
            {
                read[key] = stamp;
            }
        }

        return read;
    }

    /// <summary>2.8's hidden packs, as [ "&lt;pack&gt;", ... ]. An entry that is not a string, and a name that is
    /// empty, is skipped rather than failing the whole file: a hand-edited list costs its own line and nothing
    /// else.</summary>
    private static IReadOnlyList<string>? Hidden(JsonObject obj)
    {
        if (obj["hiddenPacks"] is not JsonArray names)
        {
            return null;
        }

        var read = new List<string>();
        foreach (var name in names)
        {
            if (name?.GetValueKind() == JsonValueKind.String && name.GetValue<string>() is { Length: > 0 } pack)
            {
                read.Add(pack);
            }
        }

        return read;
    }
}
