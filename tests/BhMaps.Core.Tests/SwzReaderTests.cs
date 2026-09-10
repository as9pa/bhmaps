using System.Text;
using BhMaps.Core.LevelData;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class SwzReaderTests
{
    private const uint Key = 826351010;

    private static byte[] Xml(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Read_RoundTripsEveryEntryWithItsRootElementName()
    {
        var container = SwzWriter.Write(Key,
        [
            Xml("<LevelDesc LevelName=\"Grove\"><AssetDir>Grove</AssetDir></LevelDesc>"),
            Xml("<LevelTypes><LevelType><LevelName>Grove</LevelName></LevelType></LevelTypes>"),
            Xml("<Other>ignored</Other>"),
        ]);

        var entries = SwzReader.Read(container, Key);

        Assert.Equal(3, entries.Count);
        Assert.Equal("LevelDesc", entries[0].RootElement);
        Assert.Contains("Grove", entries[0].Xml);
        Assert.Equal("LevelTypes", entries[1].RootElement);
        Assert.Equal("Other", entries[2].RootElement);
    }

    [Fact]
    public void Read_HandlesAnEntryLargerThanOneKilobyte()
    {
        var big = "<LevelDesc>" + new string('x', 40_000) + "</LevelDesc>";
        var container = SwzWriter.Write(Key, [Xml(big)]);

        Assert.Equal(big, SwzReader.Read(container, Key)[0].Xml);
    }

    [Fact]
    public void Read_ThrowsOnTheWrongKey()
    {
        var container = SwzWriter.Write(Key, [Xml("<LevelDesc/>")]);

        Assert.Throws<InvalidDataException>(() => SwzReader.Read(container, Key + 1));
    }

    [Fact]
    public void Read_ThrowsWhenAnEntryChecksumIsWrong()
    {
        var container = SwzWriter.Write(Key, [Xml("<LevelDesc>abcdefghijklmnop</LevelDesc>")]);
        container[^1] ^= 0xFF;

        Assert.Throws<InvalidDataException>(() => SwzReader.Read(container, Key));
    }

    [Fact]
    public void RootElementName_SkipsDeclarationsAndComments()
    {
        Assert.Equal("LevelSetTypes", SwzReader.RootElementName("<?xml version=\"1.0\"?><LevelSetTypes />"));
        Assert.Equal("LevelDesc", SwzReader.RootElementName("<!-- c --><LevelDesc x=\"1\">"));
    }
}
