using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class PackApplierTests
{
    private static (Pack Pack, string Game) Arrange(TempDir tmp)
    {
        var game = Path.Combine(tmp.Path, "game");
        new FakeGameTree(game).File("BloodMoon", "A.png", "old-a").File("Swamp", "Mud1.png", "old-mud");
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(Path.Combine(lib, "packs", "flower"))
            .File("BloodMoon", "A.png", "new-a")
            .File("BloodMoon", "B.png", "new-b")
            .File("Backgrounds", "BG_Sewer.jpg", "new-bg");
        return (PackScanner.ScanAll(lib).Single(), game);
    }

    private static string Read(string game, string folder, string name) => File.ReadAllText(Path.Combine(game, folder, name));

    private static MapEntry Map(string folder, params string[] slots) =>
        new(folder, folder, new LevelDesc(folder, folder, new CameraBounds(0, 0, 100, 100), [], []), [], [],
            slots, []);

    [Fact]
    public void ApplyPack_CopiesEveryFolderOverwritesAndReportsProgress()
    {
        using var tmp = new TempDir();
        var (pack, game) = Arrange(tmp);
        var progress = new List<string>();

        var result = PackApplier.ApplyPack(pack, game, new SyncProgress(progress));

        Assert.Equal(3, result.Copied);
        Assert.Equal(0, result.Failed);
        Assert.Equal("new-a", Read(game, "BloodMoon", "A.png"));
        Assert.Equal("new-b", Read(game, "BloodMoon", "B.png"));
        Assert.Equal("new-bg", Read(game, "Backgrounds", "BG_Sewer.jpg"));
        Assert.Equal("old-mud", Read(game, "Swamp", "Mud1.png"));
        Assert.Equal(3, progress.Count);
        Assert.Contains(progress, p => p.Contains("BG_Sewer.jpg"));
    }

    [Fact]
    public void ApplyFolder_CopiesOnlyThatFolder()
    {
        using var tmp = new TempDir();
        var (pack, game) = Arrange(tmp);

        var result = PackApplier.ApplyFolder(pack, "bloodmoon", game);

        Assert.Equal(2, result.Copied);
        Assert.Empty(result.Failures);
        Assert.Equal("new-a", Read(game, "BloodMoon", "A.png"));
        Assert.False(File.Exists(Path.Combine(game, "Backgrounds", "BG_Sewer.jpg")));
    }

    [Fact]
    public void ApplyFolder_UnknownFolderIsRecordedAsFailure()
    {
        using var tmp = new TempDir();
        var (pack, game) = Arrange(tmp);

        var result = PackApplier.ApplyFolder(pack, "Nope", game);

        Assert.Equal(0, result.Copied);
        var failure = Assert.Single(result.Failures);
        Assert.Contains("Nope", failure.Error);
    }

    [Fact]
    public void ApplyFile_CopiesOneFile()
    {
        using var tmp = new TempDir();
        var (pack, game) = Arrange(tmp);

        var result = PackApplier.ApplyFile(pack, "BloodMoon", "b.png", game);

        Assert.Equal(1, result.Copied);
        Assert.Equal("new-b", Read(game, "BloodMoon", "B.png"));
        Assert.Equal("old-a", Read(game, "BloodMoon", "A.png"));
    }

    [Fact]
    public void ApplyFile_UnknownFileIsRecordedAsFailure()
    {
        using var tmp = new TempDir();
        var (pack, game) = Arrange(tmp);

        var result = PackApplier.ApplyFile(pack, "BloodMoon", "Missing.png", game);

        Assert.Equal(0, result.Copied);
        Assert.Contains("Missing.png", Assert.Single(result.Failures).Error);
    }

    [Fact]
    public void LockedTargetIsRecordedAndBatchContinues()
    {
        using var tmp = new TempDir();
        var (pack, game) = Arrange(tmp);
        var lockedPath = Path.Combine(game, "BloodMoon", "A.png");

        using (new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = PackApplier.ApplyPack(pack, game);

            Assert.Equal(2, result.Copied);
            var failure = Assert.Single(result.Failures);
            Assert.Equal(lockedPath, failure.Path);
            Assert.NotEmpty(failure.Error);
        }

        Assert.Equal("new-b", Read(game, "BloodMoon", "B.png"));
        Assert.Equal("old-a", Read(game, "BloodMoon", "A.png"));
    }

    [Fact]
    public void ApplyPack_HonorsCancellation()
    {
        using var tmp = new TempDir();
        var (pack, game) = Arrange(tmp);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => PackApplier.ApplyPack(pack, game, null, cts.Token));
        Assert.Equal("old-a", Read(game, "BloodMoon", "A.png"));
    }

    [Fact]
    public void ApplyToMaps_CopiesEachMapsFolderAndOnlyItsOwnSlots()
    {
        using var tmp = new TempDir();
        var (pack, game) = Arrange(tmp);
        var progress = new List<string>();

        var result = PackApplier.ApplyToMaps(
            pack, [Map("BloodMoon", "BG_Sewer.jpg")], game, new SyncProgress(progress), CancellationToken.None);

        Assert.Equal(3, result.Copied);
        Assert.Empty(result.Failures);
        Assert.Equal("new-a", Read(game, "BloodMoon", "A.png"));
        Assert.Equal("new-b", Read(game, "BloodMoon", "B.png"));
        Assert.Equal("new-bg", Read(game, "Backgrounds", "BG_Sewer.jpg"));
        Assert.Contains(progress, p => p.Contains("BG_Sewer.jpg"));
    }

    [Fact]
    public void ApplyToMaps_LeavesOutSlotsAndFoldersTheMapsDoNotName()
    {
        using var tmp = new TempDir();
        var (pack, game) = Arrange(tmp);

        var result = PackApplier.ApplyToMaps(pack, [Map("BloodMoon")], game, null, CancellationToken.None);

        Assert.Equal(2, result.Copied);
        Assert.False(File.Exists(Path.Combine(game, "Backgrounds", "BG_Sewer.jpg")));
        Assert.Equal("old-mud", Read(game, "Swamp", "Mud1.png"));
    }

    [Fact]
    public void ApplyToMaps_SkipsAMapThePackHasNothingFor()
    {
        using var tmp = new TempDir();
        var (pack, game) = Arrange(tmp);

        var result = PackApplier.ApplyToMaps(pack, [Map("Swamp", "BG_Nope.jpg")], game, null, CancellationToken.None);

        Assert.Equal(0, result.Copied);
        Assert.Empty(result.Failures);
        Assert.Equal("old-mud", Read(game, "Swamp", "Mud1.png"));
    }

    [Fact]
    public void ApplyToMapsPaths_NamesEveryFileTheApplyWouldWriteOnceEach()
    {
        using var tmp = new TempDir();
        var (pack, _) = Arrange(tmp);

        var paths = PackApplier.ApplyToMapsPaths(
            pack, [Map("BloodMoon", "BG_Sewer.jpg"), Map("Swamp", "BG_Sewer.jpg")]);

        Assert.Equal(
            new[]
            {
                Path.Combine("BloodMoon", "A.png"),
                Path.Combine("BloodMoon", "B.png"),
                Path.Combine("Backgrounds", "BG_Sewer.jpg"),
            },
            paths);
    }

    [Fact]
    public void ResetMapPaths_NamesWhatTheGameHasNowPlusThePacksFilesAndTheSlotsItCanRestore()
    {
        using var tmp = new TempDir();
        var (pack, game) = Arrange(tmp);

        var paths = PackApplier.ResetMapPaths(
            GameTreeScanner.Scan(game), Map("BloodMoon", "BG_Sewer.jpg"), pack);

        Assert.Equal(
            new[]
            {
                Path.Combine("BloodMoon", "A.png"),
                Path.Combine("BloodMoon", "B.png"),
                Path.Combine("Backgrounds", "BG_Sewer.jpg"),
            },
            paths);
    }

    [Fact]
    public void ResetMapPaths_WithNoSlotsAndNothingInThePackStillNamesTheFolderAsItIs()
    {
        using var tmp = new TempDir();
        var (pack, game) = Arrange(tmp);

        var paths = PackApplier.ResetMapPaths(GameTreeScanner.Scan(game), Map("Swamp"), pack);

        Assert.Equal(new[] { Path.Combine("Swamp", "Mud1.png") }, paths);
    }
}
