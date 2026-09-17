namespace BhMaps.Core.Settings;

/// <summary>How a folder path is shown in a row too narrow to hold it (3.0). The drive and the last two folders
/// are what tells two installs apart, so the middle is what goes: the row shows the ends and the full path stays
/// in the tooltip beside it.</summary>
public static class PathText
{
    private const string Dots = "...";

    /// <summary>Shortens <paramref name="path"/> to <paramref name="maxChars"/> by dropping whole segments out of
    /// the middle, keeping as many from the start as fit and always the last two. A path that already fits comes
    /// back unchanged. When even the last two segments cannot fit, the last one is kept and hard-cut with the
    /// dots in front of it, because a name cut at the front still reads as a name. A budget smaller than the dots
    /// gets the path's last characters on their own, so the result never runs past what was asked for.</summary>
    public static string MiddleTruncate(string path, int maxChars)
    {
        if (string.IsNullOrEmpty(path) || maxChars <= 0 || path.Length <= maxChars)
        {
            return path;
        }

        var separators = Separators(path);
        if (separators.Count >= 2)
        {
            // The last two segments, with the separator between them: the tail every result ends with.
            var tailStart = separators[^2] + 1;
            var tail = path[tailStart..];

            // Longest head first, so the result keeps as much of the start as the budget allows. The candidates
            // stop two separators from the end: a head reaching the tail's own separator would drop nothing.
            for (var i = separators.Count - 3; i >= 0; i--)
            {
                var head = path[..separators[i]];

                // The joiner carries the path's own separators, so a path written with forward slashes stays
                // written that way.
                var joiner = $"{path[separators[i]]}{Dots}{path[separators[^2]]}";
                if (head.Length + joiner.Length + tail.Length <= maxChars)
                {
                    return head + joiner + tail;
                }
            }
        }

        var last = separators.Count > 0 ? path[(separators[^1] + 1)..] : path;
        var kept = Dots + last;
        if (kept.Length <= maxChars)
        {
            return kept;
        }

        // A budget under the dots themselves cannot fit them and a tail, so the end of the path is what the row
        // gets: still inside the budget, and the part that says which folder it is.
        return maxChars <= Dots.Length
            ? path[^Math.Min(path.Length, maxChars)..]
            : Dots + last[^Math.Min(last.Length, maxChars - Dots.Length)..];
    }

    /// <summary>Both separators, because a path typed by hand or copied out of a log may use either.</summary>
    private static List<int> Separators(string path)
    {
        var found = new List<int>();
        for (var i = 0; i < path.Length; i++)
        {
            if (path[i] is '\\' or '/')
            {
                found.Add(i);
            }
        }

        return found;
    }
}
