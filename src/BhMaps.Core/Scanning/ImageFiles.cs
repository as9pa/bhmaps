using BhMaps.Core.Model;

namespace BhMaps.Core.Scanning;

/// <summary>The one place that decides what counts as map art: a .png or .jpg file, one level deep.</summary>
public static class ImageFiles
{
    private static readonly string[] Extensions = [".png", ".jpg"];

    public static bool IsImage(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return Extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Image files directly inside <paramref name="directory"/>, sorted by name. Empty when the directory is missing.</summary>
    public static IReadOnlyList<GameFile> ListImageFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return Array.Empty<GameFile>();
        }

        return new DirectoryInfo(directory)
            .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .Where(f => IsImage(f.Name))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(f => new GameFile(f.Name, f.FullName, f.Length, f.LastWriteTimeUtc.Ticks))
            .ToList();
    }

    public static GameFolder ScanFolder(string directory)
    {
        var info = new DirectoryInfo(directory);
        return new GameFolder(info.Name, info.FullName, ListImageFiles(directory));
    }

    /// <summary>Every immediate subfolder of <paramref name="rootPath"/> as a GameFolder, sorted by name. Empty when the root is missing.</summary>
    public static IReadOnlyList<GameFolder> ScanOneLevel(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            return Array.Empty<GameFolder>();
        }

        return new DirectoryInfo(rootPath)
            .EnumerateDirectories()
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => ScanFolder(d.FullName))
            .ToList();
    }
}
