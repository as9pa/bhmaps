using BhMaps.Core.Hashing;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;

namespace BhMaps.Core.Operations;

/// <summary>One picture that is nobody's map art: a pack file under a name no map's background slot uses, or a
/// background in the game that no pack accounts for. One picture is one entry however many copies of it exist,
/// because the user thinks in pictures and the app should not show the same one four times (spec 4).</summary>
public sealed record CustomPicture(
    string Hash,
    string DisplayName,
    IReadOnlyList<string> LibraryPaths,
    IReadOnlyList<string> InGameSlots,
    string? PackName);

/// <summary>Builds the custom picture list once per scan, off the UI thread, through the scan's own hash cache.</summary>
public static class CustomPictureLibrary
{
    private const string BackgroundsFolder = "Backgrounds";
    private const string JpgExtension = ".jpg";

    public static IReadOnlyList<CustomPicture> Build(
        IReadOnlyList<Pack> packs, GameTree tree, MapCatalog catalog, HashCache hashes)
    {
        var slotNames = SlotNames(catalog);
        var byHash = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        // Every pack hash, slot-named or not: a game file matching any of them is a pack's art, not a picture of
        // the user's. Collected in the same pass that picks the custom ones out.
        var packHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in packs)
        {
            foreach (var file in Jpgs(pack.FindFolder(BackgroundsFolder)))
            {
                if (Hash(hashes, file) is not { } hash)
                {
                    continue;
                }

                packHashes.Add(hash);
                if (!slotNames.Contains(file.Name))
                {
                    Of(byHash, hash).Library.Add((pack.Name, file.FullPath));
                }
            }
        }

        foreach (var file in Jpgs(tree.FindFolder(BackgroundsFolder)))
        {
            if (Hash(hashes, file) is not { } hash)
            {
                continue;
            }

            // A game file matching a picture already found joins it, which is how one tile learns every slot it
            // occupies. Anything else matching a pack is that pack's art for some map and belongs to no picture.
            if (!byHash.ContainsKey(hash) && packHashes.Contains(hash))
            {
                continue;
            }

            Of(byHash, hash).GameNames.Add(file.Name);
        }

        return byHash
            .Select(pair => Picture(pair.Key, pair.Value, slotNames))
            .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Hash, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Spec 3.1: what to call the picture the game is showing in <paramref name="slot"/>. The library's
    /// own name for it, or the slot's file name when no picture matches, which is a picture the last scan did not
    /// see. One rule in one place, so a map's card and its panel can never name the same picture differently.</summary>
    public static string NameFor(IReadOnlyList<CustomPicture> pictures, string slot)
    {
        var fileName = Path.GetFileName(AssetPath.Background(slot));
        return pictures
                   .FirstOrDefault(p => p.InGameSlots.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                   ?.DisplayName
               ?? fileName;
    }

    /// <summary>Every name a map's background slot resolves to, so a slot borrowed from a theme folder through
    /// "../" is compared on the file name the pack would actually hold.</summary>
    private static HashSet<string> SlotNames(MapCatalog catalog)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in catalog.Maps.SelectMany(m => m.BackgroundSlots))
        {
            names.Add(Path.GetFileName(AssetPath.Background(slot)));
        }

        return names;
    }

    private static CustomPicture Picture(string hash, Entry entry, HashSet<string> slotNames)
    {
        var displayName = entry.Library.Count > 0
            ? Path.GetFileName(entry.Library[0].Path)
            : entry.GameNames.Count > 0 ? entry.GameNames[0] : hash;

        return new CustomPicture(
            hash,
            displayName,
            entry.Library.Select(l => l.Path).ToList(),
            entry.GameNames.Where(slotNames.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            entry.Library.Count > 0 ? entry.Library[0].Pack : null);
    }

    private static Entry Of(Dictionary<string, Entry> byHash, string hash)
    {
        if (!byHash.TryGetValue(hash, out var entry))
        {
            entry = new Entry();
            byHash[hash] = entry;
        }

        return entry;
    }

    private static IEnumerable<GameFile> Jpgs(GameFolder? folder) =>
        folder is null
            ? Array.Empty<GameFile>()
            : folder.Files.Where(f => JpgExtension.Equals(Path.GetExtension(f.Name), StringComparison.OrdinalIgnoreCase));

    /// <summary>Null for a file that has gone since the scan, which simply never becomes a picture.</summary>
    private static string? Hash(HashCache hashes, GameFile file)
    {
        try
        {
            return hashes.GetOrCompute(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed class Entry
    {
        public List<(string Pack, string Path)> Library { get; } = [];

        public List<string> GameNames { get; } = [];
    }
}
