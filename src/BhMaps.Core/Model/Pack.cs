namespace BhMaps.Core.Model;

/// <summary>A pack, mirroring the game tree. Either a folder directly under &lt;library&gt;\packs, or a discovered
/// one: a folder elsewhere in the library holding a mapArt child. Name is the folder name, made unique by the
/// scanner. FullPath is the content root, the folder whose children are the game folders: the pack folder itself
/// under packs\, its mapArt folder for a discovered pack. Every read of a pack's files goes through FullPath.
/// IsDiscovered marks a pack outside packs\, which is read-only: it can be viewed and applied, never changed.</summary>
public sealed record Pack(string Name, string FullPath, IReadOnlyList<GameFolder> Folders, bool IsDiscovered = false)
{
    public GameFolder? FindFolder(string name) =>
        Folders.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public int FileCount => Folders.Sum(f => f.Files.Count);

    /// <summary>"Folder\file" for every file in the pack, in scan order.</summary>
    public IReadOnlyList<string> RelativePaths =>
        Folders.SelectMany(f => f.Files.Select(x => Path.Combine(f.Name, x.Name))).ToList();

    /// <summary>The refusal every operation that would change a discovered pack's folder returns.</summary>
    public static string ReadOnlyMessage(Pack pack) =>
        $"'{pack.Name}' is outside the packs folder, so it is read-only. It can be viewed and applied, not changed.";
}
