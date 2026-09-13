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

    /// <summary>Keys this version writes, plus the two 2.0 keys it drops. A dropped key has to stay known, or
    /// Load would carry it into <see cref="AppSettings.Unknown"/> and Save would write it straight back.</summary>
    private static readonly string[] KnownKeys =
    [
        "gamePath", "libraryPath", "firstRunDone", "mapsZoom", "backgroundsRowZoom", "packZoom", "platformsZoom",
        "welcomeDone", "homeZoom", "whileRunning", "backgroundsZoom", "packLastApplied",
        "backgroundsShowPictures", "platformPreviewIsolate", "writeGameThumbnails",
    ];

    public static string DefaultAppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BhMaps");

    /// <summary>Missing file, unreadable file, corrupt file or missing fields all fall back to defaults. Zooms are
    /// clamped, a 2.0 homeZoom loads as MapsZoom, and whileRunning is dropped.</summary>
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

        // 2.0 stored the Maps zoom as homeZoom. The new key wins where both are present; otherwise the old one is
        // read once and written back under the new name, so an upgrade does not reset anyone's column count.
        var mapsZoom = obj["mapsZoom"] is not null ? Int(obj, "mapsZoom", 6) : Int(obj, "homeZoom", 6);

        return new AppSettings(
            Str(obj, "gamePath") is { Length: > 0 } g ? g : AppSettings.DefaultGamePath,
            Str(obj, "libraryPath") is { Length: > 0 } l ? l : AppSettings.DefaultLibraryPath,
            Bool(obj, "firstRunDone"),
            Math.Clamp(mapsZoom, AppSettings.MinZoom, AppSettings.MaxZoom),

            // Addendum B: Backgrounds is a rows page now, so its stored value is a thumbnail height under its own
            // key; the old backgroundsZoom was dropped above.
            Math.Clamp(Int(obj, "backgroundsRowZoom", 2), AppSettings.MinRowZoom, AppSettings.MaxRowZoom),
            Math.Clamp(Int(obj, "packZoom", 5), AppSettings.MinZoom, AppSettings.MaxZoom),
            Bool(obj, "welcomeDone"),
            Math.Clamp(Int(obj, "platformsZoom", 3), AppSettings.MinRowZoom, AppSettings.MaxRowZoom),
            Stamps(obj),
            Bool(obj, "backgroundsShowPictures"),
            Bool(obj, "platformPreviewIsolate"),
            Bool(obj, "writeGameThumbnails"))
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
            ["mapsZoom"] = settings.MapsZoom,
            ["backgroundsRowZoom"] = settings.BackgroundsZoom,
            ["packZoom"] = settings.PackZoom,
            ["platformsZoom"] = settings.PlatformsZoom,
            ["welcomeDone"] = settings.WelcomeDone,
            ["backgroundsShowPictures"] = settings.BackgroundsShowPictures,
            ["platformPreviewIsolate"] = settings.PlatformPreviewIsolate,
            ["writeGameThumbnails"] = settings.WriteGameThumbnails,
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

    /// <summary>Null when the key is absent or holds anything other than a JSON string, so a hand-edited file never throws.</summary>
    private static string? Str(JsonObject obj, string key) =>
        obj[key] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    /// <summary>False when the key is absent or holds anything other than a JSON boolean.</summary>
    private static bool Bool(JsonObject obj, string key) =>
        obj[key] is JsonValue value && value.TryGetValue<bool>(out var b) && b;

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
}
