using BhMaps.Core.Settings;
using Microsoft.Win32;

namespace BhMaps.App.Services;

/// <summary>Finds the game's mapArt folder through Steam for the welcome window (spec 7.7). A missing registry
/// key, an unreadable libraryfolders.vdf or a path this user cannot see all answer null; nothing here throws.</summary>
public static class SteamLocator
{
    private const string SteamKey = @"Software\Valve\Steam";

    /// <summary>Brawlhalla's art folder inside any one Steam library folder.</summary>
    private const string GameRelativePath = @"steamapps\common\Brawlhalla\mapArt";

    /// <summary>The first candidate folder that exists, or null when none does.</summary>
    public static string? FindMapArt()
    {
        try
        {
            foreach (var candidate in Candidates())
            {
                if (Directory.Exists(candidate))
                {
                    // The registry writes SteamPath with forward slashes, so normalise before this reaches a field.
                    return Path.GetFullPath(candidate);
                }
            }
        }
        catch (Exception)
        {
            // Detection is a convenience: any failure at all leaves the field for the user to fill in. Catching
            // by type would mean listing everything the registry, the file system and path handling can raise.
            return null;
        }

        return null;
    }

    /// <summary>The usual install first, then Brawlhalla under the Steam folder itself, then under each library
    /// folder Steam lists. Lazy, so the registry is not read when the first candidate already exists.</summary>
    private static IEnumerable<string> Candidates()
    {
        yield return AppSettings.DefaultGamePath;

        if (SteamPath() is not { Length: > 0 } steam)
        {
            yield break;
        }

        yield return Path.Combine(steam, GameRelativePath);
        foreach (var library in LibraryFolders(steam))
        {
            yield return Path.Combine(library, GameRelativePath);
        }
    }

    private static string? SteamPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SteamKey);
        return key?.GetValue("SteamPath") as string;
    }

    /// <summary>Every "path" value in steamapps\libraryfolders.vdf, by a plain text scan rather than a parse: the
    /// file is a nest of quoted key/value lines and only those values matter. The values escape their backslashes.</summary>
    private static IEnumerable<string> LibraryFolders(string steamPath)
    {
        var vdf = Path.Combine(steamPath, @"steamapps\libraryfolders.vdf");
        if (!File.Exists(vdf))
        {
            yield break;
        }

        foreach (var line in File.ReadLines(vdf))
        {
            // A key/value line splits into indent, key, gap, value and tail, so the value is always the fourth piece.
            var parts = line.Split('"');
            if (parts.Length >= 5 && parts[1] == "path")
            {
                yield return parts[3].Replace(@"\\", @"\");
            }
        }
    }
}
