using BhMaps.Core.Model;

namespace BhMaps.Core.Scanning;

public static class PackScanner
{
    /// <summary>The child folder that marks a discovered pack: its parent is the pack, it is the content root.</summary>
    public const string MapArtFolderName = "mapArt";

    /// <summary>How deep the discovery walk looks, library children being depth 1. A mapArt folder deeper than
    /// this is not seen.</summary>
    public const int MaxDiscoveryDepth = 3;

    public static string PacksRoot(string libraryPath) => Path.Combine(libraryPath, "packs");

    /// <summary>Every folder directly under &lt;library&gt;\packs, plus every discovered pack elsewhere in the
    /// library (see Discover), ordered by name. Names are unique ignoring case: packs\ keeps its own names and a
    /// discovered pack that collides takes " (1)", " (2)" and so on. Missing library or packs folder yields an
    /// empty list for that half.</summary>
    public static IReadOnlyList<Pack> ScanAll(string libraryPath)
    {
        var root = PacksRoot(libraryPath);
        var packs = Directory.Exists(root)
            ? new DirectoryInfo(root).EnumerateDirectories().Select(d => ScanPack(d.FullName)).ToList()
            : new List<Pack>();

        var taken = packs.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var discovered = Discover(libraryPath)
            .OrderBy(d => d.ContentRoot, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Plain names first, so a folder really called "Summer (1)" keeps that name rather than losing it to a
        // suffixed "Summer" that happens to sort ahead of it. Then the colliders count up past every name taken.
        var names = new string?[discovered.Count];
        for (var i = 0; i < discovered.Count; i++)
        {
            if (taken.Add(discovered[i].Name))
            {
                names[i] = discovered[i].Name;
            }
        }

        for (var i = 0; i < discovered.Count; i++)
        {
            if (names[i] is not null)
            {
                continue;
            }

            var n = 1;
            while (!taken.Add($"{discovered[i].Name} ({n})"))
            {
                n++;
            }

            names[i] = $"{discovered[i].Name} ({n})";
        }

        for (var i = 0; i < discovered.Count; i++)
        {
            // FolderPath is the folder that holds the mapArt: the one the user named and would open or drag.
            packs.Add(
                new Pack(names[i]!, discovered[i].ContentRoot, ImageFiles.ScanOneLevel(discovered[i].ContentRoot), true)
                {
                    FolderPath = Path.GetDirectoryName(discovered[i].ContentRoot) ?? discovered[i].ContentRoot,
                });
        }

        return packs.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>3.6 P2: the folder name a pack folder may wrap its content in, matched in any case.</summary>
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

    /// <summary>Folders outside packs\ that hold a mapArt child (any case), within MaxDiscoveryDepth of the
    /// library. The walk does not go below a pack it found, skips the top-level packs folder (ScanAll reads that
    /// one itself), skips junctions and symlinks, and passes over a folder it cannot read.</summary>
    private static List<(string Name, string ContentRoot)> Discover(string libraryPath)
    {
        var found = new List<(string, string)>();
        if (!Directory.Exists(libraryPath))
        {
            return found;
        }

        var pending = new Stack<(DirectoryInfo Folder, int Depth)>();
        foreach (var child in Children(new DirectoryInfo(libraryPath)))
        {
            if (!child.Name.Equals("packs", StringComparison.OrdinalIgnoreCase))
            {
                pending.Push((child, 1));
            }
        }

        while (pending.Count > 0)
        {
            var (folder, depth) = pending.Pop();
            var children = Children(folder);
            var mapArt = depth + 1 <= MaxDiscoveryDepth
                ? children.FirstOrDefault(c => c.Name.Equals(MapArtFolderName, StringComparison.OrdinalIgnoreCase))
                : null;
            if (mapArt is not null)
            {
                found.Add((folder.Name, mapArt.FullName));
                continue;
            }

            if (depth + 1 < MaxDiscoveryDepth)
            {
                foreach (var child in children)
                {
                    pending.Push((child, depth + 1));
                }
            }
        }

        return found;
    }

    /// <summary>The folder's subfolders that are not reparse points; none when it cannot be read.</summary>
    private static List<DirectoryInfo> Children(DirectoryInfo folder)
    {
        try
        {
            return folder
                .EnumerateDirectories()
                .Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0)
                .ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
        {
            return new List<DirectoryInfo>();
        }
    }
}
