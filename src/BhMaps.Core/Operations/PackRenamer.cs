using BhMaps.Core.Scanning;

namespace BhMaps.Core.Operations;

/// <summary>3.3 P1: renames one pack by moving its folder under packs\. The new name goes through the same validator
/// and taken-name check as PackCreator; a pack that wraps a mapArt folder moves whole, wrapper and all.</summary>
public static class PackRenamer
{
    /// <summary>True when packs\&lt;oldName&gt; is now packs\&lt;newName&gt;. Sets <paramref name="error"/> to "" on
    /// success. A change of case alone is allowed for the same pack and goes through a temporary name, since
    /// Windows will not move a folder onto a name it already reads as its own.</summary>
    public static bool TryRename(string libraryPath, string oldName, string newName, out string error)
    {
        if (!PackNameValidator.IsValid(newName, out error))
        {
            return false;
        }

        var packsRoot = PackScanner.PacksRoot(libraryPath);
        var folders = Directory.Exists(packsRoot)
            ? new DirectoryInfo(packsRoot).EnumerateDirectories().ToList()
            : [];
        var source = folders.FirstOrDefault(d => d.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            error = $"The pack {oldName} is not there any more.";
            return false;
        }

        var caseOnly = newName.Equals(source.Name, StringComparison.OrdinalIgnoreCase);
        if (newName.Equals(source.Name, StringComparison.Ordinal))
        {
            error = "";
            return true;
        }

        // Compared by name rather than with Directory.Exists, the same way PackCreator does: two packs one case
        // apart are the same pack to everything downstream.
        if (!caseOnly && folders.Any(d => d.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
        {
            error = $"A pack called {newName} already exists.";
            return false;
        }

        var target = Path.Combine(packsRoot, newName);
        try
        {
            if (caseOnly)
            {
                var temporary = Path.Combine(packsRoot, $"{newName}.renaming-{Guid.NewGuid():N}");
                Directory.Move(source.FullName, temporary);
                try
                {
                    Directory.Move(temporary, target);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Put the pack back under the name it had, so a failed rename leaves nothing half done.
                    Directory.Move(temporary, source.FullName);
                    throw;
                }
            }
            else
            {
                Directory.Move(source.FullName, target);
            }
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
