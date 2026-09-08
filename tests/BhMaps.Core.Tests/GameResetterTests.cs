using BhMaps.Core.Operations;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class GameResetterTests
{
    [Fact]
    public void ResetFolder_DeletesPngAndJpgCaseInsensitivelyOnly()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        new FakeGameTree(game)
            .File("BloodMoon", "A.png", "x")
            .File("BloodMoon", "B.JPG", "x")
            .File("BloodMoon", "c.PnG", "x")
            .File("BloodMoon", "notes.txt", "x")
            .File(Path.Combine("BloodMoon", "sub"), "inner.png", "x");

        var result = GameResetter.ResetFolder(game, "BloodMoon");

        Assert.Equal(3, result.Deleted);
        Assert.Empty(result.Failures);
        Assert.True(Directory.Exists(Path.Combine(game, "BloodMoon")));
        Assert.Equal(new[] { "notes.txt" }, Directory.GetFiles(Path.Combine(game, "BloodMoon")).Select(Path.GetFileName));
        Assert.True(File.Exists(Path.Combine(game, "BloodMoon", "sub", "inner.png")));
    }

    [Fact]
    public void ResetFolder_MissingFolderIsNoOp()
    {
        using var tmp = new TempDir();

        var result = GameResetter.ResetFolder(tmp.Path, "Nope");

        Assert.Equal(0, result.Deleted);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void ResetFolder_UnreadableFolderIsRecordedAsFailure()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        new FakeGameTree(game).File("BloodMoon", "A.png", "x");
        var denied = Path.Combine(game, "BloodMoon");

        using (AccessDenial.DenyListing(denied))
        {
            var result = GameResetter.ResetFolder(game, "BloodMoon");

            Assert.Equal(0, result.Deleted);
            var failure = Assert.Single(result.Failures);
            Assert.Equal(denied, failure.Path);
            Assert.NotEmpty(failure.Error);
        }

        Assert.True(File.Exists(Path.Combine(game, "BloodMoon", "A.png")));
    }

    [Fact]
    public void ResetFile_DeletesOneFile()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        new FakeGameTree(game).File("BloodMoon", "A.png", "x").File("BloodMoon", "B.png", "x");

        var result = GameResetter.ResetFile(game, "BloodMoon", "a.png");

        Assert.Equal(1, result.Deleted);
        Assert.False(File.Exists(Path.Combine(game, "BloodMoon", "A.png")));
        Assert.True(File.Exists(Path.Combine(game, "BloodMoon", "B.png")));
    }

    [Fact]
    public void ResetFile_RefusesNonImageAndIgnoresMissing()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        new FakeGameTree(game).File("BloodMoon", "notes.txt", "x");

        var refused = GameResetter.ResetFile(game, "BloodMoon", "notes.txt");
        var missing = GameResetter.ResetFile(game, "BloodMoon", "Gone.png");

        Assert.Equal(0, refused.Deleted);
        Assert.Single(refused.Failures);
        Assert.True(File.Exists(Path.Combine(game, "BloodMoon", "notes.txt")));
        Assert.Equal(0, missing.Deleted);
        Assert.Empty(missing.Failures);
    }

    [Fact]
    public void ResetAll_ClearsEveryFolderAndReportsProgress()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        FakeGameTree.Standard(game);
        var progress = new List<string>();

        var result = GameResetter.ResetAll(game, new SyncProgress(progress));

        Assert.Equal(13, result.Deleted);
        Assert.Empty(result.Failures);
        Assert.Equal(8, Directory.GetDirectories(game).Length);
        Assert.Empty(Directory.GetFiles(game, "*", SearchOption.AllDirectories));
        Assert.Equal(8, progress.Count);
    }

    [Fact]
    public void ResetAll_HonorsCancellation()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        FakeGameTree.Standard(game);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => GameResetter.ResetAll(game, null, cts.Token));
        Assert.Equal(13, Directory.GetFiles(game, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void LockedFileIsRecordedAndOthersDeleted()
    {
        using var tmp = new TempDir();
        var game = Path.Combine(tmp.Path, "game");
        new FakeGameTree(game).File("BloodMoon", "A.png", "x").File("BloodMoon", "B.png", "x");
        var lockedPath = Path.Combine(game, "BloodMoon", "A.png");

        using (new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = GameResetter.ResetFolder(game, "BloodMoon");

            Assert.Equal(1, result.Deleted);
            Assert.Equal(lockedPath, Assert.Single(result.Failures).Path);
        }

        Assert.True(File.Exists(lockedPath));
        Assert.False(File.Exists(Path.Combine(game, "BloodMoon", "B.png")));
    }
}
