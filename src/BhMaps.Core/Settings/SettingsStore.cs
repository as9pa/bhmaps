using System.Text.Json;
using System.Text.Json.Serialization;
using BhMaps.Core.Storage;

namespace BhMaps.Core.Settings;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static string DefaultAppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BhMaps");

    /// <summary>Missing file, unreadable file, corrupt file, or missing fields all fall back to defaults.</summary>
    public static AppSettings Load(string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            return AppSettings.Default;
        }

        SettingsFile? file;
        try
        {
            file = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(settingsPath), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            file = null;
        }

        if (file is null)
        {
            return AppSettings.Default;
        }

        return new AppSettings(
            string.IsNullOrWhiteSpace(file.GamePath) ? AppSettings.DefaultGamePath : file.GamePath,
            string.IsNullOrWhiteSpace(file.LibraryPath) ? AppSettings.DefaultLibraryPath : file.LibraryPath,
            file.FirstRunDone);
    }

    public static void Save(string settingsPath, AppSettings settings)
    {
        var file = new SettingsFile
        {
            GamePath = settings.GamePath,
            LibraryPath = settings.LibraryPath,
            FirstRunDone = settings.FirstRunDone,
        };
        AtomicFile.WriteAllText(settingsPath, JsonSerializer.Serialize(file, JsonOptions));
    }

    /// <summary>The game path must exist and contain at least one subfolder.</summary>
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

        if (!Directory.EnumerateDirectories(path).Any())
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

    /// <summary>On-disk shape. Nullable so a hand-edited file with missing keys still loads.</summary>
    private sealed class SettingsFile
    {
        [JsonPropertyName("gamePath")]
        public string? GamePath { get; set; }

        [JsonPropertyName("libraryPath")]
        public string? LibraryPath { get; set; }

        [JsonPropertyName("firstRunDone")]
        public bool FirstRunDone { get; set; }
    }
}
