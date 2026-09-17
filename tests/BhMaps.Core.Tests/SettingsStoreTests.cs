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

        Assert.Equal(6, loaded.MapsZoom);
        Assert.Equal(2, loaded.BackgroundsZoom);
        Assert.Equal(3, loaded.PlatformsZoom);
        Assert.Equal(5, loaded.PackZoom);
        Assert.False(loaded.WelcomeDone);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEveryV21Field()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        var settings = new AppSettings(@"C:\g", @"C:\l", true, MapsZoom: 9, BackgroundsZoom: 3, PackZoom: 7,
            WelcomeDone: true);

        SettingsStore.Save(path, settings);
        var loaded = SettingsStore.Load(path);

        Assert.Equal(9, loaded.MapsZoom);
        Assert.Equal(3, loaded.BackgroundsZoom);
        Assert.Equal(7, loaded.PackZoom);
        Assert.True(loaded.WelcomeDone);
        var json = File.ReadAllText(path);
        Assert.Contains("\"mapsZoom\": 9", json);
        Assert.Contains("\"packZoom\": 7", json);
    }

    [Fact]
    public void Save_PreservesUnknownFieldsFromTheExistingFile()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        File.WriteAllText(path, """{"gamePath":"C:\\g","futureThing":{"a":1},"favourites":["Grove"]}""");

        var loaded = SettingsStore.Load(path);
        SettingsStore.Save(path, loaded with { MapsZoom = 2 });

        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(2, (int)json["mapsZoom"]!);
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
    public void Load_ClampsTheGridZoomsToTwoThroughTenAndTheRowZoomsToOneThroughFive()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        File.WriteAllText(path, """{"mapsZoom":99,"backgroundsRowZoom":0,"packZoom":-4,"platformsZoom":9}""");

        var loaded = SettingsStore.Load(path);

        Assert.Equal(10, loaded.MapsZoom);
        Assert.Equal(1, loaded.BackgroundsZoom);
        Assert.Equal(2, loaded.PackZoom);
        Assert.Equal(5, loaded.PlatformsZoom);
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

        Assert.Equal(2, loaded.BackgroundsZoom);
        Assert.Null(loaded.Unknown);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsThePlatformsZoom()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");

        SettingsStore.Save(path, AppSettings.Default with { PlatformsZoom = 4, BackgroundsZoom = 1 });
        var loaded = SettingsStore.Load(path);

        Assert.Equal(4, loaded.PlatformsZoom);
        Assert.Equal(1, loaded.BackgroundsZoom);
        Assert.Contains("\"platformsZoom\": 4", File.ReadAllText(path));
        Assert.Contains("\"backgroundsRowZoom\": 1", File.ReadAllText(path));
        Assert.DoesNotContain("\"backgroundsZoom\"", File.ReadAllText(path));
    }

    [Fact]
    public void Load_MigratesAHomeZoomIntoMapsZoomAndPrefersMapsZoomWhenBothArePresent()
    {
        using var tmp = new TempDir();
        var legacy = tmp.Sub("legacy.json");
        File.WriteAllText(legacy, """{"homeZoom":4}""");
        var both = tmp.Sub("both.json");
        File.WriteAllText(both, """{"homeZoom":4,"mapsZoom":8}""");

        Assert.Equal(4, SettingsStore.Load(legacy).MapsZoom);
        Assert.Equal(8, SettingsStore.Load(both).MapsZoom);
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
        Assert.Equal(4, (int)json["mapsZoom"]!);
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
    public void WriteGameThumbnails_RoundTripsAndDefaultsOff()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");

        // Spec 10.2: the switch is opt-in, so a file written before it existed loads off.
        File.WriteAllText(path, "{ \"welcomeDone\": true }");
        var older = SettingsStore.Load(path);
        Assert.True(older.WelcomeDone);
        Assert.False(older.WriteGameThumbnails);
        Assert.False(AppSettings.Default.WriteGameThumbnails);

        SettingsStore.Save(path, AppSettings.Default with { WriteGameThumbnails = true });

        Assert.True(SettingsStore.Load(path).WriteGameThumbnails);
        Assert.Contains("\"writeGameThumbnails\": true", File.ReadAllText(path));
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
}
