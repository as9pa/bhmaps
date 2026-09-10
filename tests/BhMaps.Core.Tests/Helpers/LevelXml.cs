namespace BhMaps.Core.Tests.Helpers;

/// <summary>Builds the level data shapes the parsers read. Synthetic only; no game data is copied into the repo.</summary>
public static class LevelXml
{
    public static string Level(string name, string assetDir, string body) =>
        $"<LevelDesc LevelName=\"{name}\" AssetDir=\"{assetDir}\">{body}</LevelDesc>";

    public static string LevelWithElements(string name, string assetDir, string body) =>
        $"<LevelDesc><LevelName>{name}</LevelName><AssetDir>{assetDir}</AssetDir>{body}</LevelDesc>";

    public static string Camera(double x, double y, double w, double h) =>
        $"<CameraBounds X=\"{x}\" Y=\"{y}\" W=\"{w}\" H=\"{h}\" />";

    public static string Types(params (string Level, string Display, bool DevOnly, bool TestLevel)[] rows) =>
        "<LevelTypes>" + string.Concat(rows.Select(r =>
            $"<LevelType><LevelName>{r.Level}</LevelName><DisplayName>{r.Display}</DisplayName>"
            + (r.DevOnly ? "<DevOnly>true</DevOnly>" : "")
            + (r.TestLevel ? "<TestLevel>true</TestLevel>" : "")
            + "</LevelType>")) + "</LevelTypes>";

    public static string Sets(params (string Name, string Levels)[] rows) =>
        "<LevelSetTypes>" + string.Concat(rows.Select(r =>
            $"<LevelSetType><LevelSetName>{r.Name}</LevelSetName><LevelTypes>{r.Levels}</LevelTypes></LevelSetType>"))
        + "</LevelSetTypes>";
}
