namespace BhMaps.Core.Model;

/// <summary>A pack, mirroring the game tree. Either a folder directly under &lt;library&gt;\packs, or a discovered
/// one: a folder elsewhere in the library holding a mapArt child. Name is the folder name, made unique by the
/// scanner. FullPath is the content root, the folder whose children are the game folders: the pack folder itself
/// under packs\, its mapArt folder for a discovered pack or for a pack under packs\ that wraps one (3.6 P2).
/// Every read of a pack's files goes through FullPath.
/// IsDiscovered marks a pack outside packs\, which is read-only: it can be viewed and applied, never changed.</summary>
public sealed record Pack(string Name, string FullPath, IReadOnlyList<GameFolder> Folders, bool IsDiscovered = false)
{
    /// <summary>The pack's own folder, which is what Delete, Export's name, Open folder, Rename and Duplicate act
    /// on: the folder under packs\, or the folder holding the mapArt of a discovered pack. The same as FullPath
    /// for a pack that does not wrap a mapArt folder.</summary>
    public string FolderPath { get; init; } = FullPath;

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
