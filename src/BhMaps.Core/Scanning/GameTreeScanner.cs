using BhMaps.Core.Model;

namespace BhMaps.Core.Scanning;

public static class GameTreeScanner
{
    /// <summary>Scans the mapArt folder. A missing path yields an empty tree, not an exception.</summary>
    public static GameTree Scan(string gamePath) => new(gamePath, ImageFiles.ScanOneLevel(gamePath));
}
