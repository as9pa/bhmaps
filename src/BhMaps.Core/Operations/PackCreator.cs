using BhMaps.Core.Scanning;

namespace BhMaps.Core.Operations;

/// <summary>Spec 13: makes an empty pack folder under packs\. The name goes through the same validator the
/// editors' "New pack..." rows use, and a name another pack already holds is refused.</summary>
public static class PackCreator
{
    /// <summary>True when packs\&lt;name&gt; was created. Sets <paramref name="error"/> to "" on success.</summary>
    public static bool TryCreate(string libraryPath, string name, out string error)
    {
        if (!PackNameValidator.IsValid(name, out error))
        {
            return false;
        }

        // Compared here rather than with Directory.Exists so the answer does not depend on how the file system
        // spells folder names: two packs one case apart are the same pack to everything downstream.
        var packsRoot = PackScanner.PacksRoot(libraryPath);
        if (Directory.Exists(packsRoot)
            && new DirectoryInfo(packsRoot).EnumerateDirectories()
                .Any(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            error = $"A pack called {name} already exists.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.Combine(packsRoot, name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }

        error = "";
        return true;
    }
}
