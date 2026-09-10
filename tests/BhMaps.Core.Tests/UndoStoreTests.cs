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
}
