namespace BhMaps.Core.Settings;

public sealed record AppSettings(
    string GamePath,
    string LibraryPath,
    bool FirstRunDone,
    int MapsZoom = 6,
    int BackgroundsZoom = 6,
    int PackZoom = 5,
    bool WelcomeDone = false)
{
    public const string DefaultGamePath = @"C:\Program Files (x86)\Steam\steamapps\common\Brawlhalla\mapArt";

    /// <summary>Columns a grid can be zoomed to (spec 3.1). One range for every page, so a value hand-edited or
    /// carried over from another page is never clamped away by a narrower one.</summary>
    public const int MinZoom = 2;
    public const int MaxZoom = 10;

    /// <summary>Spec 7.7: the library defaults to BhMaps inside Documents. Computed rather than a literal, so it
    /// follows a redirected Documents folder and never names one machine's user. Nothing creates the folder here;
    /// the first write into it does.</summary>
    public static string DefaultLibraryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BhMaps");

    public static AppSettings Default { get; } = new(DefaultGamePath, DefaultLibraryPath, FirstRunDone: false);

    /// <summary>Properties from settings.json this version does not know. Written back untouched.</summary>
    public System.Text.Json.Nodes.JsonObject? Unknown { get; init; }
}
