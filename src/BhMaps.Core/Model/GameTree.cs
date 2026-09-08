namespace BhMaps.Core.Model;

/// <summary>One image file as seen on disk. MtimeTicks is LastWriteTimeUtc.Ticks.</summary>
public sealed record GameFile(string Name, string FullPath, long Size, long MtimeTicks);

/// <summary>One immediate subfolder of mapArt (or of a pack), with its image files sorted by name.</summary>
public sealed record GameFolder(string Name, string FullPath, IReadOnlyList<GameFile> Files)
{
    public GameFile? FindFile(string name) =>
        Files.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>The scanned mapArt folder. Folders are sorted by name, ordinal ignore case.</summary>
public sealed record GameTree(string RootPath, IReadOnlyList<GameFolder> Folders)
{
    public GameFolder? FindFolder(string name) =>
        Folders.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static GameTree Empty(string rootPath) => new(rootPath, Array.Empty<GameFolder>());
}
