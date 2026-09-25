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
            packs.Add(
                new Pack(names[i]!, discovered[i].ContentRoot, ImageFiles.ScanOneLevel(discovered[i].ContentRoot), true));
        }

        return packs.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static Pack ScanPack(string packPath)
    {
        var info = new DirectoryInfo(packPath);
        return new Pack(info.Name, info.FullName, ImageFiles.ScanOneLevel(packPath));
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
