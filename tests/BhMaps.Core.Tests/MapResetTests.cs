using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class MapResetTests
{
    private static Pack ScanDefault(string libraryPath) => DefaultPack.Find(PackScanner.ScanAll(libraryPath))!;

    private static string PackPath(string libraryPath, params string[] parts) =>
        Path.Combine([PackScanner.PacksRoot(libraryPath), DefaultPack.Name, .. parts]);

    /// <summary>The folder a capture builds in before it becomes the Default pack. Spelled out rather than read
    /// from DefaultPack, because the point of the tests below is that nothing of it is left on disk.</summary>
    private static string CapturingPath(string libraryPath) =>
        Path.Combine(PackScanner.PacksRoot(libraryPath), "Default.capturing");

    private static string Read(string root, string folder, string name) => File.ReadAllText(Path.Combine(root, folder, name));

    [Fact]
    public void Find_MatchesTheDefaultPackCaseInsensitively()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(Path.Combine(lib, "packs", "default")).File("Grove", "a.png", "x");
        new FakeGameTree(Path.Combine(lib, "packs", "dark")).File("Grove", "a.png", "x");

        var found = DefaultPack.Find(PackScanner.ScanAll(lib));

        Assert.NotNull(found);
        Assert.Equal("default", found.Name);
    }

    [Fact]
    public void Find_ReturnsNullWhenNoDefaultPackExists()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(Path.Combine(lib, "packs", "dark")).File("Grove", "a.png", "x");

        Assert.Null(DefaultPack.Find(PackScanner.ScanAll(lib)));
        Assert.False(DefaultPack.Exists(lib));
    }

    [Fact]
    public void Capture_ReplacesAnExistingDefaultPackRatherThanMergingIntoIt()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        FakeGameTree.Standard(game);
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(PackPath(lib)).File("Stale", "old.png", "stale");
        Assert.True(DefaultPack.Exists(lib));

        var result = DefaultPack.Capture(game, lib);

        Assert.Equal(0, result.Failed);
        Assert.False(Directory.Exists(PackPath(lib, "Stale")));
        var captured = ScanDefault(lib);
        Assert.Equal(8, captured.Folders.Count);
        foreach (var folder in GameTreeScanner.Scan(game).Folders)
        {
            Assert.NotNull(captured.FindFolder(folder.Name));
        }
    }

    [Fact]
    public void Capture_CopiesEveryGameFolderIntoPacksDefault()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        FakeGameTree.Standard(game);
        var lib = Path.Combine(tmp.Path, "lib");
        var progress = new List<string>();

        var result = DefaultPack.Capture(game, lib, new SyncProgress(progress));

        Assert.Equal(13, result.Copied);
        Assert.Empty(result.Failures);
        Assert.True(DefaultPack.Exists(lib));
        Assert.True(File.Exists(PackPath(lib, "BloodMoon", "BloodMoon_PlatformA01.png")));
        Assert.Equal("bm-a01", Read(PackPath(lib), "BloodMoon", "BloodMoon_PlatformA01.png"));
        Assert.Equal("bg-sewer", Read(PackPath(lib), "Backgrounds", "BG_Sewer.jpg"));
        Assert.Equal(13, progress.Count);
    }

    [Fact]
    public void Capture_SwapsTheCapturedFolderInAndLeavesNoTemporaryFolderBehind()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        FakeGameTree.Standard(game);
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(PackPath(lib)).File("Stale", "old.png", "stale");
        new FakeGameTree(CapturingPath(lib)).File("Junk", "junk.png", "junk");

        var result = DefaultPack.Capture(game, lib);

        Assert.Equal(13, result.Copied);
        Assert.Empty(result.Failures);
        Assert.False(Directory.Exists(PackPath(lib, "Stale")));
        Assert.False(Directory.Exists(PackPath(lib, "Junk")));
        Assert.Equal("bm-a01", Read(PackPath(lib), "BloodMoon", "BloodMoon_PlatformA01.png"));
        Assert.Equal(
            new[] { DefaultPack.Name },
            Directory.GetDirectories(PackScanner.PacksRoot(lib)).Select(Path.GetFileName));
    }

    [Fact]
    public void Capture_LeavesTheOldDefaultPackUntouchedWhenCancelledPartWay()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        FakeGameTree.Standard(game);
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(PackPath(lib)).File("Stale", "old.png", "stale");
        using var cts = new CancellationTokenSource();

        Assert.Throws<OperationCanceledException>(
            () => DefaultPack.Capture(game, lib, new CancelsOnFirstReport(cts), cts.Token));

        Assert.Equal(new[] { Path.Combine("Stale", "old.png") }, ScanDefault(lib).RelativePaths);
        Assert.Equal("stale", Read(PackPath(lib), "Stale", "old.png"));
        Assert.False(Directory.Exists(CapturingPath(lib)));
    }

    [Fact]
    public void ResetMap_RestoresTheDefaultPackFilesForThatFolder()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        new FakeGameTree(game).File("Grove", "a.png", "bad");
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(PackPath(lib)).File("Grove", "a.png", "good");

        var outcome = MapReset.ResetMap(game, "Grove", [], ScanDefault(lib));

        Assert.Equal(1, outcome.Restored);
        Assert.Equal(0, outcome.Deleted);
        Assert.Equal(1, outcome.Changed);
        Assert.Equal(0, outcome.Failed);
        Assert.Equal("good", Read(game, "Grove", "a.png"));
    }

    [Fact]
    public void ResetMap_AlsoRestoresTheMapsBackgroundSlots()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        new FakeGameTree(game).File("Grove", "a.png", "bad").File("Backgrounds", "BG_Grove.jpg", "bad-bg");
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(PackPath(lib))
            .File("Grove", "a.png", "good")
            .File("Backgrounds", "BG_Grove.jpg", "good-bg")
            .File("Backgrounds", "BG_Sewer.jpg", "other-bg");

        var outcome = MapReset.ResetMap(game, "Grove", ["BG_Grove.jpg"], ScanDefault(lib));

        Assert.Equal(2, outcome.Restored);
        Assert.Equal(0, outcome.Failed);
        Assert.Equal("good-bg", Read(game, "Backgrounds", "BG_Grove.jpg"));
        Assert.False(File.Exists(Path.Combine(game, "Backgrounds", "BG_Sewer.jpg")));
    }

    [Fact]
    public void ResetMap_IgnoresSlotsTheDefaultPackDoesNotHave()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        new FakeGameTree(game).File("Grove", "a.png", "bad");
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(PackPath(lib)).File("Grove", "a.png", "good").File("Backgrounds", "BG_Grove.jpg", "good-bg");

        var outcome = MapReset.ResetMap(game, "Grove", ["BG_Grove.jpg", "BG_Gone.jpg"], ScanDefault(lib));

        Assert.Equal(2, outcome.Restored);
        Assert.Empty(outcome.Failures);
        Assert.False(File.Exists(Path.Combine(game, "Backgrounds", "BG_Gone.jpg")));
    }

    [Fact]
    public void ResetMap_DeletesTheFolderImagesWhenThereIsNoDefaultPack()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        new FakeGameTree(game)
            .File("Grove", "a.png", "x")
            .File("Grove", "b.jpg", "x")
            .File("Grove", "notes.txt", "x")
            .File("Backgrounds", "BG_Grove.jpg", "keep");

        var outcome = MapReset.ResetMap(game, "Grove", ["BG_Grove.jpg"], defaultPack: null);

        Assert.Equal(0, outcome.Restored);
        Assert.Equal(2, outcome.Deleted);
        Assert.Equal(0, outcome.Failed);
        Assert.Equal(new[] { "notes.txt" }, Directory.GetFiles(Path.Combine(game, "Grove")).Select(Path.GetFileName));
        Assert.Equal("keep", Read(game, "Backgrounds", "BG_Grove.jpg"));
    }

    [Fact]
    public void ResetPlatforms_RestoresTheFolderAndLeavesTheBackgroundsAlone()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        new FakeGameTree(game).File("Grove", "a.png", "bad").File("Backgrounds", "BG_Grove.jpg", "mine");
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(PackPath(lib)).File("Grove", "a.png", "good").File("Backgrounds", "BG_Grove.jpg", "good-bg");

        var outcome = MapReset.ResetPlatforms(game, "Grove", ScanDefault(lib));

        Assert.Equal(1, outcome.Restored);
        Assert.Equal(0, outcome.Failed);
        Assert.Equal("good", Read(game, "Grove", "a.png"));
        Assert.Equal("mine", Read(game, "Backgrounds", "BG_Grove.jpg"));
    }

    [Fact]
    public void ResetPlatforms_DeletesTheFolderImagesWhenThereIsNoDefaultPack()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        new FakeGameTree(game)
            .File("Grove", "a.png", "x")
            .File("Grove", "notes.txt", "x")
            .File("Backgrounds", "BG_Grove.jpg", "keep");

        var outcome = MapReset.ResetPlatforms(game, "Grove", defaultPack: null);

        Assert.Equal(1, outcome.Deleted);
        Assert.Equal(0, outcome.Failed);
        Assert.Equal(new[] { "notes.txt" }, Directory.GetFiles(Path.Combine(game, "Grove")).Select(Path.GetFileName));
        Assert.Equal("keep", Read(game, "Backgrounds", "BG_Grove.jpg"));
    }

    [Fact]
    public void ResetBackgrounds_RestoresTheSlotsAndLeavesTheMapFolderAlone()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        new FakeGameTree(game).File("Grove", "a.png", "mine").File("Backgrounds", "BG_Grove.jpg", "bad-bg");
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(PackPath(lib)).File("Grove", "a.png", "good").File("Backgrounds", "BG_Grove.jpg", "good-bg");

        var outcome = MapReset.ResetBackgrounds(game, ["BG_Grove.jpg", "BG_Gone.jpg"], ScanDefault(lib));

        Assert.Equal(1, outcome.Restored);
        Assert.Empty(outcome.Failures);
        Assert.Equal("good-bg", Read(game, "Backgrounds", "BG_Grove.jpg"));
        Assert.Equal("mine", Read(game, "Grove", "a.png"));
    }

    [Fact]
    public void ResetBackgrounds_DeletesTheSlotsWhenThereIsNoDefaultPack()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        new FakeGameTree(game)
            .File("Grove", "a.png", "keep")
            .File("Backgrounds", "BG_Grove.jpg", "x")
            .File("Backgrounds", "BG_Sewer.jpg", "keep");

        var outcome = MapReset.ResetBackgrounds(game, ["BG_Grove.jpg"], defaultPack: null);

        Assert.Equal(0, outcome.Restored);
        Assert.Equal(1, outcome.Deleted);
        Assert.Equal(0, outcome.Failed);
        Assert.False(File.Exists(Path.Combine(game, "Backgrounds", "BG_Grove.jpg")));
        Assert.Equal("keep", Read(game, "Backgrounds", "BG_Sewer.jpg"));
        Assert.Equal("keep", Read(game, "Grove", "a.png"));
    }

    [Fact]
    public void ResetAll_AppliesTheWholeDefaultPackWhenThereIsOne()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        new FakeGameTree(game)
            .File("Grove", "a.png", "bad")
            .File("Backgrounds", "BG_Grove.jpg", "bad-bg")
            .File("Snow", "Snow1.png", "bad-snow")
            .File("Grove", "custom.png", "custom");
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(PackPath(lib))
            .File("Grove", "a.png", "good")
            .File("Backgrounds", "BG_Grove.jpg", "good-bg")
            .File("Snow", "Snow1.png", "good-snow");

        var outcome = MapReset.ResetAll(game, ScanDefault(lib));

        Assert.Equal(3, outcome.Restored);
        Assert.Equal(0, outcome.Deleted);
        Assert.Equal(0, outcome.Failed);
        Assert.Equal("good", Read(game, "Grove", "a.png"));
        Assert.Equal("good-bg", Read(game, "Backgrounds", "BG_Grove.jpg"));
        Assert.Equal("good-snow", Read(game, "Snow", "Snow1.png"));
        Assert.Equal("custom", Read(game, "Grove", "custom.png"));
    }

    [Fact]
    public void ResetAll_FallsBackToDeletingEveryFolderWhenThereIsNoDefaultPack()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        FakeGameTree.Standard(game).File("Snow", "Snow1.png", "theme").File("Test", "Test1.png", "theme");

        var outcome = MapReset.ResetAll(game, defaultPack: null);

        Assert.Equal(0, outcome.Restored);
        Assert.Equal(15, outcome.Deleted);
        Assert.Equal(0, outcome.Failed);
        Assert.Equal(10, Directory.GetDirectories(game).Length);
        Assert.Empty(Directory.GetFiles(game, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void ResetAll_ReportsProgressPerFolder()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        FakeGameTree.Standard(game);
        var lib = Path.Combine(tmp.Path, "lib");
        DefaultPack.Capture(game, lib);
        var fromPack = new List<string>();
        var fromDelete = new List<string>();

        MapReset.ResetAll(game, ScanDefault(lib), new SyncProgress(fromPack));
        MapReset.ResetAll(game, null, new SyncProgress(fromDelete));

        Assert.NotEmpty(fromPack);
        Assert.NotEmpty(fromDelete);
    }

    /// <summary>A progress sink that cancels the source as the first file is reported, so "cancelled once the copy
    /// was under way" is a fact of the run rather than a race with a timer.</summary>
    private sealed class CancelsOnFirstReport(CancellationTokenSource cts) : IProgress<string>
    {
        public void Report(string value) => cts.Cancel();
    }
}
