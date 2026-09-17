using System.Windows.Media.Imaging;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Operations;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class ThumbnailWriterTests
{
    private static LevelDesc Level(string levelName, string folder) =>
        new(levelName, folder, new CameraBounds(0, 0, 100, 100), [], []);

    private static MapEntry Map(string folder, IReadOnlyList<string> owned, params string[] candidates) =>
        new(folder, folder, Level(folder, folder), [], [], [], [], owned, candidates);

    /// <summary>A folder whose levels name a file each, the way the ranked "Small X" levels do.</summary>
    private static MapEntry MapOfLevels(string folder, params (string File, LevelDesc Level)[] files) =>
        new(folder, folder, files[0].Level, [.. files.Select(f => f.Level)], [], [], [],
            [.. files.Select(f => f.File)],
            [.. files.Select(f => f.File)],
            files.ToDictionary(f => f.File, f => f.Level, StringComparer.OrdinalIgnoreCase));

    private static string Thumbnail(TempDir tmp, string gameRoot, string fileName, string contents)
    {
        var path = Path.Combine(tmp.Path, gameRoot, "images", "thumbnails", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public void Plan_ReturnsATargetForEveryFileTheMapOwns()
    {
        using var tmp = new TempDir();
        var gameRoot = Path.Combine(tmp.Path, "game");
        var appData = Path.Combine(tmp.Path, "appdata");
        var big = Thumbnail(tmp, "game", "Mammoth.jpg", "big picture");
        var small = Thumbnail(tmp, "game", "MammothSmall.jpg", "small picture");
        var map = Map("Fortress", ["Mammoth.jpg", "MammothSmall.jpg"], "Mammoth.jpg", "MammothSmall.jpg");

        var plan = ThumbnailWriter.Plan(map, [map], gameRoot, appData);

        Assert.False(plan.NamesNoFile);
        Assert.Equal(["Mammoth.jpg", "MammothSmall.jpg"], plan.Files.Select(f => f.FileName));
        Assert.All(plan.Files, f => Assert.Equal(ThumbnailSkip.None, f.Skip));
        Assert.Equal([big, small], plan.Targets.Select(t => t.TargetPath));
        Assert.Equal(
            [
                Path.Combine(appData, "thumbnails-original", "Mammoth.jpg"),
                Path.Combine(appData, "thumbnails-original", "MammothSmall.jpg"),
            ],
            plan.Targets.Select(t => t.OriginalPath));
    }

    [Fact]
    public void Plan_KeepsTheFilesThatExistAndNotesAMissingOne()
    {
        using var tmp = new TempDir();
        var gameRoot = Path.Combine(tmp.Path, "game");
        var appData = Path.Combine(tmp.Path, "appdata");
        Thumbnail(tmp, "game", "wasteland.jpg", "game picture");
        var map = Map(
            "Seven",
            ["wasteland.jpg", "wastelandshowdown.jpg"],
            "wasteland.jpg",
            "wastelandshowdown.jpg");

        var plan = ThumbnailWriter.Plan(map, [map], gameRoot, appData);

        Assert.Single(plan.Targets);
        Assert.Equal("wasteland.jpg", plan.Targets[0].FileName);
        Assert.Equal(ThumbnailSkip.Missing, plan.Files[1].Skip);
        Assert.Null(plan.Files[1].Target);
        Assert.Null(plan.Files[1].OtherMap);
    }

    [Fact]
    public void Plan_SkipsOnlyTheCandidateAnotherMapAlsoNames()
    {
        using var tmp = new TempDir();
        var gameRoot = Path.Combine(tmp.Path, "game");
        var appData = Path.Combine(tmp.Path, "appdata");
        Thumbnail(tmp, "game", "Own.jpg", "own picture");
        Thumbnail(tmp, "game", "Both.jpg", "shared picture");
        var sewer = Map("Sewer", ["Own.jpg"], "Own.jpg", "Both.jpg");
        var swamp = Map("Swamp", [], "Both.jpg");

        var plan = ThumbnailWriter.Plan(sewer, [sewer, swamp], gameRoot, appData);

        Assert.Single(plan.Targets);
        Assert.Equal("Own.jpg", plan.Targets[0].FileName);
        Assert.Equal(ThumbnailSkip.Shared, plan.Files[1].Skip);
        Assert.Equal("Swamp", plan.Files[1].OtherMap);
    }

    [Fact]
    public void Plan_NamesNoFileWhenTheMapsLevelsNameNone()
    {
        using var tmp = new TempDir();
        var map = Map("Bombsketball", []);

        var plan = ThumbnailWriter.Plan(map, [map], Path.Combine(tmp.Path, "game"), Path.Combine(tmp.Path, "appdata"));

        Assert.True(plan.NamesNoFile);
        Assert.Empty(plan.Files);
        Assert.Empty(plan.Targets);
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
        var kept = new ThumbnailTarget(
            "A.jpg",
            target,
            Path.Combine(tmp.Path, "appdata", "thumbnails-original", "A.jpg"),
            Level("A", "A"));

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
        var kept = new ThumbnailTarget(
            "A.jpg",
            target,
            Path.Combine(tmp.Path, "appdata", "thumbnails-original", "A.jpg"),
            Level("A", "A"));
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

        var composite = ThumbnailWriter.Render(level, tmp.Path);

        Assert.Equal(580, composite.PixelWidth);
        Assert.Equal(328, composite.PixelHeight);
        Assert.True(composite.IsFrozen);
        var (r, g, b) = SyntheticImage.PixelAt(composite, 290, 164);
        Assert.True(r > 200 && g < 60 && b < 60, $"expected a red pixel, got ({r},{g},{b})");
    }

    [Fact]
    public void Plan_AimsEachFileAtTheLevelThatNamesIt()
    {
        using var tmp = new TempDir();
        var gameRoot = Path.Combine(tmp.Path, "game");
        var appData = Path.Combine(tmp.Path, "appdata");
        Thumbnail(tmp, "game", "Mammoth.jpg", "big picture");
        Thumbnail(tmp, "game", "MammothSmall.jpg", "small picture");
        var fortress = Level("Fortress", "Fortress");
        var small = Level("SmallFortress", "Fortress");
        var map = MapOfLevels("Fortress", ("Mammoth.jpg", fortress), ("MammothSmall.jpg", small));

        var plan = ThumbnailWriter.Plan(map, [map], gameRoot, appData);

        Assert.Equal(2, plan.Targets.Count);
        Assert.Same(fortress, plan.Targets[0].Level);
        Assert.Same(small, plan.Targets[1].Level);
        Assert.NotEqual(plan.Targets[0].Level, plan.Targets[1].Level);
    }

    [Fact]
    public void Plan_GivesTwoFilesOfOneLevelTheSameLevel()
    {
        using var tmp = new TempDir();
        var gameRoot = Path.Combine(tmp.Path, "game");
        var appData = Path.Combine(tmp.Path, "appdata");
        Thumbnail(tmp, "game", "Grove.jpg", "one picture");
        Thumbnail(tmp, "game", "GroveAlso.jpg", "the same picture");
        var grove = Level("Grove", "Grove");
        var map = MapOfLevels("Grove", ("Grove.jpg", grove), ("GroveAlso.jpg", grove));

        var plan = ThumbnailWriter.Plan(map, [map], gameRoot, appData);

        Assert.Same(grove, plan.Targets[0].Level);
        Assert.Same(plan.Targets[0].Level, plan.Targets[1].Level);
    }

    [Fact]
    public void Plan_FallsBackToTheBaseLevelForAFileNoLevelNames()
    {
        using var tmp = new TempDir();
        var gameRoot = Path.Combine(tmp.Path, "game");
        var appData = Path.Combine(tmp.Path, "appdata");
        Thumbnail(tmp, "game", "Mammoth.jpg", "big picture");
        var map = Map("Fortress", ["Mammoth.jpg"], "Mammoth.jpg");

        var plan = ThumbnailWriter.Plan(map, [map], gameRoot, appData);

        Assert.Same(map.BaseLevel, plan.Targets[0].Level);
    }

    [Fact]
    public void Render_ComposesEachLevelOfTheFolderOnItsOwn()
    {
        using var tmp = new TempDir();
        SyntheticImage.SavePng(
            Path.Combine(tmp.Path, "Fortress", "plat.png"), 16, 16, (_, _) => SyntheticImage.Rgb(255, 0, 0));
        var big = new LevelDesc("Fortress", "Fortress", new CameraBounds(0, 0, 100, 100), [],
            [new PlatformNode(0, 0, 1, 1, 1, 0, null, [new LevelAsset("plat.png", 0, 0, 100, 100)], [])]);
        var small = new LevelDesc("SmallFortress", "Fortress", new CameraBounds(0, 0, 100, 100), [],
            [new PlatformNode(0, 0, 1, 1, 1, 0, null, [new LevelAsset("plat.png", 0, 0, 20, 20)], [])]);

        var bigComposite = ThumbnailWriter.Render(big, tmp.Path);
        var smallComposite = ThumbnailWriter.Render(small, tmp.Path);

        var (br, bg, bb) = SyntheticImage.PixelAt(bigComposite, 290, 164);
        var (sr, sg, sb) = SyntheticImage.PixelAt(smallComposite, 290, 164);
        Assert.True(br > 200 && bg < 60 && bb < 60, $"expected the big level red, got ({br},{bg},{bb})");
        Assert.True((br, bg, bb) != (sr, sg, sb), $"expected the two levels to differ, both are ({br},{bg},{bb})");
    }
}
