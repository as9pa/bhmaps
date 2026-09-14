using BhMaps.Core.Update;

namespace BhMaps.Core.Tests;

public class ReleaseCheckerTests
{
    [Fact]
    public void Parse_ReadsTagAssetsAndDate()
    {
        var release = ReleaseChecker.Parse(UpdateSamples.LatestJson);

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
        var release = ReleaseChecker.Parse(UpdateSamples.LatestJson);

        Assert.NotNull(release);
        Assert.EndsWith("bhmaps-v2.6.0-win-x64.exe", release!.ExeUrl);
        Assert.Equal("bhmaps-v2.6.0-win-x64.exe", ReleaseChecker.ExeName(release.Version));
    }

    [Fact]
    public void Parse_ReturnsNullForADraft() =>
        Assert.Null(ReleaseChecker.Parse(UpdateSamples.LatestJson.Replace("\"draft\": false", "\"draft\": true")));

    [Fact]
    public void Parse_ReturnsNullForAPrerelease() =>
        Assert.Null(ReleaseChecker.Parse(UpdateSamples.LatestJson.Replace("\"prerelease\": false", "\"prerelease\": true")));

    [Theory]
    [InlineData("\"tag_name\": \"nightly\"")]
    [InlineData("\"tag_name\": \"v2.6\"")]
    [InlineData("\"tag_name\": \"\"")]
    public void Parse_ReturnsNullForATagThatIsNotVMajorMinorBuild(string tag)
    {
        Assert.Null(ReleaseChecker.Parse(UpdateSamples.LatestJson.Replace("\"tag_name\": \"v2.6.0\"", tag)));
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
        var json = UpdateSamples.LatestJson.Replace("bhmaps-v2.6.0-win-x64.exe", "bhmaps-v2.6.0-win-arm64.exe");

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
        var release = ReleaseChecker.Parse(UpdateSamples.LatestJson);

        Assert.NotNull(release);
        Assert.Equal(expected, ReleaseChecker.IsNewer(release!, Version.Parse(current)));
    }

    [Fact]
    public void IsNewer_IgnoresTheAssemblysFourthPart()
    {
        var release = ReleaseChecker.Parse(UpdateSamples.LatestJson);

        // Assembly versions carry a fourth part; 2.6.0.0 is not older than 2.6.0.
        Assert.False(ReleaseChecker.IsNewer(release!, new Version(2, 6, 0, 0)));
    }

    [Fact]
    public void CheckedWhen_ReadsTodayYesterdayAndADate()
    {
        // The wording is a wall clock the user recognises, so the stamps are built as local times: a fixed UTC
        // instant would read back as a different hour, and sometimes a different day, on a machine off UTC.
        var now = new DateTimeOffset(new DateTime(2026, 9, 14, 18, 0, 0, DateTimeKind.Local));
        var earlier = new DateTimeOffset(new DateTime(2026, 9, 14, 15, 40, 0, DateTimeKind.Local));
        var yesterday = new DateTimeOffset(new DateTime(2026, 9, 13, 15, 40, 0, DateTimeKind.Local));
        var lastWeek = new DateTimeOffset(new DateTime(2026, 9, 8, 9, 0, 0, DateTimeKind.Local));

        Assert.Equal("never", UpdateText.CheckedWhen(null, now));
        Assert.Equal("today, 15:40", UpdateText.CheckedWhen(earlier, now));
        Assert.Equal("yesterday, 15:40", UpdateText.CheckedWhen(yesterday, now));
        Assert.Equal("8 Sep 2026", UpdateText.CheckedWhen(lastWeek, now));
    }

    [Fact]
    public void ReleaseDateAndShortAndMegabytes_MatchTheSpecsCopy()
    {
        Assert.Equal(
            "14 Sep 2026",
            UpdateText.ReleaseDate(new DateTimeOffset(new DateTime(2026, 9, 14, 10, 30, 0, DateTimeKind.Local))));
        Assert.Equal("2.6.0", UpdateText.Short(new Version(2, 6, 0, 0)));
        Assert.Equal("41 of 135 MB", UpdateText.Megabytes(43_000_000, 141_557_760));
        Assert.Equal("0 of 135 MB", UpdateText.Megabytes(0, 141_557_760));
    }
}
