using BhMaps.Core.Model;

namespace BhMaps.Core.Operations;

/// <summary>One background the user can apply, identified by pack and file name. FromGame marks the game's own copy.</summary>
public sealed record LibraryBackground(string PackName, string FileName, string FullPath, bool FromGame);

/// <summary>Every background the user can choose from: the packs' and the game's own, in one list.</summary>
public static class BackgroundLibrary
{
    public const string GameSourceName = "In game";

    private const string BackgroundsFolder = "Backgrounds";
    private const string JpgExtension = ".jpg";

    /// <summary>Every JPG under any pack's Backgrounds folder, plus the game's current backgrounds
    /// (PackName is GameSourceName and FromGame is true). Sorted by pack then file name.</summary>
    public static IReadOnlyList<LibraryBackground> Build(IReadOnlyList<Pack> packs, GameTree tree)
    {
        var fromPacks = packs.SelectMany(pack =>
            Jpgs(pack.FindFolder(BackgroundsFolder))
                .Select(file => new LibraryBackground(pack.Name, file.Name, file.FullPath, FromGame: false)));
        var fromGame = Jpgs(tree.FindFolder(BackgroundsFolder))
            .Select(file => new LibraryBackground(GameSourceName, file.Name, file.FullPath, FromGame: true));

        return fromPacks
            .Concat(fromGame)
            .OrderBy(b => b.PackName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<GameFile> Jpgs(GameFolder? folder) =>
        folder is null
            ? Array.Empty<GameFile>()
            : folder.Files.Where(f => JpgExtension.Equals(Path.GetExtension(f.Name), StringComparison.OrdinalIgnoreCase));
}
