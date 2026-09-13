using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Packs;
using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class PackExporterTests
{
    private const string PlatformsJson = "{\"version\":1,\"maps\":{}}";

    private const string BackgroundsJson = "{\"version\":1,\"slots\":{}}";

    /// <summary>A one-folder pack under &lt;tmp&gt;\lib\packs\flower, with the two edit records when asked for.</summary>
    private static Pack Arrange(TempDir tmp, bool withRecords)
    {
        var lib = Path.Combine(tmp.Path, "lib");
        var packRoot = Path.Combine(lib, "packs", "flower");
        new FakeGameTree(packRoot).File("BloodMoon", "a.png", "flower-a");
        if (withRecords)
        {
            File.WriteAllText(Path.Combine(packRoot, PlatformEditRecord.FileName), PlatformsJson);
            File.WriteAllText(Path.Combine(packRoot, BackgroundEditRecord.FileName), BackgroundsJson);
        }

        return PackScanner.ScanAll(lib).Single();
    }

    [Fact]
    public void Export_copies_records_when_present()
    {
        using var tmp = new TempDir();
        var pack = Arrange(tmp, withRecords: true);
        var destination = Path.Combine(tmp.Path, "export");

        var result = PackExporter.Export(pack, destination);

        var exported = Path.Combine(destination, "flower");
        Assert.Empty(result.Failures);
        Assert.Equal(3, result.Copied);
        Assert.Equal("flower-a", File.ReadAllText(Path.Combine(exported, "BloodMoon", "a.png")));
        Assert.Equal(PlatformsJson, File.ReadAllText(Path.Combine(exported, PlatformEditRecord.FileName)));
        Assert.Equal(BackgroundsJson, File.ReadAllText(Path.Combine(exported, BackgroundEditRecord.FileName)));
    }

    [Fact]
    public void Export_without_records_copies_only_pictures()
    {
        using var tmp = new TempDir();
        var pack = Arrange(tmp, withRecords: false);
        var destination = Path.Combine(tmp.Path, "export");

        var result = PackExporter.Export(pack, destination);

        var exported = Path.Combine(destination, "flower");
        Assert.Empty(result.Failures);
        Assert.Equal(1, result.Copied);
        Assert.Equal("flower-a", File.ReadAllText(Path.Combine(exported, "BloodMoon", "a.png")));
        Assert.False(File.Exists(Path.Combine(exported, PlatformEditRecord.FileName)));
        Assert.False(File.Exists(Path.Combine(exported, BackgroundEditRecord.FileName)));
    }
}
