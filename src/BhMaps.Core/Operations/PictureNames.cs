namespace BhMaps.Core.Operations;

/// <summary>What a picture is called. A picture's name is the name of its file in the library without the
/// extension, so there is nothing to keep in step: rename the file and the tile, the map card's tag and the
/// panel's sentence all say the new name at the next scan. A name coming in off a camera or a download is
/// cleaned on the way into the library, because "castle_night-v2" is a file name and not a name for a picture.
/// </summary>
public static class PictureNames
{
    /// <summary>What a picture whose base name cleans away to nothing is called, because "___.jpg" still has to
    /// say something on its tile.</summary>
    public const string Fallback = "Picture";

    private static readonly HashSet<char> InvalidChars = [.. Path.GetInvalidFileNameChars()];

    /// <summary>The file name a picture takes in the library: the extension exactly as it came, the base name
    /// with underscores and hyphens opened out into spaces and runs of whitespace collapsed. The case is the
    /// user's, so nothing is title-cased and nothing is lowered.</summary>
    public static string Clean(string fileName) =>
        FileName(
            Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Replace('-', ' '),
            Path.GetExtension(fileName));

    /// <summary>A file name out of a name the user typed: whatever Windows will not take in a file name is
    /// dropped, whitespace is collapsed and trimmed, and the extension is added back. Typed text keeps its own
    /// underscores and hyphens, which are there because the user put them there.</summary>
    public static string FileName(string baseName, string extension)
    {
        var kept = string.Concat(baseName.Where(c => !InvalidChars.Contains(c)));
        var cleaned = string.Join(' ', kept.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return (cleaned.Length > 0 ? cleaned : Fallback) + extension;
    }

    /// <summary>The same name when nothing holds it, and "sunset (2).jpg", then "sunset (3).jpg", when something
    /// does. Names are compared ignoring case, as Windows compares them.</summary>
    public static string Unique(string cleaned, IEnumerable<string> existingNames)
    {
        var taken = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(cleaned))
        {
            return cleaned;
        }

        var stem = Path.GetFileNameWithoutExtension(cleaned);
        var extension = Path.GetExtension(cleaned);
        var next = 2;
        string candidate;
        do
        {
            candidate = $"{stem} ({next}){extension}";
            next++;
        }
        while (taken.Contains(candidate));

        return candidate;
    }

    /// <summary>The names a set of files will take in one pack, in source order: each one cleaned, then made
    /// unique against what the pack already holds and against the names this same import has claimed on the way
    /// through, so importing two folders' worth of "sunset.jpg" keeps both.</summary>
    public static IReadOnlyList<string> ForImport(IEnumerable<string> fileNames, IEnumerable<string> existingNames)
    {
        var taken = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();
        foreach (var fileName in fileNames)
        {
            var name = Unique(Clean(fileName), taken);
            taken.Add(name);
            names.Add(name);
        }

        return names;
    }
}
