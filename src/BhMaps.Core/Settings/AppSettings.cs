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
    public const string DefaultLibraryPath = @"C:\Users\alexa\files\bh";
    public const string RestartWhileRunning = "restart";
    public const string LiveWhileRunning = "live";

    public static AppSettings Default { get; } = new(DefaultGamePath, DefaultLibraryPath, FirstRunDone: false);

    /// <summary>Properties from settings.json this version does not know. Written back untouched.</summary>
    public System.Text.Json.Nodes.JsonObject? Unknown { get; init; }
}
