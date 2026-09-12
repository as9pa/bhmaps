using BhMaps.Core.Status;

namespace BhMaps.Core.Tests;

public class RelativeTimeTests
{
    [Theory]
    [InlineData(0, "just now")]
    [InlineData(59, "just now")]
    [InlineData(60, "1 min ago")]
    [InlineData(2 * 60, "2 min ago")]
    [InlineData(59 * 60, "59 min ago")]
    [InlineData(60 * 60, "1 h ago")]
    [InlineData(23 * 3600, "23 h ago")]
    [InlineData(24 * 3600, "yesterday")]
    [InlineData(47 * 3600, "yesterday")]
    [InlineData(48 * 3600, "2 days ago")]
    [InlineData(6 * 24 * 3600, "6 days ago")]
    [InlineData(7 * 24 * 3600, "on 5 Sep")]
    [InlineData(-90, "just now")]
    public void Describe_ReadsTheGapInWords(int secondsAgo, string expected)
    {
        var now = new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(expected, RelativeTime.Describe(now.AddSeconds(-secondsAgo), now));
    }
}
