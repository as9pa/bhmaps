namespace BhMaps.Core.Model;

/// <summary>A folder directly under &lt;library&gt;\packs, mirroring the game tree. Name is the folder name.</summary>
public sealed record Pack(string Name, string FullPath, IReadOnlyList<GameFolder> Folders)
{
    public GameFolder? FindFolder(string name) =>
        Folders.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public int FileCount => Folders.Sum(f => f.Files.Count);
}
