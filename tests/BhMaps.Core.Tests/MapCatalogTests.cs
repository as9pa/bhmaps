using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class MapCatalogTests
{
    private static LevelDesc Level(string name, string dir, params string[] assets) =>
        Tree(name, dir, Node(null, assets));

    private static LevelDesc Tree(string name, string dir, params PlatformNode[] nodes) =>
        new(name, dir, new CameraBounds(0, 0, 100, 50),
            [new LevelBackground("BG_" + dir + ".jpg", null, null)],
            nodes);

    private static PlatformNode Node(string? theme, string[] assets, params PlatformNode[] children) =>
        new(0, 0, 1, 1, 1, 0, theme, assets.Select(a => new LevelAsset(a, 0, 0, 1, 1)).ToList(), children);

    private static LevelDataModel Model(
        IEnumerable<LevelDesc> levels, IEnumerable<LevelType> types, IEnumerable<LevelSet> sets) =>
        new(levels.ToList(), types.ToList(), sets.ToList(), DateTimeOffset.UtcNow);

    private static LevelType Type(string name, string displayName) => new(name, displayName, false, false);

    /// <summary>A catalog holding exactly these maps, built through <see cref="MapCatalog.Build"/> from each
    /// entry's base level so the entries come back the way the real catalog would make them.</summary>
    internal static MapCatalog CatalogOf(params MapEntry[] maps) =>
        MapCatalog.Build(Model(
            maps.Select(m => m.BaseLevel),
            maps.Select(m => Type(m.BaseLevel.LevelName, m.DisplayName)),
            []));

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
            [
                Tree("Grove", "Grove", Node(null, ["a.png", "../Grove/a.png"])),
                Level("SmallGrove", "Grove", "b.png"),
            ],
            [Type("Grove", "Twilight Grove"), Type("SmallGrove", "Small Grove")],
            []);

        var grove = MapCatalog.Build(data).ByFolder("Grove")!;

        Assert.Equal(["BG_Grove.jpg"], grove.BackgroundSlots);
        Assert.Equal([@"Grove\a.png", @"Grove\b.png"], grove.PlatformFiles);
    }

    [Fact]
    public void Build_SkipsThemedNodesAndTheirChildren()
    {
        var data = Model(
            [
                Tree(
                    "Grove",
                    "Grove",
                    Node(null, ["a.png"]),
                    Node("Snow", ["../Snow/Snow1.png"], Node(null, ["../Snow/Snow2.png"]))),
            ],
            [Type("Grove", "Twilight Grove")],
            []);

        var grove = MapCatalog.Build(data).ByFolder("Grove")!;

        Assert.Equal([@"Grove\a.png"], grove.PlatformFiles);
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
    public void Build_NamesTheThumbnailFileWhenOneMapOwnsIt()
    {
        var data = Model(
            [Level("Grove", "Grove"), Level("SmallGrove", "Grove")],
            [
                new LevelType("Grove", "Twilight Grove", false, false, "A.jpg"),
                new LevelType("SmallGrove", "Small Grove", false, false, "A.jpg"),
            ],
            []);

        var catalog = MapCatalog.Build(data);

        Assert.Equal("A.jpg", catalog.ByFolder("Grove")!.ThumbnailFile);
    }

    [Fact]
    public void Build_DropsAThumbnailFileTwoMapsShare()
    {
        var data = Model(
            [Level("X", "X"), Level("Y", "Y")],
            [
                new LevelType("X", "X", false, false, "Shared.jpg"),
                new LevelType("Y", "Y", false, false, "Shared.jpg"),
            ],
            []);

        var catalog = MapCatalog.Build(data);

        Assert.Null(catalog.ByFolder("X")!.ThumbnailFile);
        Assert.Empty(catalog.ByFolder("X")!.ThumbnailFiles);
        Assert.Null(catalog.ByFolder("Y")!.ThumbnailFile);
    }

    [Fact]
    public void Build_OwnsBothFilesWhenAFoldersTwoLevelsNameDifferentOnes()
    {
        var data = Model(
            [Level("Fortress", "Fortress"), Level("SmallFortress", "Fortress")],
            [
                new LevelType("Fortress", "Mammoth Fortress", false, false, "Mammoth.jpg"),
                new LevelType("SmallFortress", "Small Mammoth Fortress", false, false, "MammothSmall.jpg"),
            ],
            []);

        var fortress = MapCatalog.Build(data).ByFolder("Fortress")!;

        Assert.Equal(["Mammoth.jpg", "MammothSmall.jpg"], fortress.ThumbnailFiles);
        Assert.Equal("Mammoth.jpg", fortress.ThumbnailFile);
    }

    [Fact]
    public void Build_LinksEachOwnedFileToTheLevelThatNamesIt()
    {
        var data = Model(
            [Level("Fortress", "Fortress"), Level("SmallFortress", "Fortress")],
            [
                new LevelType("Fortress", "Mammoth Fortress", false, false, "Mammoth.jpg"),
                new LevelType("SmallFortress", "Small Mammoth Fortress", false, false, "MammothSmall.jpg"),
            ],
            []);

        var fortress = MapCatalog.Build(data).ByFolder("Fortress")!;

        Assert.Equal("Fortress", fortress.LevelFor("Mammoth.jpg").LevelName);
        Assert.Equal("SmallFortress", fortress.LevelFor("MammothSmall.jpg").LevelName);
        Assert.Same(fortress.BaseLevel, fortress.LevelFor("NotAFileOfThisMap.jpg"));
    }

    [Fact]
    public void Build_GivesAFileTwoOfAFoldersLevelsNameToTheBaseLevel()
    {
        var data = Model(
            [Level("SmallFortress", "Fortress"), Level("Fortress", "Fortress")],
            [
                new LevelType("SmallFortress", "Small Mammoth Fortress", false, false, "Mammoth.jpg"),
                new LevelType("Fortress", "Mammoth Fortress", false, false, "Mammoth.jpg"),
            ],
            []);

        var fortress = MapCatalog.Build(data).ByFolder("Fortress")!;

        Assert.Equal(["Mammoth.jpg"], fortress.ThumbnailFiles);
        Assert.Same(fortress.BaseLevel, fortress.LevelFor("Mammoth.jpg"));
    }

    [Fact]
    public void Build_OwnsAllThreeFilesOfAThreeLevelFolderInLevelOrder()
    {
        var data = Model(
            [Level("BigGreatHall", "GreatHall"), Level("GreatHall", "GreatHall"), Level("SmallGreatHall", "GreatHall")],
            [
                new LevelType("BigGreatHall", "Big Great Hall", false, false, "biggreathall.jpg"),
                new LevelType("GreatHall", "Great Hall", false, false, "greathall.jpg"),
                new LevelType("SmallGreatHall", "Small Great Hall", false, false, "smallgreathall.jpg"),
            ],
            []);

        var hall = MapCatalog.Build(data).ByFolder("GreatHall")!;

        Assert.Equal(["biggreathall.jpg", "greathall.jpg", "smallgreathall.jpg"], hall.ThumbnailFiles);
    }

    [Fact]
    public void Build_LeavesOutOnlyTheFileAnotherFolderAlsoNames()
    {
        var data = Model(
            [Level("X", "X"), Level("SmallX", "X"), Level("Y", "Y")],
            [
                new LevelType("X", "X", false, false, "Own.jpg"),
                new LevelType("SmallX", "Small X", false, false, "Shared.jpg"),
                new LevelType("Y", "Y", false, false, "Shared.jpg"),
            ],
            []);

        var catalog = MapCatalog.Build(data);

        Assert.Equal(["Own.jpg"], catalog.ByFolder("X")!.ThumbnailFiles);
        Assert.Equal(["Own.jpg", "Shared.jpg"], catalog.ByFolder("X")!.Candidates);
        Assert.Empty(catalog.ByFolder("Y")!.ThumbnailFiles);
        Assert.Null(catalog.ByFolder("Y")!.ThumbnailFile);
    }

    [Fact]
    public void Build_OwnsNothingWhenAMapsLevelsNameNoFile()
    {
        var data = Model([Level("Grove", "Grove")], [Type("Grove", "Twilight Grove")], []);

        var grove = MapCatalog.Build(data).ByFolder("Grove")!;

        Assert.Empty(grove.ThumbnailFiles);
        Assert.Empty(grove.Candidates);
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

    [Fact]
    public void SetsSentence_SaysOnlyTheSetsAPlayerKnows_InOneFixedOrder()
    {
        Assert.Equal(
            "Ranked 1v1, Standard, Experimental",
            MapCatalog.SetsSentence(["StandardAll", "Ranked1v1", "Experimental1v1"]));
    }

    [Fact]
    public void SetsSentence_KeepsItsOrderWhateverOrderTheSetsArriveIn()
    {
        Assert.Equal(
            "Ranked 1v1, Standard, Experimental",
            MapCatalog.SetsSentence(["Experimental1v1", "Standard1v1", "Ranked1v1", "Standard2v2"]));
    }

    [Fact]
    public void SetsSentence_SaysMinigamesForTheGameModeSet()
    {
        Assert.Equal("Minigames", MapCatalog.SetsSentence(["GameModeAll"]));
    }

    [Fact]
    public void SetsSentence_FoldsBothTournamentSetsIntoOneWord()
    {
        Assert.Equal("Tournament", MapCatalog.SetsSentence(["Tournament1v1", "Tournament2v2"]));
    }

    [Fact]
    public void SetsSentence_SaysOtherModesWhenNoSetHasAWordOfItsOwn()
    {
        Assert.Equal("Other modes", MapCatalog.SetsSentence(["Tutorial1"]));
        Assert.Equal("Other modes", MapCatalog.SetsSentence([]));
    }

    private static MapEntry MapIn(params string[] sets) =>
        new("Brawlball", "Brawlball", Level("Brawlball", "Brawlball"), [], sets, [], []);

    [Fact]
    public void IsMinigame_IsTrueForAMapNoSetButTheModeSetHolds()
    {
        Assert.True(MapCatalog.IsMinigame(MapIn("GameModeAll")));
    }

    [Fact]
    public void IsMinigame_IsFalseForAMapAPlayerCanAlsoPick()
    {
        Assert.False(MapCatalog.IsMinigame(MapIn("GameModeAll", "StandardAll")));
        Assert.False(MapCatalog.IsMinigame(MapIn("GameModeAll", "Ranked1v1")));
        Assert.False(MapCatalog.IsMinigame(MapIn("GameModeAll", "Tournament1v1")));
    }

    [Fact]
    public void IsMinigame_IgnoresARotationSetThatOnlyStartsLikeARankedOne()
    {
        // Brawlball's big level sits in "Ranked3v3BrawlballOGMapOnly", a rotation set nobody picks a map from.
        Assert.True(MapCatalog.IsMinigame(MapIn("GameModeAll", "BrawlballBig", "Ranked3v3BrawlballOGMapOnly")));
    }

    [Fact]
    public void IsMinigame_IsFalseForAMapOutsideTheModeSet()
    {
        Assert.False(MapCatalog.IsMinigame(MapIn("Standard1v1")));
        Assert.False(MapCatalog.IsMinigame(MapIn()));
    }

    [Fact]
    public void Build_PutsTheMinigameChipLastAfterTheSetsAPlayerPicksFrom()
    {
        LevelDesc[] levels = [Level("Grove", "Grove")];
        LevelType[] types = [Type("Grove", "Twilight Grove")];
        LevelSet[] sets =
        [
            new("GameModeAll", ["Grove"]),
            new("Ranked1v1", ["Grove"]),
            new("Ranked2v2", ["Grove"]),
            new("Tournament1v1", ["Grove"]),
        ];

        var catalog = MapCatalog.Build(Model(levels, types, sets));

        Assert.Equal(
            ["Ranked 1v1", "Ranked 2v2", "Tournament", "Minigames"],
            catalog.UiSets.Select(s => s.Label));
    }
}
