namespace BhMaps.Core.Model;

/// <summary>A folder directly under &lt;library&gt;\packs, mirroring the game tree. Name is the folder name.
/// FullPath is the content root every file is read from and written to: the folder itself, or its mapArt child
/// when it wraps one (3.3 P2).</summary>
public sealed record Pack(string Name, string FullPath, IReadOnlyList<GameFolder> Folders)
{
    /// <summary>The folder under packs\ itself, which is what Delete, Export's name, Open folder, Rename and
    /// Duplicate act on. The same as FullPath for a pack that does not wrap a mapArt folder.</summary>
    public string FolderPath { get; init; } = FullPath;

    public GameFolder? FindFolder(string name) =>
        Folders.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public int FileCount => Folders.Sum(f => f.Files.Count);

    /// <summary>"Folder\file" for every file in the pack, in scan order.</summary>
    public IReadOnlyList<string> RelativePaths =>
        Folders.SelectMany(f => f.Files.Select(x => Path.Combine(f.Name, x.Name))).ToList();
}
