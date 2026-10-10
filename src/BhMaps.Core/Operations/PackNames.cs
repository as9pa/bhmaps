namespace BhMaps.Core.Operations;

public static class PackNames
{
    /// <summary>3.9.3: the name of every pack the app makes on its own.</summary>
    public const string DefaultStem = "Custom Pack";

    /// <summary>"Custom Pack" when no name in <paramref name="existing"/> is that (case-insensitive, as folder names
    /// are on Windows), else "Custom Pack 2", "Custom Pack 3", ... the first number not taken.</summary>
    public static string NextFree(IEnumerable<string> existing, string stem = DefaultStem)
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
