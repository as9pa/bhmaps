using BhMaps.Core.Text;

namespace BhMaps.Core.Tests;

public class SentencesTests
{
    [Fact]
    public void TakesTheFirstOfSeveral()
    {
        Assert.Equal(
            "b&w maps applied to 1 map.",
            Sentences.First("b&w maps applied to 1 map. Map-select thumbnail updated. Shows when Brawlhalla starts."));
    }

    [Fact]
    public void KeepsAWholeLineThatEndsNoSentence()
    {
        Assert.Equal("3 maps imported into b&w maps", Sentences.First("3 maps imported into b&w maps"));
    }

    /// <summary>A period inside a version or a file name is not a sentence ending, so the line stands whole.</summary>
    [Fact]
    public void IgnoresAPeriodThatEndsNothing()
    {
        Assert.Equal("Game data read from Brawlhalla 10.10.", Sentences.First("Game data read from Brawlhalla 10.10."));
    }

    [Theory]
    [InlineData("  Captured the Default pack. 68 files.  ", "Captured the Default pack.")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void TrimsWhatItIsGiven(string text, string expected)
    {
        Assert.Equal(expected, Sentences.First(text));
    }
}
