using System.Buffers.Binary;
using System.Text;
using BhMaps.Core.LevelData;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class GameVersionTests
{
    /// <summary>An uncompressed SWF whose body is the given string constants, one per NUL-separated run. Only
    /// the eight header bytes matter to the reader; everything after them is scanned as bytes.</summary>
    private static byte[] Swf(params string[] literals)
    {
        var body = Encoding.ASCII.GetBytes(string.Join('\0', literals));
        var output = new MemoryStream();
        output.Write("FWS"u8);
        output.WriteByte(13);
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)(8 + body.Length));
        output.Write(length);
        output.Write(body);
        return output.ToArray();
    }

    [Fact]
    public void Parse_picks_the_full_version_and_keeps_major_minor()
    {
        Assert.Equal("10.10", GameVersion.Parse(["a", "10.10.83192", "10.10", "1.0"]));
    }

    [Fact]
    public void Parse_ignores_a_two_part_version()
    {
        Assert.Null(GameVersion.Parse(["1.0", "10.10"]));
    }

    [Fact]
    public void Parse_ignores_a_short_build_number()
    {
        Assert.Null(GameVersion.Parse(["1.2.3", "9.99.99"]));
    }

    [Fact]
    public void Parse_of_nothing_is_null()
    {
        Assert.Null(GameVersion.Parse([]));
    }

    [Fact]
    public void Read_finds_the_version_among_the_string_constants()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "BrawlhallaAir.swf");
        File.WriteAllBytes(path, Swf("com.brawlhalla.Main", "10.10", "10.10.83192", "1.0"));

        Assert.Equal("10.10", GameVersion.Read(path));
    }

    [Fact]
    public void Read_ignores_a_version_buried_in_a_longer_run()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "BrawlhallaAir.swf");

        // A path with a version in it is not the version constant, and a run that long is never one.
        File.WriteAllBytes(path, Swf(@"C:\builds\brawlhalla\10.10.83192\air\BrawlhallaAir.swf"));

        Assert.Null(GameVersion.Read(path));
    }

    [Fact]
    public void Read_of_a_swf_with_no_version_is_null()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "BrawlhallaAir.swf");
        File.WriteAllBytes(path, Swf("Grove", "1.0", "1.2.3"));

        Assert.Null(GameVersion.Read(path));
    }

    [Fact]
    public void Read_of_a_missing_file_is_null()
    {
        using var tmp = new TempDir();

        Assert.Null(GameVersion.Read(Path.Combine(tmp.Path, "BrawlhallaAir.swf")));
    }

    [Fact]
    public void Read_of_something_that_is_not_a_swf_is_null()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "BrawlhallaAir.swf");
        File.WriteAllText(path, "10.10.83192");

        Assert.Null(GameVersion.Read(path));
    }
}
