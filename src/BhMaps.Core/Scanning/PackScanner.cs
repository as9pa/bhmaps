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

    public static Pack ScanPack(string packPath)
    {
        var info = new DirectoryInfo(packPath);
        return new Pack(info.Name, info.FullName, ImageFiles.ScanOneLevel(packPath));
    }
}
