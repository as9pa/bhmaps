using System.Windows.Media;
using BhMaps.Core.Imaging;
using BhMaps.Core.LevelData;
using BhMaps.Core.Settings;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class PreviewModeTests
{
    private static LevelDesc Level(CameraBounds camera, string? background, params PlatformNode[] platforms) =>
        new("Test", "Grove", camera,
            background is null ? [] : [new LevelBackground(background, null, null)],
            platforms);

    private static PlatformNode Node(double x, double y, string? theme, params LevelAsset[] assets) =>
        new(x, y, 1, 1, 1, 0, theme, assets, []);

    private static string Background(TempDir tmp, int width, int height, Func<int, int, (byte, byte, byte, byte)> pixel) =>
        SyntheticImage.SavePng(Path.Combine(tmp.Path, "Backgrounds", "BG_Grove.jpg"), width, height, pixel);

    private static void AssertExactly((byte R, byte G, byte B) actual, Color expected) =>
        Assert.True(
            Math.Abs(actual.R - expected.R) <= 2 && Math.Abs(actual.G - expected.G) <= 2
                && Math.Abs(actual.B - expected.B) <= 2,
            $"expected {expected}, got {actual}");

    private static void AssertColour((byte R, byte G, byte B) actual, byte r, byte g, byte b) =>
        Assert.True(Math.Abs(actual.R - r) < 40 && Math.Abs(actual.G - g) < 40 && Math.Abs(actual.B - b) < 40,
            $"expected about ({r},{g},{b}), got {actual}");

    [Fact]
    public void Render_PlatformsDrawsNoBackgroundButACheckerboard()
    {
        using var tmp = new TempDir();
        Background(tmp, 16, 16, (_, _) => (0, 0, 255, 255));
        var level = Level(new CameraBounds(0, 0, 640, 360), "BG_Grove.jpg");

        var bitmap = MapCompositor.Render(
            level, MapCompositor.CardWidth, MapCompositor.CardHeight, new AssetSources(tmp.Path),
            mode: PreviewMode.Platforms);

        var half = (int)(MapCompositor.CheckerSquare / 2);
        var next = (int)(MapCompositor.CheckerSquare * 1.5);
        AssertExactly(SyntheticImage.PixelAt(bitmap, half, half), MapCompositor.CheckerDark);
        AssertExactly(SyntheticImage.PixelAt(bitmap, next, half), MapCompositor.CheckerLight);
        AssertExactly(SyntheticImage.PixelAt(bitmap, half, next), MapCompositor.CheckerLight);
        AssertExactly(SyntheticImage.PixelAt(bitmap, next, next), MapCompositor.CheckerDark);
    }

    [Fact]
    public void Render_PlatformsStillDrawsThePieces()
    {
        using var tmp = new TempDir();
        Background(tmp, 16, 16, (_, _) => (0, 0, 255, 255));
        SyntheticImage.SavePng(Path.Combine(tmp.Path, "Grove", "plat.png"), 16, 16, (_, _) => (255, 0, 0, 255));
        var level = Level(new CameraBounds(0, 0, 100, 100), "BG_Grove.jpg",
            Node(0, 0, null, new LevelAsset("plat.png", 0, 0, 50, 50)));

        var bitmap = MapCompositor.Render(level, 100, 100, new AssetSources(tmp.Path), mode: PreviewMode.Platforms);

        AssertColour(SyntheticImage.PixelAt(bitmap, 25, 25), 255, 0, 0);
        var empty = SyntheticImage.PixelAt(bitmap, 75, 75);
        Assert.True(empty.B < 100, $"the background leaked through: {empty}");
    }

    [Fact]
    public void Render_BackgroundsCropsThePictureToFillAndDrawsNoPieces()
    {
        using var tmp = new TempDir();

        // Twice as wide as tall, left half red and right half blue. Filling a square crops a quarter off each side.
        Background(tmp, 32, 16, (x, _) => x < 16 ? ((byte)255, (byte)0, (byte)0, (byte)255) : ((byte)0, (byte)0, (byte)255, (byte)255));
        SyntheticImage.SavePng(Path.Combine(tmp.Path, "Grove", "plat.png"), 16, 16, (_, _) => (0, 255, 0, 255));
        var level = Level(new CameraBounds(0, 0, 100, 100), "BG_Grove.jpg",
            Node(0, 0, null, new LevelAsset("plat.png", 0, 0, 100, 100)));

        var bitmap = MapCompositor.Render(level, 100, 100, new AssetSources(tmp.Path), mode: PreviewMode.Backgrounds);

        AssertColour(SyntheticImage.PixelAt(bitmap, 1, 1), 255, 0, 0);
        AssertColour(SyntheticImage.PixelAt(bitmap, 10, 50), 255, 0, 0);
        AssertColour(SyntheticImage.PixelAt(bitmap, 90, 50), 0, 0, 255);
        AssertColour(SyntheticImage.PixelAt(bitmap, 98, 98), 0, 0, 255);
    }

    [Fact]
    public void CollectInputs_ReadsOnlyWhatTheModeDraws()
    {
        using var tmp = new TempDir();
        var background = Background(tmp, 16, 16, (_, _) => (0, 0, 255, 255));
        var plat = SyntheticImage.SavePng(Path.Combine(tmp.Path, "Grove", "plat.png"), 16, 16, (_, _) => (255, 0, 0, 255));
        var level = Level(new CameraBounds(0, 0, 100, 100), "BG_Grove.jpg",
            Node(0, 0, null, new LevelAsset("plat.png", 0, 0, 50, 50)));
        var sources = new AssetSources(tmp.Path);

        Assert.Equal([background, plat], MapCompositor.CollectInputs(level, sources));
        Assert.Equal([plat], MapCompositor.CollectInputs(level, sources, PreviewMode.Platforms));
        Assert.Equal([background], MapCompositor.CollectInputs(level, sources, PreviewMode.Backgrounds));
    }

    [Fact]
    public void Settings_RememberThePreviewModeAndDefaultToBoth()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "settings.json");

        SettingsStore.Save(path, AppSettings.Default);
        Assert.Equal(PreviewMode.Both, SettingsStore.Load(path).PreviewMode);

        SettingsStore.Save(path, AppSettings.Default with { PreviewMode = PreviewMode.Platforms });
        Assert.Equal(PreviewMode.Platforms, SettingsStore.Load(path).PreviewMode);

        SettingsStore.Save(path, AppSettings.Default with { PreviewMode = PreviewMode.Backgrounds });
        Assert.Equal(PreviewMode.Backgrounds, SettingsStore.Load(path).PreviewMode);
    }
}
