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
        Assert.Equal(@"C:\Users\alexa\files\bh", AppSettings.DefaultLibraryPath);
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
}
