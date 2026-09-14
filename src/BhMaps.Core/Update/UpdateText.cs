using System.Globalization;

namespace BhMaps.Core.Update;

/// <summary>Spec 7.3's wording, in one place, so the top bar, the Version row and the Updates row say the same
/// thing the same way. Every date is formatted invariant: the copy is English and reads the same everywhere.</summary>
public static class UpdateText
{
    /// <summary>"today, 15:40", "yesterday, 15:40", "14 Sep 2026", or "never".</summary>
    public static string CheckedWhen(DateTimeOffset? last, DateTimeOffset now)
    {
        if (last is not { } then)
        {
            return "never";
        }

        var local = then.ToLocalTime();
        var today = now.ToLocalTime().Date;
        var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (local.Date == today)
        {
            return $"today, {time}";
        }

        return local.Date == today.AddDays(-1)
            ? $"yesterday, {time}"
            : ReleaseDate(then);
    }

    /// <summary>"14 Sep 2026", the release date the Version row names.</summary>
    public static string ReleaseDate(DateTimeOffset published) =>
        published.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>"2.6.0", never the assembly's fourth part.</summary>
    public static string Short(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";

    /// <summary>Whole megabytes both sides, because a byte count moving under a progress bar is noise.</summary>
    public static string Megabytes(long done, long total) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{done / 1_048_576} of {total / 1_048_576} MB");
}
