using BhMaps.Core.Model;
using BhMaps.Core.Scanning;

namespace BhMaps.Core.Operations;

/// <summary>3.6 P1: renames one pack by moving its folder under packs\. The new name goes through the same validator
/// and taken-name check as PackCreator; a pack that wraps a mapArt folder moves whole, wrapper and all. A discovered
/// pack renames its own folder (the one holding the mapArt child) in place, through the same path guard as Delete.</summary>
public static class PackRenamer
{
    /// <summary>Rename for a scanned pack. A pack under packs\ goes through the name overload; a discovered pack
    /// moves its FolderPath to a sibling folder called <paramref name="newName"/>.</summary>
    public static bool TryRename(string libraryPath, Pack pack, string newName, out string error) =>
        pack.IsDiscovered
            ? TryRenameDiscoveredFolder(libraryPath, pack.FolderPath, newName, out error)
            : TryRename(libraryPath, pack.Name, newName, out error);

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

        return TryMove(source.FullName, packsRoot, newName, caseOnly, out error);
    }

    /// <summary>The discovered-pack rename. The folder must be a strict child of the library, and must not be
    /// packs\ or any folder holding it, the same guard PackDeleter uses. The new name is refused when a sibling
    /// folder or a pack under packs\ already has it.</summary>
    internal static bool TryRenameDiscoveredFolder(string libraryPath, string packDirectory, string newName, out string error)
    {
        if (!PackNameValidator.IsValid(newName, out error))
        {
            return false;
        }

        var libraryRoot = WithTrailingSeparator(Path.GetFullPath(libraryPath));
        var packsRootPath = Path.GetFullPath(PackScanner.PacksRoot(libraryPath));
        var packsRoot = WithTrailingSeparator(packsRootPath);
        var source = Path.GetFullPath(packDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var sourceWithSeparator = WithTrailingSeparator(source);
        if (!sourceWithSeparator.StartsWith(libraryRoot, StringComparison.OrdinalIgnoreCase)
            || sourceWithSeparator.Equals(libraryRoot, StringComparison.OrdinalIgnoreCase)
            || packsRoot.StartsWith(sourceWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            error = $"Refusing to rename '{source}': it is not a pack folder under '{libraryRoot}'.";
            return false;
        }

        if (!Directory.Exists(source))
        {
            error = $"Pack folder not found: {source}";
            return false;
        }

        var parent = Path.GetDirectoryName(source)!;
        var currentName = Path.GetFileName(source);
        var caseOnly = newName.Equals(currentName, StringComparison.OrdinalIgnoreCase);
        if (newName.Equals(currentName, StringComparison.Ordinal))
        {
            error = "";
            return true;
        }

        // A sibling one case apart is the same folder to Windows, and a pack under packs\ with the new name would
        // show up as a second pack of that name.
        if (!caseOnly
            && (new DirectoryInfo(parent).EnumerateDirectories().Any(d => d.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))
                || Directory.Exists(Path.Combine(packsRootPath, newName))))
        {
            error = $"A pack called {newName} already exists.";
            return false;
        }

        return TryMove(source, parent, newName, caseOnly, out error);
    }

    /// <summary>Moves <paramref name="source"/> to <paramref name="parent"/>\<paramref name="newName"/>. A change of
    /// case alone goes through a temporary name and puts the folder back if the second move fails.</summary>
    private static bool TryMove(string source, string parent, string newName, bool caseOnly, out string error)
    {
        var target = Path.Combine(parent, newName);
        try
        {
            if (caseOnly)
            {
                var temporary = Path.Combine(parent, $"{newName}.renaming-{Guid.NewGuid():N}");
                Directory.Move(source, temporary);
                try
                {
                    Directory.Move(temporary, target);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Put the pack back under the name it had, so a failed rename leaves nothing half done.
                    Directory.Move(temporary, source);
                    throw;
                }
            }
            else
            {
                Directory.Move(source, target);
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

    private static string WithTrailingSeparator(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
}
