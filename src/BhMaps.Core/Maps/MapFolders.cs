using BhMaps.Core.LevelData;

namespace BhMaps.Core.Maps;

/// <summary>Which of the catalog's map folders a game file belongs to. A file under a map's own folder belongs to
/// that map, and a background belongs to every map whose levels name it: backgrounds all sit in the game's one
/// shared Backgrounds folder, so the path alone cannot say whose they are and only the level data can. The counts
/// the owner reads are maps, so both counters ask this rather than reading the first segment of a path (3.0).</summary>
public sealed class MapFolders
{
    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    private readonly HashSet<string> _folders;

    private readonly Dictionary<string, HashSet<string>> _backgrounds;

    private MapFolders(HashSet<string> folders, Dictionary<string, HashSet<string>> backgrounds)
    {
        _folders = folders;
        _backgrounds = backgrounds;
    }

    /// <summary>The lookup these catalog maps make: their folder names, and the game path of every background slot
    /// their levels name against the maps that name it. A slot two maps share belongs to both of them.</summary>
    public static MapFolders Of(IEnumerable<MapEntry> maps)
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var backgrounds = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in maps)
        {
            folders.Add(map.FolderName);
            foreach (var slot in map.BackgroundSlots)
            {
                var path = Normalized(AssetPath.Background(slot));
                if (!backgrounds.TryGetValue(path, out var named))
                {
                    named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    backgrounds[path] = named;
                }

                named.Add(map.FolderName);
            }
        }

        return new MapFolders(folders, backgrounds);
    }

    /// <summary>The map folders this game-relative path counts for, compared ignoring case as Windows does: the
    /// folder it sits in when the catalog knows that folder as a map, plus every map whose levels name it. Empty
    /// for a path that is neither, such as a folder for a map the game no longer has or a background no map uses.</summary>
    public IReadOnlyCollection<string> Of(string gameRelativePath)
    {
        var path = Normalized(gameRelativePath);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folder = path.Split(Separators)[0];
        if (_folders.Contains(folder))
        {
            folders.Add(folder);
        }

        if (_backgrounds.TryGetValue(path, out var named))
        {
            folders.UnionWith(named);
        }

        return folders;
    }

    /// <summary>The path written with one separator, because a path out of the record and one resolved out of the
    /// level data need not have been written with the same one.</summary>
    private static string Normalized(string relativePath) =>
        string.Join(Path.DirectorySeparatorChar, relativePath.Split(Separators));
}
