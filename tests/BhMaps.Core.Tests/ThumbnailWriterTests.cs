using System.Windows.Media.Imaging;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Operations;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class ThumbnailWriterTests
{
    private static MapEntry Map(string folder, string? owned, params string[] candidates) =>
        new(folder, folder, new LevelDesc(folder, folder, new CameraBounds(0, 0, 100, 100), [], []),
            [], [], [], [], owned is null ? Array.Empty<string>() : [owned], candidates);

    private static string Thumbnail(TempDir tmp, string gameRoot, string fileName, string contents)
    {
        var path = Path.Combine(tmp.Path, gameRoot, "images", "thumbnails", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public void Plan_ReturnsTheTargetWhenTheFileExists()
    {
        using var tmp = new TempDir();
        var gameRoot = Path.Combine(tmp.Path, "game");
        var appData = Path.Combine(tmp.Path, "appdata");
        var target = Thumbnail(tmp, "game", "A.jpg", "game picture");
        var map = Map("Grove", "A.jpg", "A.jpg");

        var plan = ThumbnailWriter.Plan(map, [map], gameRoot, appData);

        Assert.Equal(ThumbnailSkip.None, plan.Skip);
        Assert.Null(plan.OtherMap);
        Assert.Equal("A.jpg", plan.Target!.FileName);
        Assert.Equal(target, plan.Target.TargetPath);
        Assert.Equal(Path.Combine(appData, "thumbnails-original", "A.jpg"), plan.Target.OriginalPath);
    }

    [Fact]
    public void Plan_SkipsMissingSharedAndNoFile()
    {
        using var tmp = new TempDir();
        var gameRoot = Path.Combine(tmp.Path, "game");
        var appData = Path.Combine(tmp.Path, "appdata");
        var missing = Map("Grove", "Grove.jpg", "Grove.jpg");
        var shared = Map("Sewer", null, "Both.jpg");
        var sharing = Map("Swamp", null, "Both.jpg");
        var noFile = Map("Bombsketball", null);
        IReadOnlyList<MapEntry> all = [missing, shared, sharing, noFile];

        Assert.Equal(ThumbnailSkip.Missing, ThumbnailWriter.Plan(missing, all, gameRoot, appData).Skip);

        var sharedPlan = ThumbnailWriter.Plan(shared, all, gameRoot, appData);
        Assert.Equal(ThumbnailSkip.Shared, sharedPlan.Skip);
        Assert.Null(sharedPlan.Target);
        Assert.Equal("Swamp", sharedPlan.OtherMap);

        var noFilePlan = ThumbnailWriter.Plan(noFile, all, gameRoot, appData);
        Assert.Equal(ThumbnailSkip.NoFile, noFilePlan.Skip);
        Assert.Null(noFilePlan.OtherMap);
    }

    [Fact]
    public void Write_ProducesA290By164Jpeg()
    {
        using var tmp = new TempDir();
        var source = SyntheticImage.SavePng(tmp.Sub("src.png"), 580, 328, (_, _) => SyntheticImage.Rgb(200, 30, 30));
        var target = Path.Combine(tmp.Path, "out", "A.jpg");

        ThumbnailWriter.Write(SyntheticImage.DecodePng(source), target);

        var written = SyntheticImage.DecodeJpeg(File.ReadAllBytes(target));
        Assert.Equal(290, written.PixelWidth);
        Assert.Equal(164, written.PixelHeight);
        var (r, g, b) = SyntheticImage.PixelAt(written, 145, 82);
        Assert.True(r > 150 && g < 90 && b < 90, $"expected a red pixel, got ({r},{g},{b})");
    }

    [Fact]
    public void KeepOriginal_CopiesOnceAndNeverOverwrites()
    {
        using var tmp = new TempDir();
        var target = Thumbnail(tmp, "game", "A.jpg", "game picture");
        var kept = new ThumbnailTarget("A.jpg", target, Path.Combine(tmp.Path, "appdata", "thumbnails-original", "A.jpg"));

        ThumbnailWriter.KeepOriginal(kept);
        File.WriteAllText(target, "our thumbnail");
        ThumbnailWriter.KeepOriginal(kept);

        Assert.Equal("game picture", File.ReadAllText(kept.OriginalPath));
    }

    [Fact]
    public void RestoreOriginal_PutsTheKeptPictureBackOrReportsThereIsNone()
    {
        using var tmp = new TempDir();
        var target = Thumbnail(tmp, "game", "A.jpg", "game picture");
        var kept = new ThumbnailTarget("A.jpg", target, Path.Combine(tmp.Path, "appdata", "thumbnails-original", "A.jpg"));
        Assert.False(ThumbnailWriter.RestoreOriginal(kept));

        ThumbnailWriter.KeepOriginal(kept);
        File.WriteAllText(target, "our thumbnail");

        Assert.True(ThumbnailWriter.RestoreOriginal(kept));
        Assert.Equal("game picture", File.ReadAllText(target));
    }

    [Fact]
    public void RestoreAll_CopiesBackAndDeletesTheCopies()
    {
        using var tmp = new TempDir();
        var a = Thumbnail(tmp, "game", "A.jpg", "ours a");
        var b = Thumbnail(tmp, "game", "B.jpg", "ours b");
        var originals = Path.Combine(tmp.Path, "appdata", "thumbnails-original");
        Directory.CreateDirectory(originals);
        File.WriteAllText(Path.Combine(originals, "A.jpg"), "game a");
        File.WriteAllText(Path.Combine(originals, "B.jpg"), "game b");
        var thumbnails = Path.GetDirectoryName(a)!;

        Assert.Equal(new[] { "A.jpg", "B.jpg" }, ThumbnailWriter.KeptOriginals(originals));
        var written = ThumbnailWriter.RestoreAll(originals, thumbnails);

        Assert.Equal(new[] { "A.jpg", "B.jpg" }, written);
        Assert.Equal("game a", File.ReadAllText(a));
        Assert.Equal("game b", File.ReadAllText(b));
        Assert.Empty(Directory.GetFiles(originals));
    }

    [Fact]
    public void Render_ComposesTheMapFromTheGameFolder()
    {
        using var tmp = new TempDir();
        SyntheticImage.SavePng(Path.Combine(tmp.Path, "Grove", "plat.png"), 16, 16, (_, _) => SyntheticImage.Rgb(255, 0, 0));
        var level = new LevelDesc("Grove", "Grove", new CameraBounds(0, 0, 100, 100), [],
            [new PlatformNode(0, 0, 1, 1, 1, 0, null, [new LevelAsset("plat.png", 0, 0, 100, 100)], [])]);
        var map = new MapEntry("Grove", "Grove", level, [level], [], [], []);

        var composite = ThumbnailWriter.Render(map, tmp.Path);

        Assert.Equal(580, composite.PixelWidth);
        Assert.Equal(328, composite.PixelHeight);
        Assert.True(composite.IsFrozen);
        var (r, g, b) = SyntheticImage.PixelAt(composite, 290, 164);
        Assert.True(r > 200 && g < 60 && b < 60, $"expected a red pixel, got ({r},{g},{b})");
    }
}
