using BhMaps.Core.Hashing;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;

namespace BhMaps.Core.Operations;

/// <summary>One picture that is nobody's map art: a file the user imported into a pack of their own under a name
/// no map's background slot uses, or a background in the game that no pack accounts for. The Default pack is a
/// capture of the game's own folder, so a file of its own is the game's and never a picture of the user's. One
/// picture is one entry however many copies of it exist,
/// because the user thinks in pictures and the app should not show the same one four times (spec 4). ReadOnlyPaths
/// are the LibraryPaths that sit in a discovered pack (outside packs\), which a rename refuses to touch.</summary>
public sealed record CustomPicture(
    string Hash,
    string DisplayName,
    IReadOnlyList<string> LibraryPaths,
    IReadOnlyList<string> InGameSlots,
    string? PackName,
    IReadOnlyList<string>? ReadOnlyPaths = null)
{
    /// <summary>3.0: the picture's name, which is its file name without the extension. Every line that names
    /// the picture to the user (tile, chooser title, done line, menu row) says this; DisplayName is the file.</summary>
    public string Name => Path.GetFileNameWithoutExtension(DisplayName);
}

/// <summary>One library copy that took a new name: where it was and where it is now, so the caller can point the
/// applied record at the file the game's bytes came from.</summary>
public sealed record PictureRename(string From, string To);

/// <summary>What a rename did. No pairs and no failures is the no-op: renaming a picture to the name it already
/// has moves nothing.</summary>
public sealed record PictureRenameResult(IReadOnlyList<PictureRename> Renamed, IReadOnlyList<FileFailure> Failures);

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
            // The Default pack is a capture of the game folder, so a file in it under no map's slot name is one
            // of the game's own unreferenced backgrounds, not something the user imported. Its hash still counts
            // as pack art, so the game file matching it stays out of the picture list too.
            var isDefault = pack.Name.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase);
            foreach (var file in Jpgs(pack.FindFolder(BackgroundsFolder)))
            {
                if (Hash(hashes, file) is not { } hash)
                {
                    continue;
                }

                packHashes.Add(hash);
                if (!isDefault && !slotNames.Contains(file.Name))
                {
                    Of(byHash, hash).Library.Add((pack.Name, file.FullPath));
                    if (pack.IsDiscovered)
                    {
                        Of(byHash, hash).ReadOnly.Add(file.FullPath);
                    }
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
    public static string NameFor(IReadOnlyList<CustomPicture> pictures, string slot) =>
        NamedFor(pictures, slot) ?? Path.GetFileName(AssetPath.Background(slot));

    /// <summary>The library's own name for the picture in <paramref name="slot"/>, or null when no picture
    /// matches. 3.0: the callers that would rather print nothing than print a file name ask this one.</summary>
    public static string? NamedFor(IReadOnlyList<CustomPicture> pictures, string slot)
    {
        var fileName = Path.GetFileName(AssetPath.Background(slot));
        return pictures
            .FirstOrDefault(p => p.InGameSlots.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            ?.DisplayName;
    }

    /// <summary>Renames the picture: its file in the library takes <paramref name="newBaseName"/>, keeping its
    /// folder and its extension, and the name is made unique when that folder already holds it. One picture is
    /// one entry however many copies of it exist, so every copy is renamed together, the way deleting one
    /// deletes them all. Nothing in the game folder is touched: the bytes the game is showing came from this
    /// file and still did after it changed its name, which is why the caller points the applied record at the
    /// new path rather than forgetting the entry.</summary>
    public static PictureRenameResult Rename(CustomPicture picture, string newBaseName)
    {
        var renamed = new List<PictureRename>();
        var failures = new List<FileFailure>();
        foreach (var path in picture.LibraryPaths)
        {
            var folder = Path.GetDirectoryName(path);
            var current = Path.GetFileName(path);
            var wanted = PictureNames.FileName(newBaseName, Path.GetExtension(path));
            if (folder is null || wanted.Equals(current, StringComparison.Ordinal))
            {
                continue;
            }

            // A copy in a discovered pack is read-only: it keeps its name and says why.
            if (picture.ReadOnlyPaths?.Contains(path, StringComparer.OrdinalIgnoreCase) == true)
            {
                failures.Add(new FileFailure(path, "The pack this copy is in is outside the packs folder, so it is read-only."));
                continue;
            }

            try
            {
                // The file's own name is not a name it has to dodge, so a picture renamed only in its casing
                // becomes "Sunset.jpg" rather than "Sunset (2).jpg".
                var taken = Names(folder).Where(n => !n.Equals(current, StringComparison.OrdinalIgnoreCase));
                var target = Path.Combine(folder, PictureNames.Unique(wanted, taken));
                File.Move(path, target);
                renamed.Add(new PictureRename(path, target));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new FileFailure(path, ex.Message));
            }
        }

        return new PictureRenameResult(renamed, failures);
    }

    /// <summary>The file names a folder holds, or none when it has gone since the scan, which a rename into it
    /// then fails on its own rather than here.</summary>
    private static IEnumerable<string> Names(string folder)
    {
        try
        {
            return [.. Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>()];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
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
            entry.Library.Count > 0 ? entry.Library[0].Pack : null,
            entry.ReadOnly.Count > 0 ? [.. entry.ReadOnly] : null);
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

        public HashSet<string> ReadOnly { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
