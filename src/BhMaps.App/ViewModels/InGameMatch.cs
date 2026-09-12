using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;

namespace BhMaps.App.ViewModels;

/// <summary>What the game is showing, as the last scan measured it. One rule everywhere: a file matching both the
/// Default pack and another pack counts as the other pack's, because the pack is what the user chose to apply.</summary>
public static class InGameMatch
{
    public static MapFileStatus? File(MapStatus? status, string relativePath) =>
        status?.Files.FirstOrDefault(f => f.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));

    public static bool Matches(MapStatus? status, string relativePath, string packName) =>
        File(status, relativePath) is { } file
        && (packName.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase)
            ? file.State == MapFileState.Default
            : file.PackNames.Contains(packName, StringComparer.OrdinalIgnoreCase));

    /// <summary>The tick and the ring on a platform set: every file the set would write is already the file that
    /// is there. A pack with nothing for the folder is never "in game".</summary>
    public static bool SetInGame(Pack pack, string folderName, MapStatus? status)
    {
        var paths = PlatformSetApplier.TargetPaths(pack, folderName);
        return paths.Count > 0 && paths.All(path => Matches(status, path, pack.Name));
    }
}
