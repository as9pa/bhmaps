namespace BhMaps.Core.Operations;

public static class PackNames
{
    /// <summary>"New Pack" when no name in <paramref name="existing"/> is that (case-insensitive, as folder names
    /// are on Windows), else "New Pack 2", "New Pack 3", ... the first number not taken.</summary>
    public static string NextFree(IEnumerable<string> existing, string stem = "New Pack")
    {
        var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(stem))
        {
            return stem;
        }

        for (var n = 2; ; n++)
        {
            var name = $"{stem} {n}";
            if (!taken.Contains(name))
            {
                return name;
            }
        }
    }
}
