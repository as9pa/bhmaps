namespace BhMaps.Core.Settings;

public sealed record AppSettings(
    string GamePath,
    string LibraryPath,
    bool FirstRunDone,
    int HomeZoom = 3,
    int BackgroundsZoom = 4,
    string WhileRunning = AppSettings.RestartWhileRunning,
    bool WelcomeDone = false)
{
    public const string DefaultGamePath = @"C:\Program Files (x86)\Steam\steamapps\common\Brawlhalla\mapArt";
    public const string RestartWhileRunning = "restart";
    public const string LiveWhileRunning = "live";

    /// <summary>Spec 7.7: the library defaults to BhMaps inside Documents. Computed rather than a literal, so it
    /// follows a redirected Documents folder and never names one machine's user. Nothing creates the folder here;
    /// the first write into it does.</summary>
    public static string DefaultLibraryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BhMaps");

    public static AppSettings Default { get; } = new(DefaultGamePath, DefaultLibraryPath, FirstRunDone: false);

    /// <summary>Properties from settings.json this version does not know. Written back untouched.</summary>
    public System.Text.Json.Nodes.JsonObject? Unknown { get; init; }
}
