using System.Globalization;

namespace BhMaps.Core.Status;

/// <summary>Spec 8: how long ago something happened, in the words the counts line uses.</summary>
public static class RelativeTime
{
    /// <summary>"just now" under a minute, then minutes, hours, "yesterday", days, and a date past a week. A
    /// <paramref name="then"/> in the future reads as "just now": a clock that moved backwards is not worth a
    /// line of its own.</summary>
    public static string Describe(DateTimeOffset then, DateTimeOffset now)
    {
        var since = now - then;
        if (since < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (since < TimeSpan.FromHours(1))
        {
            return $"{(int)since.TotalMinutes} min ago";
        }

        if (since < TimeSpan.FromDays(1))
        {
            return $"{(int)since.TotalHours} h ago";
        }

        if (since < TimeSpan.FromDays(2))
        {
            return "yesterday";
        }

        if (since < TimeSpan.FromDays(7))
        {
            return $"{(int)since.TotalDays} days ago";
        }

        return then.ToString("'on' d MMM", CultureInfo.InvariantCulture);
    }
}
