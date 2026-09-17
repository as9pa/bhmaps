using BhMaps.App.Services;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;

namespace BhMaps.App.ViewModels;

/// <summary>What a map's background art is, in one short line: the detail under a menu header (spec 4.1). The
/// map panel keeps its own sentence in MapPanelViewModel.BuildStatusText, because a panel has room for a
/// sentence and a menu header has room for a line.</summary>
public static class MapArtText
{
    /// <summary>The line, or null when the scan knows nothing about the map's background. Never wraps: it names
    /// one source, not the whole file list.</summary>
    public static string? Describe(MapEntry map, MapStatus? status, ScanSnapshot snapshot)
    {
        // A missing file is what the user needs to hear first, whichever slot it is in.
        var missing = status?.Files.Count(f => f.State == MapFileState.Missing) ?? 0;
        if (missing > 0)
        {
            return missing == 1 ? "Missing 1 file" : $"Missing {missing} files";
        }

        var slot = map.BackgroundSlots.Count > 0 ? map.BackgroundSlots[0] : null;
        if (slot is null)
        {
            return null;
        }

        return InGameMatch.File(status, AssetPath.Background(slot)) switch
        {
            { State: MapFileState.Custom } => CustomText(slot, snapshot),
            { State: MapFileState.Pack, PackNames.Count: > 0 } file => $"{file.PackNames[0]} pack",
            { State: MapFileState.Default } => "Default art",
            _ => null,
        };
    }

    /// <summary>The picture the game is showing names the pack it lives in, which is where the user would look
    /// for it again; a picture no pack holds is in the game and nowhere else (spec 3).</summary>
    private static string CustomText(string slot, ScanSnapshot snapshot)
    {
        var fileName = Path.GetFileName(AssetPath.Background(slot));
        var picture = snapshot.CustomPictures.FirstOrDefault(
            p => p.InGameSlots.Contains(fileName, StringComparer.OrdinalIgnoreCase));
        var name = Path.GetFileNameWithoutExtension(picture?.DisplayName ?? fileName);
        return picture?.PackName is { } pack ? $"{name} from {pack}" : $"{name} in game only";
    }
}
