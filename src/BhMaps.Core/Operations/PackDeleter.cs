using BhMaps.Core.Scanning;

namespace BhMaps.Core.Operations;

/// <summary>Deletes one pack folder. Two guards: the name must be a valid pack name, and the resolved path must be a strict child of packs\.</summary>
public static class PackDeleter
{
    /// <summary>Null on success; otherwise the reason nothing was deleted.</summary>
    public static string? Delete(string libraryPath, string packName)
    {
        if (!PackNameValidator.IsValid(packName, out var nameError))
        {
            return nameError;
        }

        return DeleteFolder(libraryPath, Path.Combine(PackScanner.PacksRoot(libraryPath), packName));
    }

    /// <summary>The path guard on its own. Internal so tests can reach it with paths a valid pack name can never produce.</summary>
    internal static string? DeleteFolder(string libraryPath, string packDirectory)
    {
        var packsRoot = WithTrailingSeparator(Path.GetFullPath(PackScanner.PacksRoot(libraryPath)));
        var target = Path.GetFullPath(packDirectory);
        var targetWithSeparator = WithTrailingSeparator(target);
        if (!targetWithSeparator.StartsWith(packsRoot, StringComparison.OrdinalIgnoreCase)
            || targetWithSeparator.Equals(packsRoot, StringComparison.OrdinalIgnoreCase))
        {
            return $"Refusing to delete '{target}': it is not a pack folder under '{packsRoot}'.";
        }

        if (!Directory.Exists(target))
        {
            return $"Pack folder not found: {target}";
        }

        try
        {
            Directory.Delete(target, recursive: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    private static string WithTrailingSeparator(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
}
