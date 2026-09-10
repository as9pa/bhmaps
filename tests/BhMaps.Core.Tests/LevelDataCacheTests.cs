using System.Text;
using BhMaps.Core.LevelData;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class LevelDataCacheTests
{
    private const uint Key = 826351010;

    /// <summary>A game root holding the four files, with two levels sharing the Grove folder.</summary>
    private static string BuildGameRoot(TempDir tmp, string groveDisplay = "Twilight Grove")
    {
        var root = Path.Combine(tmp.Path, "game");
        Directory.CreateDirectory(root);

        File.WriteAllBytes(Path.Combine(root, "BrawlhallaAir.swf"), SwfWriter.Uncompressed([Key]));
        File.WriteAllBytes(Path.Combine(root, "Dynamic.swz"), SwzWriter.Write(Key,
        [
            Encoding.UTF8.GetBytes(LevelXml.Level("Grove", "Grove",
                LevelXml.Camera(0, 0, 100, 50) + "<Background AssetName=\"BG_Grove.jpg\" />")),
            Encoding.UTF8.GetBytes(LevelXml.Level("SmallGrove", "Grove", LevelXml.Camera(0, 0, 60, 30))),
        ]));
        File.WriteAllBytes(Path.Combine(root, "Init.swz"), SwzWriter.Write(Key,
        [
            Encoding.UTF8.GetBytes(LevelXml.Types(
                ("Grove", groveDisplay, false, false),
                ("SmallGrove", "Small Grove", false, false))),
        ]));
        File.WriteAllBytes(Path.Combine(root, "Game.swz"), SwzWriter.Write(Key,
            [Encoding.UTF8.GetBytes(LevelXml.Sets(("Ranked1v1", "Grove")))]));
        return root;
    }

    /// <summary>The same bytes the game ships for some entries: UTF-8 with a leading byte-order mark.</summary>
    private static byte[] WithBom(string xml) => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(xml)];

    [Fact]
    public void Read_ParsesTheFourFilesIntoOneModel()
    {
        using var tmp = new TempDir();

        var result = LevelDataReader.Read(BuildGameRoot(tmp), knownKey: null);

        Assert.True(result.Available, result.Error);
        Assert.Equal(Key, result.Key);
        Assert.Equal(2, result.Model!.Levels.Count);
        Assert.Equal("Twilight Grove", result.Model.Types.Single(t => t.LevelName == "Grove").DisplayName);
        Assert.Equal("Ranked1v1", Assert.Single(result.Model.Sets).Name);
    }

    [Fact]
    public void Read_UsesAKnownKeyWithoutScanningTheSwf()
    {
        using var tmp = new TempDir();
        var root = BuildGameRoot(tmp);
        File.WriteAllBytes(Path.Combine(root, "BrawlhallaAir.swf"), [0, 1, 2]);   // unusable SWF

        Assert.True(LevelDataReader.Read(root, knownKey: Key).Available);
    }

    [Fact]
    public void Read_ReturnsAnErrorInsteadOfThrowingWhenFilesAreMissing()
    {
        using var tmp = new TempDir();

        var result = LevelDataReader.Read(tmp.Path, knownKey: null);

        Assert.False(result.Available);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Read_ParsesEntriesThatStartWithAByteOrderMark()
    {
        using var tmp = new TempDir();
        var root = BuildGameRoot(tmp);
        File.WriteAllBytes(Path.Combine(root, "Dynamic.swz"), SwzWriter.Write(Key,
            [WithBom(LevelXml.Level("Grove", "Grove", LevelXml.Camera(0, 0, 100, 50)))]));
        File.WriteAllBytes(Path.Combine(root, "Init.swz"), SwzWriter.Write(Key,
            [WithBom(LevelXml.Types(("Grove", "Twilight Grove", false, false)))]));

        var result = LevelDataReader.Read(root, knownKey: Key);

        Assert.True(result.Available, result.Error);
        Assert.Null(result.Error);
        Assert.Equal("Grove", Assert.Single(result.Model!.Levels).LevelName);
        Assert.Equal("Twilight Grove", Assert.Single(result.Model.Types).DisplayName);
    }

    [Fact]
    public void Read_SkipsOneMalformedLevelAndKeepsTheRest()
    {
        using var tmp = new TempDir();
        var root = BuildGameRoot(tmp);
        File.WriteAllBytes(Path.Combine(root, "Dynamic.swz"), SwzWriter.Write(Key,
        [
            Encoding.UTF8.GetBytes("<LevelDesc LevelName=\"Broken\"><CameraBounds X=\"0\"></LevelDesc>"),
            Encoding.UTF8.GetBytes(LevelXml.Level("Grove", "Grove", LevelXml.Camera(0, 0, 100, 50))),
        ]));

        var result = LevelDataReader.Read(root, knownKey: Key);

        Assert.Equal("Grove", Assert.Single(result.Model!.Levels).LevelName);
        Assert.Contains("Broken", result.Error);
    }

    [Fact]
    public void Stamp_ChangesWhenADataFileChanges()
    {
        using var tmp = new TempDir();
        var root = BuildGameRoot(tmp);
        var before = LevelDataReader.Stamp(root, Key);

        File.AppendAllText(Path.Combine(root, "Game.swz"), "x");

        Assert.NotEqual(before, LevelDataReader.Stamp(root, Key));
    }

    [Fact]
    public void Stamp_ChangesWhenTheKeyChanges()
    {
        using var tmp = new TempDir();
        var root = BuildGameRoot(tmp);

        Assert.NotEqual(LevelDataReader.Stamp(root, Key), LevelDataReader.Stamp(root, Key + 1));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsTheModelAndTheStamp()
    {
        using var tmp = new TempDir();
        var root = BuildGameRoot(tmp);
        var read = LevelDataReader.Read(root, null);
        var appData = Path.Combine(tmp.Path, "appdata");
        var cachePath = LevelDataCache.PathFor(appData);
        var stamp = LevelDataReader.Stamp(root, read.Key);

        LevelDataCache.Save(cachePath, read.Model!, stamp);
        var loaded = LevelDataCache.Load(cachePath);

        Assert.NotNull(loaded);
        Assert.Equal(stamp, loaded!.Value.Stamp);
        Assert.Equal(2, loaded.Value.Model.Levels.Count);
        Assert.Equal("Grove", loaded.Value.Model.Levels[0].AssetDir);
        Assert.Equal("BG_Grove.jpg", loaded.Value.Model.Levels[0].Backgrounds[0].AssetName);
    }

    [Fact]
    public void Load_ReturnsNullForACorruptCache()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("leveldata.json");
        File.WriteAllText(path, "{ not json");

        Assert.Null(LevelDataCache.Load(path));
    }

    /// <summary>The stamp only notices the game changing, so a model written by an older build of the app has to
    /// be turned away by its version or it would be served forever.</summary>
    [Fact]
    public void Load_ReturnsNullWhenTheSchemaVersionIsMissingOrDifferent()
    {
        using var tmp = new TempDir();
        var root = BuildGameRoot(tmp);
        var read = LevelDataReader.Read(root, null);
        var path = LevelDataCache.PathFor(Path.Combine(tmp.Path, "appdata"));
        LevelDataCache.Save(path, read.Model!, LevelDataReader.Stamp(root, read.Key));
        var stamped = File.ReadAllText(path);
        var missing = stamped.Replace($"\"version\":{LevelDataCache.SchemaVersion},", "");
        var older = stamped.Replace($"\"version\":{LevelDataCache.SchemaVersion}", "\"version\":1");

        // Both edits must bite, or the test would pass on a cache file it never changed.
        Assert.NotEqual(stamped, missing);
        Assert.NotEqual(stamped, older);

        File.WriteAllText(path, missing);
        Assert.Null(LevelDataCache.Load(path));

        File.WriteAllText(path, older);
        Assert.Null(LevelDataCache.Load(path));

        File.WriteAllText(path, stamped);
        Assert.NotNull(LevelDataCache.Load(path));
    }

    [Fact]
    public void Save_LeavesNoTempFileBehind()
    {
        using var tmp = new TempDir();
        var root = BuildGameRoot(tmp);
        var read = LevelDataReader.Read(root, null);
        var appData = Path.Combine(tmp.Path, "appdata");

        LevelDataCache.Save(LevelDataCache.PathFor(appData), read.Model!, LevelDataReader.Stamp(root, read.Key));

        Assert.Equal(["leveldata.json"], Directory.GetFiles(appData).Select(Path.GetFileName));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsThePlatformTree()
    {
        using var tmp = new TempDir();
        var root = Path.Combine(tmp.Path, "game");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "Dynamic.swz"), SwzWriter.Write(Key,
            [Encoding.UTF8.GetBytes(LevelXml.Level("Grove", "Grove",
                LevelXml.Camera(0, 0, 100, 50)
                + "<Platform X=\"1\" Scale=\"2\" Theme=\"Snow\">"
                + "<Asset AssetName=\"a.png\" X=\"1\" Y=\"2\" W=\"-3\" H=\"4\" /></Platform>"))]));
        var read = LevelDataReader.Read(root, Key);
        var path = LevelDataCache.PathFor(Path.Combine(tmp.Path, "appdata"));

        LevelDataCache.Save(path, read.Model!, LevelDataReader.Stamp(root, read.Key));
        var loaded = LevelDataCache.Load(path);

        var json = File.ReadAllText(path);
        Assert.DoesNotContain("isThemed", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"\"version\":{LevelDataCache.SchemaVersion}", json, StringComparison.Ordinal);
        var platform = Assert.Single(Assert.Single(loaded!.Value.Model.Levels).Platforms);
        Assert.Equal(2, platform.Scale);
        Assert.Equal("Snow", platform.Theme);
        Assert.True(platform.IsThemed);
        Assert.Equal(-3, Assert.Single(platform.Assets).W);
    }
}
