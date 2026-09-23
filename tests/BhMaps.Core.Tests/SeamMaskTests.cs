using BhMaps.Core.Imaging;
using BhMaps.Core.LevelData;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class SeamMaskTests
{
    private static LevelDesc Level(string name, params PlatformNode[] platforms) =>
        new(name, "Grove", new CameraBounds(0, 0, 100, 100), [], platforms);

    private static PlatformNode Node(double x, double y, params LevelAsset[] assets) =>
        new(x, y, 1, 1, 1, 0, null, assets, []);

    private static SeamPiece Piece(string name, byte alpha, double opacity, int size = 40) =>
        new($@"Grove\{name}", size, size, Enumerable.Repeat(alpha, size * size).ToArray(), opacity);

    /// <summary>The weight each piece ends up with when the written alphas are composited top first.</summary>
    private static double[] Weights(double[] written)
    {
        var weights = new double[written.Length];
        var through = 1.0;
        for (var i = 0; i < written.Length; i++)
        {
            weights[i] = written[i] * through;
            through *= 1 - written[i];
        }

        return weights;
    }

    [Theory]
    [InlineData(0.8, 0.5, 0.6)]
    [InlineData(1.0, 0.3, 1.0)]
    [InlineData(0.5, 0.75, 0.25)]
    public void Written_TwoPiecesAtOneOpacity_MatchesTheClosedForm(double s, double a, double t)
    {
        var written = SeamMask.Written(s, a, [(t, a)]);

        Assert.Equal(s * a * (1 - t) / (1 - (a * t)), written, 9);
    }

    [Fact]
    public void Written_ThreePiecesAtOneOpacity_MatchesTheClosedFormOverTheCombinedCover()
    {
        const double a = 0.4;
        var t = 1 - ((1 - 0.6) * (1 - 0.3));

        var written = SeamMask.Written(0.9, a, [(0.6, a), (0.3, a)]);

        Assert.Equal(0.9 * a * (1 - t) / (1 - (a * t)), written, 9);
    }

    [Fact]
    public void Written_UnequalOpacities_GivesEveryPieceItsWantedWeight()
    {
        (double S, double A)[] stack = [(0.7, 0.3), (1.0, 0.8), (0.5, 0.6)];
        var written = new double[stack.Length];
        for (var i = 0; i < stack.Length; i++)
        {
            written[i] = SeamMask.Written(stack[i].S, stack[i].A, stack[..i]);
        }

        var weights = Weights(written);
        var native = 1.0;
        for (var i = 0; i < stack.Length; i++)
        {
            Assert.Equal(stack[i].A * stack[i].S * native, weights[i], 9);
            native *= 1 - stack[i].S;
        }
    }

    [Fact]
    public void Written_AtFullOpacity_IsTheNativeAlpha()
    {
        Assert.Equal(0.8, SeamMask.Written(0.8, 1, [(0.6, 1), (0.3, 1)]), 9);
    }

    [Fact]
    public void Compute_AtFullOpacity_ChangesNothing()
    {
        var level = Level("L", Node(0, 0, new LevelAsset("a.png", 0, 0, 0, 0), new LevelAsset("b.png", 20, 0, 0, 0)));

        var masks = SeamMask.Compute([level], [Piece("a.png", 255, 1), Piece("b.png", 255, 1)]);

        Assert.Empty(masks);
    }

    [Fact]
    public void Compute_CorrectsTheLowerPieceOnlyWhereTheUpperCoversIt()
    {
        var level = Level("L", Node(0, 0, new LevelAsset("a.png", 0, 0, 0, 0), new LevelAsset("b.png", 20, 0, 0, 0)));

        var masks = SeamMask.Compute([level], [Piece("a.png", 255, 0.5), Piece("b.png", 255, 0.5)]);

        var lower = Assert.Single(masks).Value;
        Assert.Equal(0.5f, lower[5], 5);
        Assert.Equal(0f, lower[30], 5);
    }

    [Fact]
    public void Compute_LeavesPiecesOnDifferentMovingPlatformsAlone()
    {
        var still = Node(0, 0, new LevelAsset("a.png", 0, 0, 0, 0));
        var moving = new PlatformNode(20, 0, 1, 1, 1, 0, null, [new LevelAsset("b.png", 0, 0, 0, 0)], [], Moving: true);

        var masks = SeamMask.Compute([Level("L", still, moving)], [Piece("a.png", 255, 0.5), Piece("b.png", 255, 0.5)]);

        Assert.Empty(masks);
    }

    [Fact]
    public void Compute_KeepsThePlainFadeWhereTwoUsesDisagree()
    {
        var overlapping = Level("One", Node(0, 0, new LevelAsset("a.png", 0, 0, 0, 0), new LevelAsset("b.png", 20, 0, 0, 0)));
        var apart = Level("Two", Node(0, 0, new LevelAsset("a.png", 0, 0, 0, 0), new LevelAsset("b.png", 60, 0, 0, 0)));

        var masks = SeamMask.Compute([overlapping, apart], [Piece("a.png", 255, 0.5), Piece("b.png", 255, 0.5)]);

        Assert.Empty(masks);
    }

    [Fact]
    public void Parse_MarksAMovingPlatform()
    {
        var level = LevelDescParser.Parse(
            "<LevelDesc AssetDir=\"Grove\" LevelName=\"L\"><Platform X=\"1\" />"
            + "<MovingPlatform X=\"5\"><Platform><Asset AssetName=\"a.png\" X=\"0\" Y=\"0\" W=\"1\" H=\"1\" /></Platform></MovingPlatform></LevelDesc>");

        Assert.False(level.Platforms[0].Moving);
        Assert.True(level.Platforms[1].Moving);
    }

    /// <summary>Three overlapping pieces, one of them half transparent, faded to 50% with the mask, composite to the
    /// same picture as the stack drawn at full strength and faded as one layer over the background.</summary>
    [Fact]
    public void CorrectedPieces_CompositeToTheOneLayerImage()
    {
        using var tmp = new TempDir();
        const double opacity = 0.5;
        var game = Path.Combine(tmp.Path, "game");
        var faded = Path.Combine(tmp.Path, "faded");
        SyntheticImage.SavePng(Path.Combine(game, @"Backgrounds\BG.png"), 100, 100, (_, _) => (20, 40, 200, 255));
        SyntheticImage.SavePng(Path.Combine(game, @"Grove\a.png"), 40, 40, (_, _) => (250, 30, 30, 255));
        SyntheticImage.SavePng(Path.Combine(game, @"Grove\b.png"), 40, 40, (x, _) => (30, 250, 30, (byte)(x < 20 ? 255 : 180)));
        SyntheticImage.SavePng(Path.Combine(game, @"Grove\c.png"), 40, 40, (_, _) => (240, 240, 60, 255));
        File.Copy(Path.Combine(game, @"Backgrounds\BG.png"), Path.Combine(Directory.CreateDirectory(Path.Combine(faded, "Backgrounds")).FullName, "BG.png"));
        var platforms = Node(0, 0,
            new LevelAsset("a.png", 10, 10, 0, 0),
            new LevelAsset("b.png", 30, 20, 0, 0),
            new LevelAsset("c.png", 40, 40, 0, 0));
        var level = new LevelDesc("L", "Grove", new CameraBounds(0, 0, 100, 100), [new LevelBackground("BG.png", null, null)], [platforms]);
        var backgroundOnly = level with { Platforms = [] };

        var pieces = new[] { "a.png", "b.png", "c.png" }
            .Select(n => (Name: n, Bitmap: PlatformRecolor.Decode(Path.Combine(game, "Grove", n))))
            .ToList();
        var masks = SeamMask.Compute(
            [level],
            pieces.Select(p => new SeamPiece($@"Grove\{p.Name}", 40, 40, PlatformRecolor.AlphaOf(p.Bitmap), opacity)).ToList());
        foreach (var (name, bitmap) in pieces)
        {
            PlatformRecolor.Apply(bitmap, Path.Combine(faded, "Grove", name), opacity, 0, masks.GetValueOrDefault($@"Grove\{name}"));
        }

        var full = MapCompositor.Render(level, 100, 100, new AssetSources(game));
        var under = MapCompositor.Render(backgroundOnly, 100, 100, new AssetSources(game));
        var actual = MapCompositor.Render(level, 100, 100, new AssetSources(faded));

        Assert.Equal(2, masks.Count);
        for (var y = 0; y < 100; y++)
        {
            for (var x = 0; x < 100; x++)
            {
                var f = SyntheticImage.PixelAt(full, x, y);
                var u = SyntheticImage.PixelAt(under, x, y);
                var got = SyntheticImage.PixelAt(actual, x, y);
                AssertNear(u.R, f.R, got.R, opacity, x, y);
                AssertNear(u.G, f.G, got.G, opacity, x, y);
                AssertNear(u.B, f.B, got.B, opacity, x, y);
            }
        }
    }

    private static void AssertNear(byte under, byte full, byte actual, double opacity, int x, int y)
    {
        var wanted = (under * (1 - opacity)) + (full * opacity);
        Assert.True(Math.Abs(actual - wanted) <= 2, $"at ({x},{y}) wanted {wanted:0.0}, got {actual}");
    }
}
