using System.Text;
using BhMaps.Core.LevelData;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class SwfKeyFinderTests
{
    // 0x31434AE2, at or above 2^28, so it exercises the five-byte u30 path.
    private const uint Key = 826351010;

    private static string WriteSwz(TempDir tmp)
    {
        var path = tmp.Sub("Dynamic.swz");
        File.WriteAllBytes(path, SwzWriter.Write(Key, [Encoding.UTF8.GetBytes("<LevelDesc/>")]));
        return path;
    }

    [Fact]
    public void Find_LocatesTheKeyInAnUncompressedSwf()
    {
        using var tmp = new TempDir();
        var swf = tmp.Sub("BrawlhallaAir.swf");
        File.WriteAllBytes(swf, SwfWriter.Uncompressed([1u, 2u, Key, 99u]));

        var result = SwfKeyFinder.Find(swf, WriteSwz(tmp));

        Assert.Equal(Key, result.Key);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Find_LocatesTheKeyInACompressedSwf()
    {
        using var tmp = new TempDir();
        var swf = tmp.Sub("BrawlhallaAir.swf");
        File.WriteAllBytes(swf, SwfWriter.Compressed([Key]));

        Assert.Equal(Key, SwfKeyFinder.Find(swf, WriteSwz(tmp)).Key);
    }

    [Fact]
    public void Find_ReturnsNoKeyWhenNoCandidateMatches()
    {
        using var tmp = new TempDir();
        var swf = tmp.Sub("BrawlhallaAir.swf");
        File.WriteAllBytes(swf, SwfWriter.Uncompressed([1u, 2u, 3u]));

        var result = SwfKeyFinder.Find(swf, WriteSwz(tmp));

        Assert.Null(result.Key);
        Assert.Equal(3, result.CandidatesScanned);
    }

    [Fact]
    public void Find_ReportsLzmaSwfAsUnsupportedInsteadOfThrowing()
    {
        using var tmp = new TempDir();
        var swf = tmp.Sub("BrawlhallaAir.swf");
        File.WriteAllBytes(swf, SwfWriter.Lzma());

        var result = SwfKeyFinder.Find(swf, WriteSwz(tmp));

        Assert.Null(result.Key);
        Assert.Contains("LZMA", result.Error);
    }

    [Fact]
    public void Find_ReportsAMissingFileInsteadOfThrowing()
    {
        using var tmp = new TempDir();

        var result = SwfKeyFinder.Find(Path.Combine(tmp.Path, "nope.swf"), Path.Combine(tmp.Path, "nope.swz"));

        Assert.Null(result.Key);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void AbcUints_DecodesFiveByteValues()
    {
        using var tmp = new TempDir();
        var swf = tmp.Sub("big.swf");
        File.WriteAllBytes(swf, SwfWriter.Uncompressed([0xFFFFFFFFu]));
        var tag = SwfKeyFinder.Tags(SwfKeyFinder.ReadBody(swf)).Single(t => t.Code == SwfKeyFinder.DoAbcTag).Body;
        var abc = tag[(Array.IndexOf(tag, (byte)0, 4) + 1)..];

        Assert.Equal([0xFFFFFFFFu], SwfKeyFinder.AbcUints(abc));
    }
}
