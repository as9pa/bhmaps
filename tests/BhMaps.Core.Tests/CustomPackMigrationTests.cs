using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Settings;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class CustomPackMigrationTests
{
    private static readonly DateTimeOffset Noted = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private static AppSettings Fresh(string lib) => new(@"D:\game\mapArt", lib, true);

    /// <summary>A library at tmp\lib with one picture in each named pack.</summary>
    private static string Library(TempDir tmp, params string[] packs)
    {
        var lib = Path.Combine(tmp.Path, "lib");
        foreach (var pack in packs)
        {
            var file = Path.Combine(lib, "packs", pack, "Backgrounds", "sunset.jpg");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, pack);
        }

        return lib;
    }

    [Fact]
    public void RenamesMyBackgroundsToCustomPackAndSetsTheFlag()
    {
        using var tmp = new TempDir();
        var lib = Library(tmp, "My Backgrounds", "flowermap");

        var migrated = CustomPackMigration.Run(lib, Path.Combine(tmp.Path, "applied.json"), Fresh(lib));

        Assert.NotNull(migrated);
        Assert.True(migrated.MyBackgroundsMigrated);
        Assert.False(Directory.Exists(Path.Combine(lib, "packs", "My Backgrounds")));
        Assert.Equal(
            "My Backgrounds",
            File.ReadAllText(Path.Combine(lib, "packs", "Custom Pack", "Backgrounds", "sunset.jpg")));
        Assert.True(Directory.Exists(Path.Combine(lib, "packs", "flowermap")));
    }

    [Fact]
    public void TakesTheNextFreeNameWhenCustomPackExists()
    {
        using var tmp = new TempDir();
        var lib = Library(tmp, "my backgrounds", "Custom Pack");

        var migrated = CustomPackMigration.Run(lib, Path.Combine(tmp.Path, "applied.json"), Fresh(lib));

        Assert.NotNull(migrated);
        Assert.Equal(
            "my backgrounds",
            File.ReadAllText(Path.Combine(lib, "packs", "Custom Pack 2", "Backgrounds", "sunset.jpg")));
        Assert.Equal(
            "Custom Pack",
            File.ReadAllText(Path.Combine(lib, "packs", "Custom Pack", "Backgrounds", "sunset.jpg")));
    }

    [Fact]
    public void LeavesMyBackgroundsAloneOnceTheFlagIsSet()
    {
        using var tmp = new TempDir();
        var lib = Library(tmp, "My Backgrounds");

        var migrated = CustomPackMigration.Run(
            lib, Path.Combine(tmp.Path, "applied.json"), Fresh(lib) with { MyBackgroundsMigrated = true });

        Assert.Null(migrated);
        Assert.True(Directory.Exists(Path.Combine(lib, "packs", "My Backgrounds")));
        Assert.False(Directory.Exists(Path.Combine(lib, "packs", "Custom Pack")));
    }

    [Fact]
    public void NothingToRenameStillSetsTheFlag()
    {
        using var tmp = new TempDir();
        var lib = Library(tmp, "flowermap");

        var migrated = CustomPackMigration.Run(lib, Path.Combine(tmp.Path, "applied.json"), Fresh(lib));

        Assert.NotNull(migrated);
        Assert.True(migrated.MyBackgroundsMigrated);
        Assert.False(Directory.Exists(Path.Combine(lib, "packs", "Custom Pack")));
    }

    [Fact]
    public void FailedRenameSavesNothingSoTheNextStartTriesAgain()
    {
        using var tmp = new TempDir();
        var lib = Library(tmp, "My Backgrounds");
        var held = Path.Combine(lib, "packs", "My Backgrounds", "Backgrounds", "sunset.jpg");

        AppSettings? migrated;
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            migrated = CustomPackMigration.Run(lib, Path.Combine(tmp.Path, "applied.json"), Fresh(lib));
        }

        Assert.Null(migrated);
        Assert.True(Directory.Exists(Path.Combine(lib, "packs", "My Backgrounds")));
    }

    [Fact]
    public void UpdatesTheAppliedRecordAndPackSettingsTheWayRenameDoes()
    {
        using var tmp = new TempDir();
        var lib = Library(tmp, "My Backgrounds");
        var game = Path.Combine(tmp.Path, "game");
        new FakeGameTree(game).File("Grove", "BG_Grove.jpg", "My Backgrounds");
        var record = Path.Combine(tmp.Path, "applied.json");
        AppliedRecord.Note(
            record,
            game,
            lib,
            [new AppliedSource(
                "Grove\\BG_Grove.jpg",
                Path.Combine(lib, "packs", "My Backgrounds", "Backgrounds", "sunset.jpg"),
                "My Backgrounds")],
            Noted,
            _ => null);
        var settings = Fresh(lib) with
        {
            HiddenPackNames = ["My Backgrounds"],
            PackLastApplied = new Dictionary<string, DateTimeOffset> { ["my backgrounds"] = Noted },
        };

        var migrated = CustomPackMigration.Run(lib, record, settings);

        Assert.NotNull(migrated);
        Assert.Equal(["Custom Pack"], migrated.HiddenPacks);
        Assert.Equal(Noted, migrated.LastApplied["Custom Pack"]);
        var entry = AppliedRecord.Load(record).Entries["Grove\\BG_Grove.jpg"];
        Assert.Equal("Custom Pack", entry.Pack);
        Assert.Equal(Path.Combine("packs", "Custom Pack", "Backgrounds", "sunset.jpg"), entry.Source);
    }

    [Fact]
    public void TheFlagSurvivesASaveAndLoad()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "settings.json");

        SettingsStore.Save(path, Fresh(@"D:\lib") with { MyBackgroundsMigrated = true });

        Assert.True(SettingsStore.Load(path).MyBackgroundsMigrated);
        Assert.False(SettingsStore.Load(Path.Combine(tmp.Path, "missing.json")).MyBackgroundsMigrated);
    }
}
