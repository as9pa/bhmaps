using BhMaps.Core.Update;

namespace BhMaps.Core.Tests;

public class ReleaseCheckerTests
{
    /// <summary>A cut-down copy of the shape api.github.com returns for releases/latest, with the three assets
    /// 2.6.0 ships. Saved as a string: no test of this class ever reaches the network.</summary>
    private const string LatestJson = """
    {
      "url": "https://api.github.com/repos/as9pa/bhmaps/releases/1",
      "html_url": "https://github.com/as9pa/bhmaps/releases/tag/v2.6.0",
      "tag_name": "v2.6.0",
      "name": "BhMaps 2.6.0",
      "draft": false,
      "prerelease": false,
      "published_at": "2026-09-14T10:30:00Z",
      "body": "Pack tile menus, copy and move, auto-update.",
      "assets": [
        {
          "name": "bhmaps-v2.6.0-win-x64.exe",
          "size": 141557760,
          "browser_download_url": "https://github.com/as9pa/bhmaps/releases/download/v2.6.0/bhmaps-v2.6.0-win-x64.exe"
        },
        {
          "name": "bhmaps-v2.6.0-win-x64-dotnet.zip",
          "size": 3145728,
          "browser_download_url": "https://github.com/as9pa/bhmaps/releases/download/v2.6.0/bhmaps-v2.6.0-win-x64-dotnet.zip"
        },
        {
          "name": "SHA256SUMS.txt",
          "size": 189,
          "browser_download_url": "https://github.com/as9pa/bhmaps/releases/download/v2.6.0/SHA256SUMS.txt"
        }
      ]
    }
    """;

    [Fact]
    public void Parse_ReadsTagAssetsAndDate()
    {
        var release = ReleaseChecker.Parse(LatestJson);

        Assert.NotNull(release);
        Assert.Equal(new Version(2, 6, 0), release!.Version);
        Assert.Equal("v2.6.0", release.TagName);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 10, 30, 0, TimeSpan.Zero), release.PublishedAt);
        Assert.Equal("https://github.com/as9pa/bhmaps/releases/tag/v2.6.0", release.HtmlUrl);
        Assert.Equal(
            "https://github.com/as9pa/bhmaps/releases/download/v2.6.0/bhmaps-v2.6.0-win-x64.exe",
            release.ExeUrl);
        Assert.Equal(141557760, release.ExeSize);
        Assert.Equal(
            "https://github.com/as9pa/bhmaps/releases/download/v2.6.0/SHA256SUMS.txt",
            release.ChecksumsUrl);
        Assert.Equal("Pack tile menus, copy and move, auto-update.", release.Body);
    }

    [Fact]
    public void Parse_TakesTheSelfContainedExeNotTheZip()
    {
        var release = ReleaseChecker.Parse(LatestJson);

        Assert.NotNull(release);
        Assert.EndsWith("bhmaps-v2.6.0-win-x64.exe", release!.ExeUrl);
        Assert.Equal("bhmaps-v2.6.0-win-x64.exe", ReleaseChecker.ExeName(release.Version));
    }

    [Fact]
    public void Parse_ReturnsNullForADraft() =>
        Assert.Null(ReleaseChecker.Parse(LatestJson.Replace("\"draft\": false", "\"draft\": true")));

    [Fact]
    public void Parse_ReturnsNullForAPrerelease() =>
        Assert.Null(ReleaseChecker.Parse(LatestJson.Replace("\"prerelease\": false", "\"prerelease\": true")));

    [Theory]
    [InlineData("\"tag_name\": \"nightly\"")]
    [InlineData("\"tag_name\": \"v2.6\"")]
    [InlineData("\"tag_name\": \"\"")]
    public void Parse_ReturnsNullForATagThatIsNotVMajorMinorBuild(string tag)
    {
        Assert.Null(ReleaseChecker.Parse(LatestJson.Replace("\"tag_name\": \"v2.6.0\"", tag)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void Parse_ReturnsNullForJunk(string json) => Assert.Null(ReleaseChecker.Parse(json));

    [Fact]
    public void Parse_KeepsTheReleaseWhenTheExeAssetIsMissing()
    {
        var json = LatestJson.Replace("bhmaps-v2.6.0-win-x64.exe", "bhmaps-v2.6.0-win-arm64.exe");

        var release = ReleaseChecker.Parse(json);

        Assert.NotNull(release);
        Assert.Null(release!.ExeUrl);
        Assert.Equal(0, release.ExeSize);
    }

    [Theory]
    [InlineData("2.5.0", true)]
    [InlineData("2.5.9", true)]
    [InlineData("1.9.9", true)]
    [InlineData("2.6.0", false)]
    [InlineData("2.6.1", false)]
    [InlineData("3.0.0", false)]
    public void IsNewer_ComparesThreeParts(string current, bool expected)
    {
        var release = ReleaseChecker.Parse(LatestJson);

        Assert.NotNull(release);
        Assert.Equal(expected, ReleaseChecker.IsNewer(release!, Version.Parse(current)));
    }

    [Fact]
    public void IsNewer_IgnoresTheAssemblysFourthPart()
    {
        var release = ReleaseChecker.Parse(LatestJson);

        // Assembly versions carry a fourth part; 2.6.0.0 is not older than 2.6.0.
        Assert.False(ReleaseChecker.IsNewer(release!, new Version(2, 6, 0, 0)));
    }
}
