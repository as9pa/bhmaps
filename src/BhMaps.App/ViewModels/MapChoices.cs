using BhMaps.App.Services;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Operations;

namespace BhMaps.App.ViewModels;

/// <summary>One strip of any-map pictures: every picture in one pack, or the ones only the game has. The
/// header carries the count, because a strip folded shut has to say how much is behind it (spec 3).</summary>
public sealed record PictureGroup(string Header, string? PackName, IReadOnlyList<CustomPictureTileViewModel> Tiles);

/// <summary>The two lists of choices one map has: the pictures its background slot can take, and the platform
/// sets its folder can take. Built here and nowhere else, so the map panel (spec 3.2) and a rows page (addendum
/// B and C) cannot offer a different set of choices for the same map. The order is the panel's: Default first,
/// then the packs in the Packs page order. A rows page reorders what it gets, it does not rebuild it.</summary>
public static class MapChoices
{
    /// <summary>One tile per pack holding a picture for the map's first background slot. Empty for a map with no
    /// slot, which is what a map looks like without level data (spec 3.6).</summary>
    public static IReadOnlyList<MapPictureTileViewModel> PackBackgrounds(
        MainViewModel shell, MapEntry map, MapStatus? status, ScanSnapshot snapshot)
    {
        if (map.BackgroundSlots.Count == 0)
        {
            return [];
        }

        var slot = map.BackgroundSlots[0];
        var relative = AssetPath.Background(slot);
        var tiles = new List<MapPictureTileViewModel>();
        foreach (var choice in BackgroundChoices.For(slot, snapshot.Packs))
        {
            tiles.Add(new MapPictureTileViewModel(
                shell, map, slot, choice.Pack.Name, "", choice.File.FullPath, choice.Pack.Name,
                InGameMatch.Matches(status, relative, choice.Pack.Name)));
        }

        return tiles;
    }

    /// <summary>One tile per custom picture in the library, offered for this map's first slot. The tick is the
    /// picture's own list of in-game slots, so a picture the game is showing on this map carries the check.</summary>
    public static IReadOnlyList<CustomPictureTileViewModel> CustomBackgrounds(
        MainViewModel shell, MapEntry map, ScanSnapshot snapshot)
    {
        if (map.BackgroundSlots.Count == 0)
        {
            return [];
        }

        var slot = map.BackgroundSlots[0];
        var fileName = Path.GetFileName(AssetPath.Background(slot));
        var tiles = new List<CustomPictureTileViewModel>();
        foreach (var picture in snapshot.CustomPictures)
        {
            tiles.Add(new CustomPictureTileViewModel(
                shell, picture, "", map, slot,
                picture.InGameSlots.Contains(fileName, StringComparer.OrdinalIgnoreCase)));
        }

        return tiles;
    }

    /// <summary>The same tiles as <see cref="CustomBackgrounds" />, one group per pack that has an any-map
    /// picture, in the Packs page order, then one last group for the pictures only the game has (spec 3). A pack
    /// with nothing of the kind is not a group at all.</summary>
    public static IReadOnlyList<PictureGroup> PictureGroups(
        MainViewModel shell, MapEntry map, ScanSnapshot snapshot)
    {
        var tiles = CustomBackgrounds(shell, map, snapshot);
        var groups = new List<PictureGroup>();
        foreach (var pack in snapshot.Packs)
        {
            var inPack = tiles
                .Where(t => string.Equals(t.PackName, pack.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (inPack.Count > 0)
            {
                groups.Add(new PictureGroup($"{pack.Name} ({inPack.Count})", pack.Name, inPack));
            }
        }

        var inGameOnly = tiles.Where(t => t.PackName is null).ToList();
        if (inGameOnly.Count > 0)
        {
            groups.Add(new PictureGroup($"In game only ({inGameOnly.Count})", null, inGameOnly));
        }

        return groups;
    }

    /// <summary>One tile per pack with at least one file for this map's folder, the Default pack first. A pack
    /// that has nothing for the map is not a choice at all (addendum C). <paramref name="showFiles" /> is null
    /// where the caller has no file list to open, and the tiles then leave Show files out of the menu (spec 9).</summary>
    public static IReadOnlyList<PlatformSetTileViewModel> Platforms(
        MainViewModel shell, MapEntry map, MapStatus? status, ScanSnapshot snapshot,
        int width, int height, Action? showFiles)
    {
        var tiles = new List<PlatformSetTileViewModel>();
        foreach (var pack in PlatformSetApplier.SetsFor(map.FolderName, snapshot.Packs))
        {
            tiles.Add(new PlatformSetTileViewModel(
                shell, map, pack, InGameMatch.SetInGame(pack, map.FolderName, status), width, height, showFiles,
                () => _ = shell.OpenPlatformEditorAsync(map, pack)));
        }

        return tiles;
    }
}
