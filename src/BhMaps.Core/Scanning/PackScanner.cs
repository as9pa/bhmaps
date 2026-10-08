using BhMaps.Core.Model;

namespace BhMaps.Core.Scanning;

public static class PackScanner
{
    public static string PacksRoot(string libraryPath) => Path.Combine(libraryPath, "packs");

    /// <summary>Every folder directly under &lt;library&gt;\packs. Missing library or packs folder yields an empty list.</summary>
    public static IReadOnlyList<Pack> ScanAll(string libraryPath)
    {
        var root = PacksRoot(libraryPath);
        if (!Directory.Exists(root))
        {
            return Array.Empty<Pack>();
        }

        return new DirectoryInfo(root)
            .EnumerateDirectories()
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => ScanPack(d.FullName))
            .ToList();
    }

    /// <summary>3.3 P2: the folder name a pack folder may wrap its content in, matched in any case.</summary>
    public const string MapArtFolder = "mapArt";

    /// <summary>The folder a pack's files live in: packFolder\mapArt when packFolder has that child (any case),
    /// else packFolder itself.</summary>
    public static string ContentRoot(string packFolder)
    {
        try
        {
            if (Directory.Exists(packFolder)
                && new DirectoryInfo(packFolder).EnumerateDirectories()
                    .FirstOrDefault(d => d.Name.Equals(MapArtFolder, StringComparison.OrdinalIgnoreCase)) is { } child)
            {
                return child.FullName;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be listed has no wrapper we can see; it reads as a plain pack.
        }

        return packFolder;
    }

    /// <summary>The content root of the pack with this name: packs\&lt;name&gt;\mapArt when that folder exists,
    /// else packs\&lt;name&gt;, so every write into a wrapped pack lands where the game will look.</summary>
    public static string PackRootFor(string libraryPath, string packName) =>
        ContentRoot(Path.Combine(PacksRoot(libraryPath), packName));

    /// <summary>The pack in the folder packPath, named after that folder and read from its content root.</summary>
    public static Pack ScanPack(string packPath)
    {
        var info = new DirectoryInfo(packPath);
        var contentRoot = ContentRoot(info.FullName);
        return new Pack(info.Name, contentRoot, ImageFiles.ScanOneLevel(contentRoot)) { FolderPath = info.FullName };
    }
}
