using BhMaps.Core.Imaging;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class BackgroundApplierTests
{
    private static void AssertRed((byte R, byte G, byte B) p) { Assert.True(p.R > 200 && p.G < 60 && p.B < 60, $"expected red, got {p}"); }

    /// <summary>A JPEG that is already exactly 2048x1151, so Apply has nothing to refit.</summary>
    private static string ExactSizeJpeg(TempDir tmp)
    {
        var quadrants = SyntheticImage.SaveQuadrants(tmp.Sub("exact-src.png"), 800, 200);
        var path = tmp.Sub("exact.jpg");
        File.WriteAllBytes(path, BackgroundFitter.Fit(quadrants, new FitOptions(FitMode.Cover)));
        return path;
    }

    [Fact]
    public void Build_ListsEveryPackBackgroundAndTheGamesOwn()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(Path.Combine(lib, "packs", "dark")).File("Backgrounds", "BG_Sewer.jpg", "d").File("Swamp", "Mud1.png", "d");
        new FakeGameTree(Path.Combine(lib, "packs", "neon")).File("Backgrounds", "BG_Space.jpg", "n");
        var game = Path.Combine(tmp.Path, "game");
        new FakeGameTree(game).File("Backgrounds", "BG_Grove.jpg", "g").File("Backgrounds", "BG_Cave.jpg", "g");

        var backgrounds = BackgroundLibrary.Build(PackScanner.ScanAll(lib), GameTreeScanner.Scan(game));

        Assert.Equal(4, backgrounds.Count);
        Assert.Equal(
            new[] { "dark", BackgroundLibrary.GameSourceName, BackgroundLibrary.GameSourceName, "neon" },
            backgrounds.Select(b => b.PackName));
        Assert.Equal(new[] { "BG_Sewer.jpg", "BG_Cave.jpg", "BG_Grove.jpg", "BG_Space.jpg" }, backgrounds.Select(b => b.FileName));
        Assert.Equal(new[] { false, true, true, false }, backgrounds.Select(b => b.FromGame));
        Assert.Equal(Path.Combine(lib, "packs", "dark", "Backgrounds", "BG_Sewer.jpg"), backgrounds[0].FullPath);
        Assert.Equal(Path.Combine(game, "Backgrounds", "BG_Cave.jpg"), backgrounds[1].FullPath);
    }

    [Fact]
    public void Build_SkipsNonJpgFilesInABackgroundsFolder()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(Path.Combine(lib, "packs", "dark"))
            .File("Backgrounds", "BG_Sewer.jpg", "d")
            .File("Backgrounds", "BG_Sewer.png", "d");

        var backgrounds = BackgroundLibrary.Build(PackScanner.ScanAll(lib), GameTree.Empty(Path.Combine(tmp.Path, "game")));

        Assert.Equal("BG_Sewer.jpg", Assert.Single(backgrounds).FileName);
    }

    [Fact]
    public void Build_ReturnsAnEmptyListWhenNothingHasBackgrounds()
    {
        using var tmp = new TempDir();
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(Path.Combine(lib, "packs", "dark")).File("Swamp", "Mud1.png", "d");
        var game = Path.Combine(tmp.Path, "game");
        new FakeGameTree(game).File("Swamp", "Mud1.png", "g");

        Assert.Empty(BackgroundLibrary.Build(PackScanner.ScanAll(lib), GameTreeScanner.Scan(game)));
    }

    [Fact]
    public void Apply_CopiesA2048x1151SourceByteForByteWithoutRefitting()
    {
        using var tmp = new TempDir();
        var source = ExactSizeJpeg(tmp);
        var game = Path.Combine(tmp.Path, "game");

        var result = BackgroundApplier.Apply(source, game, ["BG_Grove.jpg"]);

        Assert.Equal(1, result.Copied);
        Assert.Equal(0, result.Failed);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(Path.Combine(game, "Backgrounds", "BG_Grove.jpg")));
    }

    [Fact]
    public void Apply_FitsAWrongSizedSourceWithCoverTo2048x1151()
    {
        using var tmp = new TempDir();
        var source = SyntheticImage.SaveQuadrants(tmp.Sub("src.png"), 800, 200);
        var game = Path.Combine(tmp.Path, "game");

        var result = BackgroundApplier.Apply(source, game, ["BG_Grove.jpg"]);

        Assert.Equal(1, result.Copied);
        var written = SyntheticImage.DecodeJpeg(File.ReadAllBytes(Path.Combine(game, "Backgrounds", "BG_Grove.jpg")));
        Assert.Equal(BackgroundFitter.OutputWidth, written.PixelWidth);
        Assert.Equal(BackgroundFitter.OutputHeight, written.PixelHeight);

        // Contain would letterbox this 800x200 source and leave both of these black.
        AssertRed(SyntheticImage.PixelAt(written, 5, 5));
        AssertRed(SyntheticImage.PixelAt(written, 5, 300));
    }

    [Fact]
    public void Apply_WritesEverySlotFromOneFit()
    {
        using var tmp = new TempDir();
        var source = SyntheticImage.SaveQuadrants(tmp.Sub("src.png"), 800, 200);
        var game = Path.Combine(tmp.Path, "game");
        var progress = new List<string>();

        var result = BackgroundApplier.Apply(source, game, ["BG_Grove.jpg", "BG_Cave.jpg", "BG_Sewer.jpg"], new SyncProgress(progress));

        Assert.Equal(3, result.Copied);
        Assert.Empty(result.Failures);
        var written = new[] { "BG_Grove.jpg", "BG_Cave.jpg", "BG_Sewer.jpg" }
            .Select(slot => File.ReadAllBytes(Path.Combine(game, "Backgrounds", slot)))
            .ToList();
        Assert.Equal(written[0], written[1]);
        Assert.Equal(written[0], written[2]);
        Assert.Equal(3, progress.Count);
        Assert.Contains(progress, p => p.Contains("BG_Cave.jpg"));
    }

    [Fact]
    public void Apply_CreatesTheBackgroundsFolderWhenItIsMissing()
    {
        using var tmp = new TempDir();
        var source = ExactSizeJpeg(tmp);
        var game = Path.Combine(tmp.Path, "game");
        Directory.CreateDirectory(game);

        var result = BackgroundApplier.Apply(source, game, ["BG_Grove.jpg"]);

        Assert.Equal(1, result.Copied);
        Assert.True(Directory.Exists(Path.Combine(game, "Backgrounds")));
        Assert.True(File.Exists(Path.Combine(game, "Backgrounds", "BG_Grove.jpg")));
    }

    [Fact]
    public void Apply_RecordsAFailureAndContinuesWhenOneSlotIsLocked()
    {
        using var tmp = new TempDir();
        var source = ExactSizeJpeg(tmp);
        var game = Path.Combine(tmp.Path, "game");
        var lockedPath = Path.Combine(game, "Backgrounds", "BG_Locked.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(lockedPath)!);
        File.WriteAllText(lockedPath, "locked");

        using (new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = BackgroundApplier.Apply(source, game, ["BG_Grove.jpg", "BG_Locked.jpg"]);

            Assert.Equal(1, result.Copied);
            var failure = Assert.Single(result.Failures);
            Assert.Equal(lockedPath, failure.Path);
            Assert.NotEmpty(failure.Error);
        }

        Assert.Equal("locked", File.ReadAllText(lockedPath));
        Assert.True(File.Exists(Path.Combine(game, "Backgrounds", "BG_Grove.jpg")));
    }

    [Fact]
    public void TargetPaths_PrefixesEverySlotWithBackgrounds()
    {
        Assert.Equal(
            new[] { Path.Combine("Backgrounds", "BG_Grove.jpg"), Path.Combine("Backgrounds", "BG_Cave.jpg") },
            BackgroundApplier.TargetPaths(["BG_Grove.jpg", "BG_Cave.jpg"]));
    }
}
