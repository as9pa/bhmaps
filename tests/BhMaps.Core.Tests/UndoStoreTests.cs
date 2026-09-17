using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class UndoStoreTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 9, 10, 13, 45, 7, TimeSpan.Zero);

    private static (UndoStore Store, string Game) Arrange(TempDir tmp)
    {
        var game = Path.Combine(tmp.Path, "game");
        new FakeGameTree(game).File("Grove", "a.png", "x").File("Grove", "b.png", "bee");
        return (new UndoStore(Path.Combine(tmp.Path, "appdata")), game);
    }

    [Fact]
    public void Begin_CreatesATimestampedFolderUnderUndo()
    {
        using var tmp = new TempDir();
        var (store, _) = Arrange(tmp);

        var session = store.Begin(Stamp);

        Assert.Equal(Path.Combine(tmp.Path, "appdata", "undo"), store.Root);
        Assert.Equal("20260910-134507", Path.GetFileName(session.Path));
        Assert.True(Directory.Exists(session.Path));
        Assert.Equal(0, session.Count);
    }

    [Fact]
    public void Begin_DeletesEveryEarlierSession()
    {
        using var tmp = new TempDir();
        var (store, game) = Arrange(tmp);
        var first = store.Begin(Stamp);
        first.Capture(game, "Grove\\a.png");

        var second = store.Begin(Stamp.AddMinutes(1));

        Assert.Equal(second.Path, Assert.Single(Directory.GetDirectories(store.Root)));
        Assert.False(Directory.Exists(first.Path));
    }

    [Fact]
    public void Begin_AppendsASuffixWhenTheSameSecondCollides()
    {
        using var tmp = new TempDir();
        var (store, _) = Arrange(tmp);

        var first = store.Begin(Stamp);
        var second = store.Begin(Stamp);

        Assert.NotEqual(first.Path, second.Path);
        Assert.Equal("20260910-134507-2", Path.GetFileName(second.Path));
    }

    [Fact]
    public void Capture_CopiesAnExistingGameFileIntoTheSession()
    {
        using var tmp = new TempDir();
        var (store, game) = Arrange(tmp);
        var session = store.Begin(Stamp);

        session.Capture(game, "Grove\\a.png");

        Assert.Equal(1, session.Count);
        Assert.Equal("x", File.ReadAllText(Path.Combine(session.Path, "Grove", "a.png")));
    }

    [Fact]
    public void MapFolderCount_CountsEachMapFolderOnce()
    {
        using var tmp = new TempDir();
        var (store, game) = Arrange(tmp);
        var session = store.Begin(Stamp);

        session.Capture(game, ["Grove\\a.png", "Grove\\b.png", "Swamp\\gone.png"]);

        Assert.Equal(3, session.Count);
        Assert.Equal(2, session.MapFolderCount());
    }

    [Fact]
    public void MapFolderCount_CountsOnlyTheFoldersTheCatalogKnows()
    {
        using var tmp = new TempDir();
        var (store, game) = Arrange(tmp);
        var session = store.Begin(Stamp);

        session.Capture(game, ["Grove\\a.png", "Backgrounds\\BG_Sewer.jpg", "Gone\\old.png"]);

        // The shared Backgrounds folder and a folder for a map the game no longer has are no map of the catalog's,
        // so the undo's done line does not count them. The match ignores case, as the game paths do.
        Assert.Equal(1, session.MapFolderCount(["grove", "Swamp"]));
    }

    [Fact]
    public void Capture_RecordsAMissingFileAsAbsent()
    {
        using var tmp = new TempDir();
        var (store, game) = Arrange(tmp);
        var session = store.Begin(Stamp);

        session.Capture(game, "Grove\\gone.png");

        Assert.Equal(1, session.Count);
        Assert.Equal(new[] { "Grove\\gone.png" }, File.ReadAllLines(Path.Combine(session.Path, "_absent.txt")));
        Assert.False(File.Exists(Path.Combine(session.Path, "Grove", "gone.png")));
    }

    [Fact]
    public void Restore_PutsTheOriginalBytesBackOverAModifiedFile()
    {
        using var tmp = new TempDir();
        var (store, game) = Arrange(tmp);
        var session = store.Begin(Stamp);
        session.Capture(game, "Grove\\a.png");
        File.WriteAllText(Path.Combine(game, "Grove", "a.png"), "y");

        var result = store.Restore(session, game);

        Assert.Equal(1, result.Copied);
        Assert.Equal(0, result.Failed);
        Assert.Equal("x", File.ReadAllText(Path.Combine(game, "Grove", "a.png")));
    }

    [Fact]
    public void Restore_DeletesFilesThatWereAbsentWhenTheOperationStarted()
    {
        using var tmp = new TempDir();
        var (store, game) = Arrange(tmp);
        var session = store.Begin(Stamp);
        session.Capture(game, "Grove\\new.png");
        File.WriteAllText(Path.Combine(game, "Grove", "new.png"), "written by the operation");

        var result = store.Restore(session, game);

        Assert.Equal(1, result.Copied);
        Assert.Empty(result.Failures);
        Assert.False(File.Exists(Path.Combine(game, "Grove", "new.png")));
        Assert.Equal("x", File.ReadAllText(Path.Combine(game, "Grove", "a.png")));
    }

    [Fact]
    public void Restore_RecordsAFailureAndContinuesWhenOneFileIsLocked()
    {
        using var tmp = new TempDir();
        var (store, game) = Arrange(tmp);
        var session = store.Begin(Stamp);
        session.Capture(game, new[] { "Grove\\a.png", "Grove\\b.png" });
        File.WriteAllText(Path.Combine(game, "Grove", "b.png"), "changed");
        var lockedPath = Path.Combine(game, "Grove", "a.png");

        using (new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = store.Restore(session, game);

            Assert.Equal(1, result.Copied);
            var failure = Assert.Single(result.Failures);
            Assert.Equal(lockedPath, failure.Path);
            Assert.NotEmpty(failure.Error);
        }

        Assert.Equal("bee", File.ReadAllText(Path.Combine(game, "Grove", "b.png")));
    }

    [Fact]
    public void Restore_DiscardsTheSnapshotWhenEveryFileWasRestored()
    {
        using var tmp = new TempDir();
        var (store, game) = Arrange(tmp);
        var session = store.Begin(Stamp);
        session.Capture(game, new[] { "Grove\\a.png", "Grove\\gone.png" });
        File.WriteAllText(Path.Combine(game, "Grove", "a.png"), "y");

        var result = store.Restore(session, game);

        Assert.Empty(result.Failures);
        Assert.Equal("x", File.ReadAllText(Path.Combine(game, "Grove", "a.png")));
        Assert.False(Directory.Exists(session.Path));
        Assert.Null(store.Latest);
    }

    [Fact]
    public void Restore_KeepsTheSnapshotWhenAFileFailed()
    {
        using var tmp = new TempDir();
        var (store, game) = Arrange(tmp);
        var session = store.Begin(Stamp);
        session.Capture(game, new[] { "Grove\\a.png", "Grove\\b.png" });

        using (new FileStream(Path.Combine(game, "Grove", "a.png"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = store.Restore(session, game);

            Assert.Single(result.Failures);
            Assert.True(Directory.Exists(session.Path));

            var latest = store.Latest;

            Assert.NotNull(latest);
            Assert.Equal(session.Path, latest.Path);
        }
    }

    [Fact]
    public void Latest_ReturnsTheOnlySessionOrNull()
    {
        using var tmp = new TempDir();
        var (store, game) = Arrange(tmp);
        Assert.Null(store.Latest);

        var session = store.Begin(Stamp);
        session.Capture(game, new[] { "Grove\\a.png", "Grove\\gone.png" });

        var latest = store.Latest;

        Assert.NotNull(latest);
        Assert.Equal(session.Path, latest.Path);
        Assert.Equal(2, latest.Count);

        store.Clear();

        Assert.Null(store.Latest);
    }

    [Fact]
    public void Pack_RelativePaths_ListsEveryFileAsFolderBackslashFile()
    {
        using var tmp = new TempDir();
        var packPath = Path.Combine(tmp.Path, "lib", "packs", "flower");
        new FakeGameTree(packPath)
            .File("BloodMoon", "A.png", "a")
            .File("BloodMoon", "B.png", "b")
            .File("Backgrounds", "BG_Sewer.jpg", "bg");

        var pack = PackScanner.ScanPack(packPath);

        Assert.Equal(
            new[] { "Backgrounds\\BG_Sewer.jpg", "BloodMoon\\A.png", "BloodMoon\\B.png" },
            pack.RelativePaths);
    }

    /// <summary>A library holding one pack with a platform record; the background record of that pack is absent.</summary>
    private static string ArrangeLibrary(TempDir tmp)
    {
        var lib = Path.Combine(tmp.Path, "lib");
        Directory.CreateDirectory(Path.Combine(lib, "packs", "P"));
        File.WriteAllText(Path.Combine(lib, "packs", "P", "platforms.bhmaps.json"), "old");
        return lib;
    }

    [Fact]
    public void Library_side_restores_and_deletes()
    {
        using var tmp = new TempDir();
        var (store, game) = Arrange(tmp);
        var lib = ArrangeLibrary(tmp);
        var platforms = Path.Combine(lib, "packs", "P", "platforms.bhmaps.json");
        var backgrounds = Path.Combine(lib, "packs", "P", "backgrounds.bhmaps.json");
        var session = store.Begin(Stamp);
        session.CaptureLibrary(lib, new[] { "packs\\P\\platforms.bhmaps.json", "packs\\P\\backgrounds.bhmaps.json" });
        File.WriteAllText(platforms, "new");
        File.WriteAllText(backgrounds, "written by the operation");

        var result = store.Restore(session, game, lib);

        Assert.Empty(result.Failures);
        Assert.Equal(2, result.Copied);
        Assert.Equal("old", File.ReadAllText(platforms));
        Assert.False(File.Exists(backgrounds));
        Assert.False(Directory.Exists(session.Path));
        Assert.Null(store.Latest);
    }

    [Fact]
    public void Game_only_restore_ignores_library_side()
    {
        using var tmp = new TempDir();
        var (store, game) = Arrange(tmp);
        var lib = ArrangeLibrary(tmp);
        var platforms = Path.Combine(lib, "packs", "P", "platforms.bhmaps.json");
        var session = store.Begin(Stamp);
        session.Capture(game, "Grove\\a.png");
        session.CaptureLibrary(lib, new[] { "packs\\P\\platforms.bhmaps.json" });
        File.WriteAllText(Path.Combine(game, "Grove", "a.png"), "changed");
        File.WriteAllText(platforms, "new");

        var result = store.Restore(session, game);

        Assert.Empty(result.Failures);
        Assert.Equal(1, result.Copied);
        Assert.Equal("x", File.ReadAllText(Path.Combine(game, "Grove", "a.png")));
        Assert.Equal("new", File.ReadAllText(platforms));
    }

    [Fact]
    public void Session_without_library_side_restores_as_before()
    {
        using var tmp = new TempDir();
        var (store, game) = Arrange(tmp);
        var lib = ArrangeLibrary(tmp);
        var session = store.Begin(Stamp);
        session.Capture(game, new[] { "Grove\\a.png", "Grove\\gone.png" });
        File.WriteAllText(Path.Combine(game, "Grove", "a.png"), "y");
        File.WriteAllText(Path.Combine(game, "Grove", "gone.png"), "written by the operation");

        var result = store.Restore(session, game, lib);

        Assert.Empty(result.Failures);
        Assert.Equal(2, result.Copied);
        Assert.Equal("x", File.ReadAllText(Path.Combine(game, "Grove", "a.png")));
        Assert.False(File.Exists(Path.Combine(game, "Grove", "gone.png")));
        Assert.False(Directory.Exists(session.Path));
        Assert.Null(store.Latest);
    }

    /// <summary>A thumbnails folder holding the game's own A.jpg; B.jpg is absent.</summary>
    private static string ArrangeThumbnails(TempDir tmp)
    {
        var thumbnails = Path.Combine(tmp.Path, "game-root", "images", "thumbnails");
        Directory.CreateDirectory(thumbnails);
        File.WriteAllText(Path.Combine(thumbnails, "A.jpg"), "game picture");
        return thumbnails;
    }

    [Fact]
    public void Thumbnails_side_restores_and_deletes()
    {
        using var tmp = new TempDir();
        var (store, game) = Arrange(tmp);
        var lib = ArrangeLibrary(tmp);
        var thumbnails = ArrangeThumbnails(tmp);
        var a = Path.Combine(thumbnails, "A.jpg");
        var b = Path.Combine(thumbnails, "B.jpg");
        var session = store.Begin(Stamp);
        session.CaptureThumbnails(thumbnails, new[] { "A.jpg", "B.jpg" });
        File.WriteAllText(a, "our thumbnail");
        File.WriteAllText(b, "written by the operation");

        var result = store.Restore(session, game, lib, thumbnails);

        Assert.Empty(result.Failures);
        Assert.Equal(2, result.Copied);
        Assert.Equal("game picture", File.ReadAllText(a));
        Assert.False(File.Exists(b));
        Assert.False(Directory.Exists(session.Path));
    }

    [Fact]
    public void Restore_without_thumbnails_dir_ignores_that_side()
    {
        using var tmp = new TempDir();
        var (store, game) = Arrange(tmp);
        var lib = ArrangeLibrary(tmp);
        var thumbnails = ArrangeThumbnails(tmp);
        var a = Path.Combine(thumbnails, "A.jpg");
        var session = store.Begin(Stamp);
        session.Capture(game, "Grove\\a.png");
        session.CaptureThumbnails(thumbnails, new[] { "A.jpg" });
        File.WriteAllText(Path.Combine(game, "Grove", "a.png"), "changed");
        File.WriteAllText(a, "our thumbnail");

        var result = store.Restore(session, game, lib);

        Assert.Empty(result.Failures);
        Assert.Equal(1, result.Copied);
        Assert.Equal("x", File.ReadAllText(Path.Combine(game, "Grove", "a.png")));
        Assert.Equal("our thumbnail", File.ReadAllText(a));
    }

    [Fact]
    public void Restore_deletes_a_library_file_that_was_absent_and_the_folder_it_left_empty()
    {
        using var tmp = new TempDir();
        var library = Path.Combine(tmp.Path, "lib");
        var game = Path.Combine(tmp.Path, "game");
        Directory.CreateDirectory(game);
        var store = new UndoStore(Path.Combine(tmp.Path, "appdata"));
        var relative = Path.Combine("packs", "flower copy", "BloodMoon", "a.png");
        var session = store.Begin();
        session.CaptureLibrary(library, [relative]);
        Directory.CreateDirectory(Path.Combine(library, "packs", "flower copy", "BloodMoon"));
        File.WriteAllText(Path.Combine(library, relative), "copied");

        var result = store.Restore(session, game, library);

        Assert.Empty(result.Failures);
        Assert.False(File.Exists(Path.Combine(library, relative)));
        Assert.False(Directory.Exists(Path.Combine(library, "packs", "flower copy")));
        Assert.True(Directory.Exists(Path.Combine(library, "packs")));
    }

    [Fact]
    public void Restore_keeps_a_folder_that_still_holds_a_file()
    {
        using var tmp = new TempDir();
        var library = Path.Combine(tmp.Path, "lib");
        var game = Path.Combine(tmp.Path, "game");
        Directory.CreateDirectory(game);
        var store = new UndoStore(Path.Combine(tmp.Path, "appdata"));
        var relative = Path.Combine("packs", "stone", "BloodMoon", "a.png");
        var session = store.Begin();
        session.CaptureLibrary(library, [relative]);
        Directory.CreateDirectory(Path.Combine(library, "packs", "stone", "BloodMoon"));
        File.WriteAllText(Path.Combine(library, relative), "copied");
        File.WriteAllText(Path.Combine(library, "packs", "stone", "BloodMoon", "kept.png"), "kept");

        store.Restore(session, game, library);

        Assert.True(Directory.Exists(Path.Combine(library, "packs", "stone", "BloodMoon")));
    }
}
