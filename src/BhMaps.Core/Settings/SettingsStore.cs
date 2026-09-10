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

    /// <summary>Keys this version writes itself. Everything else in the file is carried in <see cref="AppSettings.Unknown"/>.</summary>
    private static readonly string[] KnownKeys =
        ["gamePath", "libraryPath", "firstRunDone", "homeZoom", "backgroundsZoom", "whileRunning", "welcomeDone"];

    public static string DefaultAppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BhMaps");

    /// <summary>Missing file, unreadable file, corrupt file, or missing fields all fall back to defaults. Zooms are clamped and any whileRunning value other than "live" loads as "restart".</summary>
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

        return new AppSettings(
            Str(obj, "gamePath") is { Length: > 0 } g ? g : AppSettings.DefaultGamePath,
            Str(obj, "libraryPath") is { Length: > 0 } l ? l : AppSettings.DefaultLibraryPath,
            Bool(obj, "firstRunDone"),
            Math.Clamp(Int(obj, "homeZoom", 3), 2, 5),
            Math.Clamp(Int(obj, "backgroundsZoom", 4), 3, 8),
            Str(obj, "whileRunning") == AppSettings.LiveWhileRunning
                ? AppSettings.LiveWhileRunning
                : AppSettings.RestartWhileRunning,
            Bool(obj, "welcomeDone"))
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
            ["homeZoom"] = settings.HomeZoom,
            ["backgroundsZoom"] = settings.BackgroundsZoom,
            ["whileRunning"] = settings.WhileRunning,
            ["welcomeDone"] = settings.WelcomeDone,
        };
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
}
