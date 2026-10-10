using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class PackPicturesTests
{
    private static MapEntry Map(string folder, IReadOnlyList<string> owned, params string[] candidates) =>
        new(folder, folder, new LevelDesc(folder, folder, new CameraBounds(0, 0, 100, 100), [], []), [], [], [], [], owned, candidates);

    private static string WriteFile(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public void ScanPack_NeverReadsTheImagesFolderAsAMap()
    {
        using var tmp = new TempDir();
        var folder = Path.Combine(tmp.Path, "lib", "packs", "Summer");
        WriteFile(Path.Combine(folder, "BloodMoon", "a.png"), "art");
        WriteFile(Path.Combine(folder, "images", "thumbnails", "BloodMoon.jpg"), "picture");
        WriteFile(Path.Combine(folder, "Images", "loose.png"), "picture");

        var pack = PackScanner.ScanPack(folder);

        Assert.Equal(["BloodMoon"], pack.Folders.Select(f => f.Name));
        Assert.Equal(Path.Combine(folder, "images", "thumbnails"), PackPictures.Dir(pack));
    }

    [Fact]
    public void Match_MapsFileNamesToTheirMapsAndSkipsSharedAndUnknownOnes()
    {
        using var tmp = new TempDir();
        var folder = Path.Combine(tmp.Path, "Summer");
        var thumbs = Path.Combine(folder, "images", "thumbnails");
        WriteFile(Path.Combine(folder, "mapArt", "BloodMoon", "a.png"), "art");
        WriteFile(Path.Combine(thumbs, "bloodmoon.jpg"), "own");
        WriteFile(Path.Combine(thumbs, "Shared.jpg"), "shared");
        WriteFile(Path.Combine(thumbs, "Nobody.jpg"), "unknown");
        var bloodMoon = Map("BloodMoon", ["BloodMoon.jpg"], "BloodMoon.jpg");
        var a = Map("A", ["Shared.jpg"], "Shared.jpg");
        var b = Map("B", [], "Shared.jpg");
        var pack = PackScanner.ScanPack(folder);

        var pictures = PackPictures.Match(pack, [bloodMoon, a, b], [bloodMoon, a, b]);

        var picture = Assert.Single(pictures);
        Assert.Equal("BloodMoon", picture.MapFolder);
        Assert.Equal("bloodmoon.jpg", picture.FileName);
        Assert.Equal(Path.Combine(thumbs, "bloodmoon.jpg"), picture.SourcePath);
    }

    [Fact]
    public void ForApply_CopiesOnlyThePicturesOfMapsThePackHasArtFor()
    {
        using var tmp = new TempDir();
        var folder = Path.Combine(tmp.Path, "packs", "Summer");
        var gameRoot = Path.Combine(tmp.Path, "game");
        var appData = Path.Combine(tmp.Path, "appdata");
        WriteFile(Path.Combine(folder, "BloodMoon", "a.png"), "art");
        WriteFile(Path.Combine(folder, "images", "thumbnails", "BloodMoon.jpg"), "pack picture");
        WriteFile(Path.Combine(folder, "images", "thumbnails", "Other.jpg"), "no art for this one");
        var gameJpg = WriteFile(Path.Combine(ThumbnailWriter.ThumbnailsDir(gameRoot), "BloodMoon.jpg"), "game picture");
        WriteFile(Path.Combine(ThumbnailWriter.ThumbnailsDir(gameRoot), "Other.jpg"), "game other");
        var bloodMoon = Map("BloodMoon", ["BloodMoon.jpg"], "BloodMoon.jpg");
        var other = Map("Other", ["Other.jpg"], "Other.jpg");
        var maps = new[] { bloodMoon, other };
        var pack = PackScanner.ScanPack(folder);
        var recordPath = ThumbnailRecord.PathFor(appData);

        var pictures = PackPictures.ForApply(pack, maps, maps);
        foreach (var picture in pictures)
        {
            var map = maps.Single(m => m.FolderName == picture.MapFolder);
            var target = ThumbnailWriter.Plan(map, maps, gameRoot, appData).Targets.Single();
            PackPictures.Copy(picture, target, pack.Name, recordPath);
        }

        Assert.Single(pictures);
        Assert.Equal("pack picture", File.ReadAllText(gameJpg));
        Assert.Equal("game other", File.ReadAllText(Path.Combine(ThumbnailWriter.ThumbnailsDir(gameRoot), "Other.jpg")));
        Assert.Equal("game picture", File.ReadAllText(Path.Combine(ThumbnailWriter.OriginalsDir(appData), "BloodMoon.jpg")));
        var entry = Assert.Single(ThumbnailRecord.Load(recordPath));
        Assert.Equal(("BloodMoon", "Summer", "BloodMoon.jpg"), (entry.Map, entry.Pack, entry.File));
    }

    [Fact]
    public void Keeps_WhileTheGameJpgStillHasTheRecordedHash()
    {
        using var tmp = new TempDir();
        var recordPath = ThumbnailRecord.PathFor(Path.Combine(tmp.Path, "appdata"));
        var jpg = WriteFile(Path.Combine(tmp.Path, "game", "BloodMoon.jpg"), "pack picture");
        ThumbnailRecord.Note(recordPath, "BloodMoon", "Summer", "BloodMoon.jpg", Hashing.FileHasher.Hash(jpg));

        Assert.True(ThumbnailRecord.Keeps(ThumbnailRecord.Load(recordPath), "BloodMoon", "BloodMoon.jpg", jpg));
        Assert.False(ThumbnailRecord.Keeps(ThumbnailRecord.Load(recordPath), "Other", "BloodMoon.jpg", jpg));

        File.WriteAllText(jpg, "changed by the game");

        Assert.False(ThumbnailRecord.Keeps(ThumbnailRecord.Load(recordPath), "BloodMoon", "BloodMoon.jpg", jpg));
    }

    [Fact]
    public void Drop_OnResetForgetsTheMapAndRemovesAnEmptyRecord()
    {
        using var tmp = new TempDir();
        var recordPath = ThumbnailRecord.PathFor(Path.Combine(tmp.Path, "appdata"));
        ThumbnailRecord.Note(recordPath, "BloodMoon", "Summer", "BloodMoon.jpg", "aa");
        ThumbnailRecord.Note(recordPath, "Mammoth", "Summer", "Mammoth.jpg", "bb");

        ThumbnailRecord.Drop(recordPath, "bloodmoon");

        Assert.Equal(["Mammoth"], ThumbnailRecord.Load(recordPath).Select(e => e.Map));

        ThumbnailRecord.Drop(recordPath, "Mammoth");

        Assert.False(File.Exists(recordPath));
    }
}
