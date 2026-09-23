using BhMaps.App.Services;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;

namespace BhMaps.App.ViewModels;

/// <summary>What a map's art is, in the short lines a card's menu has room for (spec 4.1). The map panel keeps
/// its own sentence in MapPanelViewModel.BuildStatusText, because a panel has room for a sentence and a menu line
/// has room for a name.</summary>
public static class MapArtText
{
    /// <summary>The name the game's own art goes by under an edit line.</summary>
    public const string DefaultName = "Default";

    private const string InGameOnly = "in game only";

    /// <summary>What a card's menu header says under the map's name, or null. 3.2: only a missing file, which the
    /// user needs to hear first whichever slot it is in; the pack the art comes from is said under each edit
    /// line instead (owner, 2026-09-22).</summary>
    public static string? Describe(MapStatus? status)
    {
        var missing = status?.Files.Count(f => f.State == MapFileState.Missing) ?? 0;
        return missing switch
        {
            0 => null,
            1 => "Missing 1 file",
            _ => $"Missing {missing} files",
        };
    }

    /// <summary>3.2: the pack the map's background comes from, bare ("flowermap", "My Backgrounds", "Default"),
    /// for the line under Edit background. A custom picture names the pack it lives in, or says it is in the game
    /// only when no pack holds it. Null when the scan knows nothing about the background.</summary>
    public static string? BackgroundPack(MapEntry map, MapStatus? status, ScanSnapshot snapshot)
    {
        var slot = map.BackgroundSlots.Count > 0 ? map.BackgroundSlots[0] : null;
        if (slot is null)
        {
            return null;
        }

        return InGameMatch.File(status, AssetPath.Background(slot)) switch
        {
            { State: MapFileState.Custom } => CustomPack(slot, snapshot),
            { State: MapFileState.Pack, PackNames.Count: > 0 } file => file.PackNames[0],
            { State: MapFileState.Default } => DefaultName,
            _ => null,
        };
    }

    /// <summary>3.2: the pack or packs the layout's platform files come from, for the line under Edit platforms:
    /// one name when every file agrees, the names joined with ", " when they differ, "Default" for the game's own
    /// art. A file two packs share counts as the pack another file already named, so two packs that both hold
    /// the whole set read as one. A missing file names nothing. Null when no file names anything.</summary>
    public static string? PlatformPacks(MapEntry map, MapStatus? status)
    {
        var names = new List<string>();
        foreach (var relativePath in map.LayoutFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var name = InGameMatch.File(status, relativePath) switch
            {
                { State: MapFileState.Pack, PackNames.Count: > 0 } file =>
                    file.PackNames.FirstOrDefault(n => names.Contains(n, StringComparer.OrdinalIgnoreCase))
                    ?? file.PackNames[0],
                { State: MapFileState.Default } => DefaultName,
                { State: MapFileState.Custom } => InGameOnly,
                _ => null,
            };
            if (name is not null && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name);
            }
        }

        return names.Count == 0 ? null : string.Join(", ", names);
    }

    /// <summary>The picture the game is showing names the pack it lives in, which is where the user would look
    /// for it again; a picture no pack holds is in the game and nowhere else (spec 3).</summary>
    private static string CustomPack(string slot, ScanSnapshot snapshot)
    {
        var fileName = Path.GetFileName(AssetPath.Background(slot));
        var picture = snapshot.CustomPictures.FirstOrDefault(
            p => p.InGameSlots.Contains(fileName, StringComparer.OrdinalIgnoreCase));
        return picture?.PackName ?? InGameOnly;
    }
}
