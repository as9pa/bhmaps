using BhMaps.Core.Settings;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void Load_ReturnsDefaultsWhenFileMissing()
    {
        using var tmp = new TempDir();

        var settings = SettingsStore.Load(Path.Combine(tmp.Path, "settings.json"));

        Assert.Equal(AppSettings.DefaultGamePath, settings.GamePath);
        Assert.Equal(AppSettings.DefaultLibraryPath, settings.LibraryPath);
        Assert.False(settings.FirstRunDone);
        Assert.Equal(@"C:\Program Files (x86)\Steam\steamapps\common\Brawlhalla\mapArt", AppSettings.DefaultGamePath);
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BhMaps"),
            AppSettings.DefaultLibraryPath);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsWithCamelCaseJsonAndNoTempFile()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "app", "settings.json");
        var settings = new AppSettings(@"D:\game\mapArt", @"D:\lib", true);

        SettingsStore.Save(path, settings);

        Assert.Equal(settings, SettingsStore.Load(path));
        var json = File.ReadAllText(path);
        Assert.Contains("\"gamePath\"", json);
        Assert.Contains("\"libraryPath\"", json);
        Assert.Contains("\"firstRunDone\": true", json);
        Assert.Equal(new[] { "settings.json" }, Directory.GetFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName));
    }

    [Fact]
    public void Load_FillsMissingFieldsWithDefaults()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        File.WriteAllText(path, "{ \"firstRunDone\": true }");

        var settings = SettingsStore.Load(path);

        Assert.Equal(AppSettings.DefaultGamePath, settings.GamePath);
        Assert.Equal(AppSettings.DefaultLibraryPath, settings.LibraryPath);
        Assert.True(settings.FirstRunDone);
    }

    [Fact]
    public void Load_ReturnsDefaultsOnCorruptJson()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        File.WriteAllText(path, "{ nope");

        Assert.Equal(AppSettings.Default, SettingsStore.Load(path));
    }

    [Fact]
    public void Load_ReturnsDefaultsWhenFileCannotBeRead()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        SettingsStore.Save(path, new AppSettings(@"D:\game\mapArt", @"D:\lib", true));

        using var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Equal(AppSettings.Default, SettingsStore.Load(path));
    }

    [Fact]
    public void ValidateGamePath_RequiresExistingFolderWithASubfolder()
    {
        using var tmp = new TempDir();
        var empty = tmp.Sub("empty", "x");
        Directory.CreateDirectory(Path.Combine(tmp.Path, "empty"));
        var good = Path.Combine(tmp.Path, "good");
        Directory.CreateDirectory(Path.Combine(good, "BloodMoon"));

        Assert.False(SettingsStore.ValidateGamePath(null, out var e1));
        Assert.NotEmpty(e1);
        Assert.False(SettingsStore.ValidateGamePath("", out _));
        Assert.False(SettingsStore.ValidateGamePath(Path.Combine(tmp.Path, "missing"), out var e2));
        Assert.Contains("does not exist", e2);
        Assert.False(SettingsStore.ValidateGamePath(Path.GetDirectoryName(empty)!, out var e3));
        Assert.Contains("no map folders", e3);
        Assert.True(SettingsStore.ValidateGamePath(good, out var e4));
        Assert.Equal("", e4);
    }

    [Fact]
    public void ValidateGamePath_ReportsAnUnreadableFolderAsAnError()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        Directory.CreateDirectory(Path.Combine(game, "BloodMoon"));

        using (AccessDenial.DenyListing(game))
        {
            Assert.False(SettingsStore.ValidateGamePath(game, out var error));
            Assert.Contains("Game folder cannot be read:", error);
        }

        Assert.True(SettingsStore.ValidateGamePath(game, out _));
    }

    [Fact]
    public void ValidateLibraryPath_AcceptsExistingOrCreatableAbsolutePaths()
    {
        using var tmp = new TempDir();

        Assert.True(SettingsStore.ValidateLibraryPath(tmp.Path, out var e1));
        Assert.Equal("", e1);
        Assert.True(SettingsStore.ValidateLibraryPath(Path.Combine(tmp.Path, "new", "deeper"), out _));
        Assert.False(SettingsStore.ValidateLibraryPath(null, out _));
        Assert.False(SettingsStore.ValidateLibraryPath("", out _));
        Assert.False(SettingsStore.ValidateLibraryPath("packs", out var e2));
        Assert.Contains("absolute", e2);
    }

    [Fact]
    public void DefaultAppDataDir_IsUnderRoamingAppData()
    {
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BhMaps"),
            SettingsStore.DefaultAppDataDir);
    }

    [Fact]
    public void Load_MissingV21Fields_UsesDefaults()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        File.WriteAllText(path, """{"gamePath":"C:\\g","libraryPath":"C:\\l","firstRunDone":true}""");

        var loaded = SettingsStore.Load(path);

        Assert.Equal(TileSize.Medium, loaded.MapsTileSize);
        Assert.Equal(TileSize.Medium, loaded.BackgroundsTileSize);
        Assert.Equal(TileSize.Medium, loaded.PlatformsTileSize);
        Assert.Equal(TileSize.Medium, loaded.PackTileSize);
        Assert.False(loaded.WelcomeDone);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEveryV21Field()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        var settings = new AppSettings(@"C:\g", @"C:\l", true, MapsTileSize: TileSize.Small,
            BackgroundsTileSize: TileSize.Large, PackTileSize: TileSize.Large, WelcomeDone: true);

        SettingsStore.Save(path, settings);
        var loaded = SettingsStore.Load(path);

        Assert.Equal(TileSize.Small, loaded.MapsTileSize);
        Assert.Equal(TileSize.Large, loaded.BackgroundsTileSize);
        Assert.Equal(TileSize.Large, loaded.PackTileSize);
        Assert.True(loaded.WelcomeDone);
        var json = File.ReadAllText(path);
        Assert.Contains("\"mapsTileSize\": \"small\"", json);
        Assert.Contains("\"packTileSize\": \"large\"", json);
    }

    [Fact]
    public void Save_PreservesUnknownFieldsFromTheExistingFile()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        File.WriteAllText(path, """{"gamePath":"C:\\g","futureThing":{"a":1},"favourites":["Grove"]}""");

        var loaded = SettingsStore.Load(path);
        SettingsStore.Save(path, loaded with { MapsTileSize = TileSize.Large });

        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal("large", (string)json["mapsTileSize"]!);
        Assert.Equal(1, (int)json["futureThing"]!["a"]!);
        Assert.Equal("Grove", (string)json["favourites"]![0]!);
    }

    [Fact]
    public void SaveThenLoad_ReadsAHandEditedKeyWhateverItsCaseAndWritesOneSpellingBack()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        File.WriteAllText(path, """{"GamePath":"D:\\g","LIBRARYPATH":"D:\\lib","FirstRunDone":true}""");

        var loaded = SettingsStore.Load(path);
        SettingsStore.Save(path, loaded);

        Assert.Equal(@"D:\g", loaded.GamePath);
        Assert.Equal(@"D:\lib", loaded.LibraryPath);
        Assert.True(loaded.FirstRunDone);
        Assert.Null(loaded.Unknown);
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(@"D:\g", (string)json["gamePath"]!);
        Assert.Equal(
            new[] { "gamePath" },
            json.Select(p => p.Key).Where(k => k.Equals("gamePath", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Load_ReadsATileSizeItDoesNotKnowAsMedium()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        File.WriteAllText(path, """{"mapsTileSize":"enormous","packTileSize":"LARGE","platformsTileSize":""}""");

        var loaded = SettingsStore.Load(path);

        Assert.Equal(TileSize.Medium, loaded.MapsTileSize);
        Assert.Equal(TileSize.Large, loaded.PackTileSize);
        Assert.Equal(TileSize.Medium, loaded.PlatformsTileSize);
    }

    [Theory]
    [InlineData(2, TileSize.Large)]
    [InlineData(4, TileSize.Large)]
    [InlineData(5, TileSize.Medium)]
    [InlineData(7, TileSize.Medium)]
    [InlineData(8, TileSize.Small)]
    [InlineData(10, TileSize.Small)]
    public void Load_MigratesAGridZoomIntoTheNearestTileSize(int zoom, TileSize expected)
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        File.WriteAllText(path, $$"""{"mapsZoom":{{zoom}},"packZoom":{{zoom}}}""");

        var loaded = SettingsStore.Load(path);

        Assert.Equal(expected, loaded.MapsTileSize);
        Assert.Equal(expected, loaded.PackTileSize);
    }

    [Theory]
    [InlineData(1, TileSize.Small)]
    [InlineData(3, TileSize.Small)]
    [InlineData(4, TileSize.Medium)]
    [InlineData(5, TileSize.Large)]
    public void Load_MigratesARowsZoomIntoTheNearestTileSize(int zoom, TileSize expected)
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        File.WriteAllText(path, $$"""{"backgroundsRowZoom":{{zoom}},"platformsZoom":{{zoom}}}""");

        var loaded = SettingsStore.Load(path);

        Assert.Equal(expected, loaded.BackgroundsTileSize);
        Assert.Equal(expected, loaded.PlatformsTileSize);
    }

    [Fact]
    public void Save_DropsTheOldZoomKeysOnceTheSizesAreWritten()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        File.WriteAllText(path, """{"mapsZoom":3,"packZoom":9,"backgroundsRowZoom":5,"platformsZoom":1}""");

        var loaded = SettingsStore.Load(path);
        SettingsStore.Save(path, loaded);

        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.DoesNotContain(json, p => p.Key.EndsWith("Zoom", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("large", (string)json["mapsTileSize"]!);
        Assert.Equal("small", (string)json["packTileSize"]!);
        Assert.Equal("large", (string)json["backgroundsTileSize"]!);
        Assert.Equal("small", (string)json["platformsTileSize"]!);
    }

    [Fact]
    public void Load_DropsAnOldBackgroundsZoomSoAnUpgradeStartsDense()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");

        // backgroundsZoom was a 2.0 tile size (3 to 8) and then a 2.1 column count (2 to 10). The rows page keeps
        // its thumbnail height under backgroundsRowZoom, so an old value is dropped rather than read as a height;
        // every upgrader starts at the dense default the owner chose (q1).
        File.WriteAllText(path, """{"backgroundsZoom":6}""");

        var loaded = SettingsStore.Load(path);

        Assert.Equal(TileSize.Medium, loaded.BackgroundsTileSize);
        Assert.Null(loaded.Unknown);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsThePlatformsAndBackgroundsTileSizes()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");

        SettingsStore.Save(
            path, AppSettings.Default with { PlatformsTileSize = TileSize.Large, BackgroundsTileSize = TileSize.Small });
        var loaded = SettingsStore.Load(path);

        Assert.Equal(TileSize.Large, loaded.PlatformsTileSize);
        Assert.Equal(TileSize.Small, loaded.BackgroundsTileSize);
        Assert.Contains("\"platformsTileSize\": \"large\"", File.ReadAllText(path));
        Assert.Contains("\"backgroundsTileSize\": \"small\"", File.ReadAllText(path));
        Assert.DoesNotContain("\"backgroundsZoom\"", File.ReadAllText(path));
    }

    [Fact]
    public void Load_MigratesAHomeZoomIntoTheMapsTileSizeAndPrefersMapsZoomWhenBothArePresent()
    {
        using var tmp = new TempDir();
        var legacy = tmp.Sub("legacy.json");
        File.WriteAllText(legacy, """{"homeZoom":4}""");
        var both = tmp.Sub("both.json");
        File.WriteAllText(both, """{"homeZoom":4,"mapsZoom":8}""");

        Assert.Equal(TileSize.Large, SettingsStore.Load(legacy).MapsTileSize);
        Assert.Equal(TileSize.Small, SettingsStore.Load(both).MapsTileSize);
    }

    [Fact]
    public void SaveThenLoad_DropsTheOldWhileRunningAndHomeZoomKeys()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        File.WriteAllText(path, """{"gamePath":"C:\\g","whileRunning":"restart","homeZoom":4,"keepMe":1}""");

        var loaded = SettingsStore.Load(path);
        SettingsStore.Save(path, loaded);

        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.DoesNotContain(json, p => p.Key.Equals("whileRunning", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(json, p => p.Key.Equals("homeZoom", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("large", (string)json["mapsTileSize"]!);
        Assert.Equal(1, (int)json["keepMe"]!);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsBackgroundsShowPictures()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");

        SettingsStore.Save(path, AppSettings.Default with { BackgroundsShowPictures = true });

        Assert.True(SettingsStore.Load(path).BackgroundsShowPictures);
        Assert.False(AppSettings.Default.BackgroundsShowPictures);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsPackLastApplied()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        var stamp = new DateTimeOffset(2026, 9, 12, 10, 30, 0, TimeSpan.FromHours(2));
        var settings = AppSettings.Default with
        {
            PackLastApplied = new Dictionary<string, DateTimeOffset> { ["dark"] = stamp },
        };

        SettingsStore.Save(path, settings);

        var loaded = SettingsStore.Load(path);
        Assert.Equal(stamp, loaded.LastApplied["dark"]);
        Assert.Equal(stamp, loaded.LastApplied["DARK"]);
        Assert.Single(loaded.LastApplied);
    }

    [Fact]
    public void Load_SkipsUnparseableStamps()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        File.WriteAllText(
            path,
            """{"packLastApplied":{"dark":"2026-09-12T10:30:00.0000000+02:00","b&w maps":"whenever","flowermap":7}}""");

        var loaded = SettingsStore.Load(path);

        Assert.Equal(new[] { "dark" }, loaded.LastApplied.Keys);
    }

    [Fact]
    public void Load_WithoutKey_GivesEmptyStamps()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        File.WriteAllText(path, """{"gamePath":"C:\\g"}""");

        var loaded = SettingsStore.Load(path);

        Assert.Empty(loaded.LastApplied);

        SettingsStore.Save(path, loaded);
        Assert.DoesNotContain("packLastApplied", File.ReadAllText(path));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsThePlatformPreviewMode()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");

        Assert.False(SettingsStore.Load(path).PlatformPreviewIsolate);

        SettingsStore.Save(path, AppSettings.Default with { PlatformPreviewIsolate = true });

        Assert.True(SettingsStore.Load(path).PlatformPreviewIsolate);
        Assert.Contains("\"platformPreviewIsolate\": true", File.ReadAllText(path));
    }

    [Fact]
    public void Load_IgnoresTheDroppedThumbnailSwitch()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");

        // 3.0 writes the game's map-select thumbnails always, so a 2.5 to 2.8 file still carrying the switch
        // loads without it, and Save does not write it back.
        File.WriteAllText(path, "{ \"welcomeDone\": true, \"writeGameThumbnails\": false }");
        var older = SettingsStore.Load(path);
        Assert.True(older.WelcomeDone);
        Assert.Null(older.Unknown);

        SettingsStore.Save(path, older);

        Assert.DoesNotContain("writeGameThumbnails", File.ReadAllText(path));
    }

    [Fact]
    public void Load_DefaultsTheUpdateKeysToOnAndNeverChecked()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "settings.json");
        File.WriteAllText(path, "{ \"gamePath\": \"D:\\\\g\", \"libraryPath\": \"D:\\\\l\" }");

        var settings = SettingsStore.Load(path);

        Assert.True(settings.CheckForUpdates);
        Assert.Null(settings.LastUpdateCheck);
        Assert.Null(settings.DismissedUpdate);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsTheUpdateKeys()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "settings.json");
        var checkedAt = new DateTimeOffset(2026, 9, 14, 15, 40, 0, TimeSpan.Zero);
        var settings = new AppSettings(@"D:\g", @"D:\l", true)
        {
            CheckForUpdates = false,
            LastUpdateCheck = checkedAt,
            DismissedUpdate = "v2.6.0",
        };

        SettingsStore.Save(path, settings);
        var read = SettingsStore.Load(path);

        Assert.False(read.CheckForUpdates);
        Assert.Equal(checkedAt, read.LastUpdateCheck);
        Assert.Equal("v2.6.0", read.DismissedUpdate);
        var json = File.ReadAllText(path);
        Assert.Contains("\"checkForUpdates\": false", json);
        Assert.Contains("\"lastUpdateCheck\"", json);
        Assert.Contains("\"dismissedUpdate\": \"v2.6.0\"", json);
    }

    [Fact]
    public void Save_WritesNullForANeverCheckedFileRatherThanDroppingTheKey()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "settings.json");

        SettingsStore.Save(path, new AppSettings(@"D:\g", @"D:\l", true));

        var json = File.ReadAllText(path);
        Assert.Contains("\"lastUpdateCheck\": null", json);
        Assert.Contains("\"dismissedUpdate\": null", json);
        Assert.Contains("\"checkForUpdates\": true", json);
    }

    [Fact]
    public void Load_IgnoresAHandEditedUpdateStampRatherThanFailingTheFile()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "settings.json");
        File.WriteAllText(path, "{ \"lastUpdateCheck\": \"soon\", \"checkForUpdates\": \"yes\" }");

        var settings = SettingsStore.Load(path);

        Assert.Null(settings.LastUpdateCheck);
        Assert.True(settings.CheckForUpdates);
        Assert.Null(settings.Unknown);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsTheHiddenPacks()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        var settings = AppSettings.Default with { HiddenPackNames = ["dark", "b&w maps"] };

        SettingsStore.Save(path, settings);

        var loaded = SettingsStore.Load(path);
        Assert.Equal(new[] { "dark", "b&w maps" }, loaded.HiddenPacks);
        Assert.True(loaded.IsHidden("DARK"));
        Assert.False(loaded.IsHidden("flowermap"));
    }

    [Fact]
    public void Load_WithoutKey_GivesNoHiddenPacks()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        File.WriteAllText(path, """{"gamePath":"C:\\g"}""");

        var loaded = SettingsStore.Load(path);

        Assert.Empty(loaded.HiddenPacks);
        Assert.False(loaded.IsHidden("dark"));

        SettingsStore.Save(path, loaded);
        Assert.DoesNotContain("hiddenPacks", File.ReadAllText(path));
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsTheDismissedTransparentNotes()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        var notes = new Dictionary<string, int> { ["dark"] = 3, ["b&w maps"] = 1 };
        var settings = AppSettings.Default with { DismissedTransparentNotes = notes };

        SettingsStore.Save(path, settings);

        var loaded = SettingsStore.Load(path);
        Assert.Equal(3, loaded.DismissedTransparent["DARK"]);
        Assert.True(loaded.IsTransparentNoteDismissed("Dark", 3));
        Assert.False(loaded.IsTransparentNoteDismissed("dark", 4));
        Assert.False(loaded.IsTransparentNoteDismissed("flowermap", 1));
    }

    [Fact]
    public void Load_WithoutKey_GivesNoDismissedTransparentNotes()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        File.WriteAllText(path, """{"gamePath":"C:\\g"}""");

        var loaded = SettingsStore.Load(path);

        Assert.Empty(loaded.DismissedTransparent);
        Assert.False(loaded.IsTransparentNoteDismissed("dark", 3));

        SettingsStore.Save(path, loaded);
        Assert.DoesNotContain("dismissedTransparentNotes", File.ReadAllText(path));
    }
}
