using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class MapCatalogTests
{
    private static LevelDesc Level(string name, string dir, params string[] assets) =>
        new(name, dir, new CameraBounds(0, 0, 100, 50),
            [new LevelBackground("BG_" + dir + ".jpg", null, null)],
            [new PlatformNode(0, 0, 1, 1, 1, 0, null,
                assets.Select(a => new LevelAsset(a, 0, 0, 1, 1)).ToList(), [])]);

    private static LevelDataModel Model(
        IEnumerable<LevelDesc> levels, IEnumerable<LevelType> types, IEnumerable<LevelSet> sets) =>
        new(levels.ToList(), types.ToList(), sets.ToList(), DateTimeOffset.UtcNow);

    private static LevelType Type(string name, string displayName) => new(name, displayName, false, false);

    [Fact]
    public void Build_MakesOneMapPerFolderThatALevelPointsAt()
    {
        var data = Model(
            [Level("Grove", "Grove"), Level("SmallGrove", "Grove"), Level("Enigma", "Enigma")],
            [Type("Grove", "Twilight Grove"), Type("SmallGrove", "Small Grove"), Type("Enigma", "Enigma")],
            []);

        var catalog = MapCatalog.Build(data);

        Assert.Equal(["Enigma", "Grove"], catalog.Maps.Select(m => m.FolderName));
        Assert.Equal(2, catalog.ByFolder("Grove")!.Levels.Count);
        Assert.True(catalog.HasLevelData);
    }

    [Fact]
    public void Build_ExcludesDevOnlyAndTestLevelsAndTheirFolders()
    {
        var data = Model(
            [Level("Grove", "Grove"), Level("DevRoom", "DevRoom"), Level("TestBox", "Test"), Level("Orphan", "Orphan")],
            [
                Type("Grove", "Twilight Grove"),
                new LevelType("DevRoom", "Dev Room", true, false),
                new LevelType("TestBox", "Test Box", false, true),
            ],
            []);

        var catalog = MapCatalog.Build(data);

        Assert.Equal(["Grove"], catalog.Maps.Select(m => m.FolderName));
        Assert.Null(catalog.ByFolder("DevRoom"));
        Assert.Null(catalog.ByFolder("Test"));
        Assert.Null(catalog.ByFolder("Orphan"));
    }

    [Fact]
    public void Build_PrefersTheLevelNamedLikeTheFolderAsTheBaseLevel()
    {
        var data = Model(
            [Level("SmallGrove", "Grove"), Level("Grove", "Grove"), Level("GroveNight", "Grove")],
            [Type("SmallGrove", "Small Grove"), Type("Grove", "Twilight Grove"), Type("GroveNight", "Dusk")],
            []);

        var catalog = MapCatalog.Build(data);

        var grove = catalog.ByFolder("Grove")!;
        Assert.Equal("Grove", grove.BaseLevel.LevelName);
        Assert.Equal("Twilight Grove", grove.DisplayName);
    }

    [Fact]
    public void Build_FallsBackToTheShortestDisplayNameSkippingSmallBigTutorialAndMiniGames()
    {
        var data = Model(
            [
                Level("SmallBeach", "Beach"),
                Level("BeachbrawlArena", "Beach"),
                Level("BigBeachParty", "Beach"),
                Level("SunsetBeach", "Beach"),
                Level("SmallBombs", "Bombs"),
                Level("CatchBombs", "Bombs"),
            ],
            [
                Type("SmallBeach", "Small Beach"),
                Type("BeachbrawlArena", "Beachbrawl Arena"),
                Type("BigBeachParty", "Big Beach Party"),
                Type("SunsetBeach", "Sunset Beach"),
                Type("SmallBombs", "Small Bombs"),
                Type("CatchBombs", "Catch Bombs"),
            ],
            []);

        var catalog = MapCatalog.Build(data);

        Assert.Equal("Sunset Beach", catalog.ByFolder("Beach")!.DisplayName);
        // Every Bombs level is skipped by the filter, so the shortest display name overall wins the tie.
        Assert.Equal("Catch Bombs", catalog.ByFolder("Bombs")!.DisplayName);
    }

    [Fact]
    public void Build_PutsAMapInASetWhenAnyOfItsLevelsIsInThatSet()
    {
        var data = Model(
            [Level("Grove", "Grove"), Level("SmallGrove", "Grove"), Level("Enigma", "Enigma")],
            [Type("Grove", "Twilight Grove"), Type("SmallGrove", "Small Grove"), Type("Enigma", "Enigma")],
            [new LevelSet("Ranked1v1", ["SmallGrove"]), new LevelSet("Ranked2v2", ["Grove"])]);

        var catalog = MapCatalog.Build(data);

        Assert.Equal(["Ranked1v1", "Ranked2v2"], catalog.ByFolder("Grove")!.Sets);
        Assert.Empty(catalog.ByFolder("Enigma")!.Sets);
    }

    [Fact]
    public void Build_UsesRankedSetsWhenPresentAndStandardSetsOtherwise()
    {
        LevelDesc[] levels = [Level("Grove", "Grove")];
        LevelType[] types = [Type("Grove", "Twilight Grove")];
        LevelSet[] ranked =
        [
            new("Ranked1v1", ["Grove"]),
            new("Ranked2v2", ["Grove"]),
            new("Tournament1v1", ["Grove"]),
        ];
        LevelSet[] standard =
        [
            new("Standard1v1", ["Grove"]),
            new("Standard2v2", ["Grove"]),
            new("Tournament1v1", ["Grove"]),
        ];

        var rankedCatalog = MapCatalog.Build(Model(levels, types, ranked));

        Assert.Equal(MapCatalog.RankedSetNames, rankedCatalog.UiSetNames);
        Assert.Equal(["Ranked 1v1", "Ranked 2v2", "Tournament"], rankedCatalog.UiSets.Select(s => s.Label));
        Assert.Equal(MapCatalog.StandardSetNames, MapCatalog.Build(Model(levels, types, standard)).UiSetNames);
        Assert.Equal(
            ["Standard 1v1", "Standard 2v2", "Tournament"],
            MapCatalog.Build(Model(levels, types, standard)).UiSets.Select(s => s.Label));
        Assert.Equal(
            ["Standard1v1", "Standard2v2"],
            MapCatalog.Build(Model(levels, types, standard.Take(2))).UiSetNames);
    }

    [Fact]
    public void Build_CollectsDistinctBackgroundSlotsAndPlatformFilesIncludingParentReferences()
    {
        var data = Model(
            [Level("Grove", "Grove", "a.png", "../Snow/Snow1.png", "a.png"), Level("SmallGrove", "Grove", "b.png")],
            [Type("Grove", "Twilight Grove"), Type("SmallGrove", "Small Grove")],
            []);

        var grove = MapCatalog.Build(data).ByFolder("Grove")!;

        Assert.Equal(["BG_Grove.jpg"], grove.BackgroundSlots);
        Assert.Equal([@"Grove\a.png", @"Snow\Snow1.png", @"Grove\b.png"], grove.PlatformFiles);
    }

    [Fact]
    public void Build_CollectsAPlatformFileNamedOnThePlatformElementItself()
    {
        var level = LevelDescParser.Parse(LevelXml.Level("Enigma", "Enigma", LevelXml.Camera(0, 0, 100, 50)
            + "<Background AssetName=\"BG_Steam.jpg\" />"
            + "<Platform AssetName=\"Platform_Steam1A.png\" W=\"1044.03\" H=\"1227.92\">"
            + "<Asset AssetName=\"Platform_Steam2B.png\" X=\"0\" Y=\"0\" W=\"1\" H=\"1\" /></Platform>"));
        var data = Model([level], [Type("Enigma", "Enigma")], []);

        var enigma = MapCatalog.Build(data).ByFolder("Enigma")!;

        Assert.Equal([@"Enigma\Platform_Steam1A.png", @"Enigma\Platform_Steam2B.png"], enigma.PlatformFiles);
    }

    [Fact]
    public void FromFolders_BuildsAFolderOnlyCatalogWhenLevelDataIsUnavailable()
    {
        using var tmp = new TempDir();
        FakeGameTree.Standard(tmp.Path);

        var catalog = MapCatalog.FromFolders(GameTreeScanner.Scan(tmp.Path));

        Assert.False(catalog.HasLevelData);
        Assert.Empty(catalog.UiSetNames);
        Assert.Null(catalog.ByFolder("Backgrounds"));
        var bloodMoon = catalog.ByFolder("BloodMoon");
        Assert.NotNull(bloodMoon);
        Assert.Equal("BloodMoon", bloodMoon!.DisplayName);
        Assert.Equal("BloodMoon", bloodMoon.BaseLevel.LevelName);
        Assert.Empty(bloodMoon.Sets);
        Assert.Empty(bloodMoon.BackgroundSlots);
        Assert.Contains(@"BloodMoon\BloodMoon_PlatformA01.png", bloodMoon.PlatformFiles);
    }
}
