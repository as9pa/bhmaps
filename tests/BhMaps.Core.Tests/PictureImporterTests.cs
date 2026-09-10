using BhMaps.Core.Imaging;
using BhMaps.Core.Operations;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class PictureImporterTests
{
    private const int W = BackgroundFitter.OutputWidth;
    private const int H = BackgroundFitter.OutputHeight;

    private static void AssertRed((byte R, byte G, byte B) p) { Assert.True(p.R > 200 && p.G < 60 && p.B < 60, $"expected red, got {p}"); }
    private static void AssertBlack((byte R, byte G, byte B) p) { Assert.True(p.R < 8 && p.G < 8 && p.B < 8, $"expected black, got {p}"); }

    private static string Lib(TempDir tmp) => Path.Combine(tmp.Path, "lib");

    private static string PackFile(TempDir tmp, string packName, string fileName) =>
        Path.Combine(Lib(tmp), "packs", packName, "Backgrounds", fileName);

    [Fact]
    public void ToFitOptions_MapsTheFourLabelsOntoTheFitterModes()
    {
        Assert.Equal(new FitOptions(FitMode.Stretch), PictureImporter.ToFitOptions(PictureFit.Stretch));
        Assert.Equal(new FitOptions(FitMode.Contain, NoUpscale: true), PictureImporter.ToFitOptions(PictureFit.Center));
        Assert.Equal(new FitOptions(FitMode.Cover), PictureImporter.ToFitOptions(PictureFit.Fill));
        Assert.Equal(new FitOptions(FitMode.Contain), PictureImporter.ToFitOptions(PictureFit.Fit));
        Assert.False(PictureImporter.ToFitOptions(PictureFit.Fit).NoUpscale);
    }

    [Fact]
    public void Contain_WithNoUpscale_LeavesASmallSourceAtItsOwnSize()
    {
        using var tmp = new TempDir();
        var src = SyntheticImage.SaveQuadrants(tmp.Sub("small.png"), 100, 100);

        var centered = BackgroundFitter.Render(src, new FitOptions(FitMode.Contain, NoUpscale: true), W, H);
        var upscaled = BackgroundFitter.Render(src, new FitOptions(FitMode.Contain), W, H);

        // The 100x100 source lands at (974, 525.5); (1024, 100) is letterbox for it and image for the upscaled render.
        AssertBlack(SyntheticImage.PixelAt(centered, 1024, 100));
        AssertRed(SyntheticImage.PixelAt(centered, 1000, 550));
        var p = SyntheticImage.PixelAt(upscaled, 1024, 100);
        Assert.True(p.R > 8 || p.G > 8 || p.B > 8, $"expected the upscaled render to cover (1024, 100), got {p}");
    }

    [Fact]
    public void Contain_WithoutNoUpscale_StillUpscalesExactlyAsBefore()
    {
        var upscaled = BackgroundFitter.DestinationRect(100, 100, new FitOptions(FitMode.Contain), W, H);
        Assert.Equal(H, upscaled.Width, 0.01);
        Assert.Equal(H, upscaled.Height, 0.01);
        Assert.Equal((W - H) / 2.0, upscaled.X, 0.01);
        Assert.Equal(0, upscaled.Y, 0.01);

        var clamped = BackgroundFitter.DestinationRect(100, 100, new FitOptions(FitMode.Contain, NoUpscale: true), W, H);
        Assert.Equal(100, clamped.Width, 0.01);
        Assert.Equal(100, clamped.Height, 0.01);
        Assert.Equal((W - 100) / 2.0, clamped.X, 0.01);
        Assert.Equal((H - 100) / 2.0, clamped.Y, 0.01);

        // NoUpscale only clamps growth; a source larger than the canvas still shrinks to fit.
        var shrunk = BackgroundFitter.DestinationRect(4096, 2302, new FitOptions(FitMode.Contain, NoUpscale: true), W, H);
        Assert.Equal(W, shrunk.Width, 0.01);
        Assert.Equal(H, shrunk.Height, 0.01);
    }

    [Fact]
    public void Import_WritesEachPictureAsA2048x1151JpegInThePacksBackgroundsFolder()
    {
        using var tmp = new TempDir();
        var one = SyntheticImage.SaveQuadrants(tmp.Sub("src", "one.png"), 800, 200);
        var two = SyntheticImage.SaveQuadrants(tmp.Sub("src", "two.png"), 200, 800);

        var result = PictureImporter.Import([one, two], Lib(tmp), "shots", PictureFit.Fill);

        Assert.Equal(2, result.Copied);
        Assert.Empty(result.Failures);
        foreach (var name in new[] { "one.jpg", "two.jpg" })
        {
            var written = PackFile(tmp, "shots", name);
            Assert.True(File.Exists(written), written);
            var decoded = SyntheticImage.DecodeJpeg(File.ReadAllBytes(written));
            Assert.Equal(W, decoded.PixelWidth);
            Assert.Equal(H, decoded.PixelHeight);
        }
    }

    [Fact]
    public void Import_KeepsTheSourceFileNameAndSwapsTheExtensionForJpg()
    {
        using var tmp = new TempDir();
        var src = SyntheticImage.SaveQuadrants(tmp.Sub("src", "photo.png"), 100, 60);

        Assert.Equal("photo.jpg", PictureImporter.TargetFileName(src));
        Assert.Equal("holiday.jpg", PictureImporter.TargetFileName(Path.Combine("d", "holiday.jpg")));
        Assert.Equal("my.photo.v2.jpg", PictureImporter.TargetFileName(Path.Combine("d", "my.photo.v2.png")));

        var result = PictureImporter.Import([src], Lib(tmp), "shots", PictureFit.Fit);

        Assert.Equal(1, result.Copied);
        Assert.True(File.Exists(PackFile(tmp, "shots", "photo.jpg")));
        Assert.False(File.Exists(PackFile(tmp, "shots", "photo.png")));
    }

    [Fact]
    public void Import_CreatesThePackFolderWhenItDoesNotExist()
    {
        using var tmp = new TempDir();
        var src = SyntheticImage.SaveQuadrants(tmp.Sub("src", "one.png"), 100, 60);
        Assert.False(Directory.Exists(Lib(tmp)));

        var result = PictureImporter.Import([src], Lib(tmp), "brand new pack", PictureFit.Center);

        Assert.Equal(1, result.Copied);
        Assert.Empty(result.Failures);
        Assert.True(Directory.Exists(Path.Combine(Lib(tmp), "packs", "brand new pack", "Backgrounds")));
        Assert.True(File.Exists(PackFile(tmp, "brand new pack", "one.jpg")));
    }

    [Fact]
    public void Import_OverwritesAnExistingFileOfTheSameName()
    {
        using var tmp = new TempDir();
        var src = SyntheticImage.SaveQuadrants(tmp.Sub("src", "one.png"), 100, 60);
        var target = PackFile(tmp, "shots", "one.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "old");

        var result = PictureImporter.Import([src], Lib(tmp), "shots", PictureFit.Stretch);

        Assert.Equal(1, result.Copied);
        Assert.Empty(result.Failures);
        var decoded = SyntheticImage.DecodeJpeg(File.ReadAllBytes(target));
        Assert.Equal(W, decoded.PixelWidth);
        Assert.Equal(H, decoded.PixelHeight);
    }

    [Fact]
    public void Import_NeverMovesOrDeletesASourceFile()
    {
        using var tmp = new TempDir();
        var src = SyntheticImage.SaveQuadrants(tmp.Sub("src", "one.png"), 100, 60);
        var before = File.ReadAllBytes(src);

        var result = PictureImporter.Import([src], Lib(tmp), "shots", PictureFit.Fill);

        Assert.Equal(1, result.Copied);
        Assert.True(File.Exists(src));
        Assert.Equal(before, File.ReadAllBytes(src));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(src)!));
    }

    [Fact]
    public void Import_RejectsAnInvalidPackName()
    {
        using var tmp = new TempDir();
        var src = SyntheticImage.SaveQuadrants(tmp.Sub("src", "one.png"), 100, 60);

        Assert.Throws<ArgumentException>(() => PictureImporter.Import([src], Lib(tmp), "a\\b", PictureFit.Fit));

        Assert.False(Directory.Exists(Path.Combine(Lib(tmp), "packs")));
    }

    [Fact]
    public void Import_RecordsAFailureAndContinuesWhenOneSourceIsNotAnImage()
    {
        using var tmp = new TempDir();
        var one = SyntheticImage.SaveQuadrants(tmp.Sub("src", "one.png"), 100, 60);
        var notes = tmp.Sub("src", "notes.txt");
        File.WriteAllText(notes, "not an image");
        var two = SyntheticImage.SaveQuadrants(tmp.Sub("src", "two.png"), 60, 100);

        var result = PictureImporter.Import([one, notes, two], Lib(tmp), "shots", PictureFit.Fit);

        Assert.Equal(2, result.Copied);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(PackFile(tmp, "shots", "notes.jpg"), failure.Path);
        Assert.NotEmpty(failure.Error);
        Assert.True(File.Exists(PackFile(tmp, "shots", "one.jpg")));
        Assert.True(File.Exists(PackFile(tmp, "shots", "two.jpg")));
        Assert.False(File.Exists(PackFile(tmp, "shots", "notes.jpg")));
    }

    [Fact]
    public void Import_ReportsProgressPerPicture()
    {
        using var tmp = new TempDir();
        var one = SyntheticImage.SaveQuadrants(tmp.Sub("src", "one.png"), 100, 60);
        var two = SyntheticImage.SaveQuadrants(tmp.Sub("src", "two.png"), 60, 100);
        var progress = new List<string>();

        var result = PictureImporter.Import([one, two], Lib(tmp), "shots", PictureFit.Fill, new SyncProgress(progress));

        Assert.Equal(2, result.Copied);
        Assert.Equal(new[] { Path.Combine("Backgrounds", "one.jpg"), Path.Combine("Backgrounds", "two.jpg") }, progress);
    }

    [Fact]
    public void Import_SuffixesSameCallCollisionsSoBothPicturesSurvive()
    {
        using var tmp = new TempDir();
        var one = SyntheticImage.SaveQuadrants(tmp.Sub("a", "photo.png"), 100, 60);
        var two = SyntheticImage.SaveQuadrants(tmp.Sub("b", "photo.JPG"), 60, 100);

        var result = PictureImporter.Import([one, two], Lib(tmp), "shots", PictureFit.Fit);

        Assert.Equal(2, result.Copied);
        Assert.Empty(result.Failures);
        Assert.True(File.Exists(PackFile(tmp, "shots", "photo.jpg")));
        Assert.True(File.Exists(PackFile(tmp, "shots", "photo (2).jpg")));
    }

    [Fact]
    public void Import_ReportsTheNamesItWroteWithTheSuffixEachOneTook()
    {
        using var tmp = new TempDir();
        var one = SyntheticImage.SaveQuadrants(tmp.Sub("a", "photo.png"), 100, 60);
        var notes = tmp.Sub("a", "notes.txt");
        File.WriteAllText(notes, "not an image");
        var two = SyntheticImage.SaveQuadrants(tmp.Sub("b", "photo.jpg"), 60, 100);

        var result = PictureImporter.Import([one, notes, two], Lib(tmp), "shots", PictureFit.Fill);

        // Source order, the collision suffix the second one actually took, and no entry for the source that failed.
        Assert.Equal(new[] { "photo.jpg", "photo (2).jpg" }, result.Written);
        Assert.Single(result.Failures);
    }
}
