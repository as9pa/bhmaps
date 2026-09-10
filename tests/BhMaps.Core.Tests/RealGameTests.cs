using BhMaps.Core.Imaging;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;

namespace BhMaps.Core.Tests;

/// <summary>
/// Opt-in checks against the real install. Set BHMAPS_REAL_GAME to the Brawlhalla folder to run them.
/// They only read the four data files and the mapArt images; they never write anything.
/// </summary>
public class RealGameTests
{
    private static string? Root()
    {
        var root = Environment.GetEnvironmentVariable("BHMAPS_REAL_GAME");
        return string.IsNullOrWhiteSpace(root) || !Directory.Exists(root) ? null : root;
    }

    [Fact]
    public void KeyIsFoundAndAboutOneHundredAndTwentyLevelsParse()
    {
        if (Root() is not { } root)
        {
            return;
        }

        var result = LevelDataReader.Read(root, knownKey: null);

        Assert.True(result.Available, result.Error);
        Assert.NotEqual(0u, result.Key);
        Assert.InRange(result.Model!.Levels.Count, 90, 200);
    }

    [Fact]
    public void GroveReadsAsTwilightGrove()
    {
        if (Root() is not { } root)
        {
            return;
        }

        var catalog = MapCatalog.Build(LevelDataReader.Read(root, null).Model!);
        var grove = catalog.ByFolder("Grove");

        Assert.NotNull(grove);
        Assert.Equal("Twilight Grove", grove!.DisplayName);
    }

    /// <summary>Enigma hangs every one of its unthemed platform images off the Platform element itself except
    /// Platform_Steam2B.png: five draws over four files, measured against the shipped LevelDesc_Enigma.xml.</summary>
    [Fact]
    public void EnigmaCollectsEveryUnthemedPlatformInput()
    {
        if (Root() is not { } root)
        {
            return;
        }

        var catalog = MapCatalog.Build(LevelDataReader.Read(root, null).Model!);
        var enigma = catalog.ByFolder("Enigma");
        Assert.NotNull(enigma);

        var inputs = MapCompositor.CollectInputs(enigma!.BaseLevel, new AssetSources(Path.Combine(root, "mapArt")));
        var platforms = inputs
            .Where(p => !p.Contains(@"\Backgrounds\", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.InRange(platforms.Count, 5, 100);
        Assert.InRange(platforms.Distinct(StringComparer.OrdinalIgnoreCase).Count(), 4, 100);
    }

    [Theory]
    [InlineData("Grove")]
    [InlineData("Blackguard")]
    [InlineData("Enigma")]
    public void RealCompositesHaveANonEmptyPlatformLayer(string folder)
    {
        if (Root() is not { } root)
        {
            return;
        }

        var mapArt = Path.Combine(root, "mapArt");
        var catalog = MapCatalog.Build(LevelDataReader.Read(root, null).Model!);
        var map = catalog.ByFolder(folder);
        Assert.NotNull(map);

        var sources = new AssetSources(mapArt);
        var withPlatforms = MapCompositor.Render(map!.BaseLevel, 640, 360, sources);
        var backgroundOnly = MapCompositor.Render(
            map.BaseLevel with { Platforms = Array.Empty<PlatformNode>() }, 640, 360, sources);

        Assert.NotEqual(
            Helpers.SyntheticImage.MeanLuminance(backgroundOnly),
            Helpers.SyntheticImage.MeanLuminance(withPlatforms),
            precision: 2);
    }
}
