namespace BhMaps.Core.Settings;

public sealed record AppSettings(string GamePath, string LibraryPath, bool FirstRunDone)
{
    public const string DefaultGamePath = @"C:\Program Files (x86)\Steam\steamapps\common\Brawlhalla\mapArt";
    public const string DefaultLibraryPath = @"C:\Users\alexa\files\bh";

    public static AppSettings Default { get; } = new(DefaultGamePath, DefaultLibraryPath, FirstRunDone: false);
}
