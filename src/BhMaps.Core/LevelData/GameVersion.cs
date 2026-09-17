using System.Text;
using System.Text.RegularExpressions;

namespace BhMaps.Core.LevelData;

/// <summary>Digs the patch number out of BrawlhallaAir.swf. The .swz files carry no version and the game's exe
/// reports 1.0, so the one place the patch is written down is a string constant inside the SWF (3.0).</summary>
public static class GameVersion
{
    /// <summary>Shortest and longest run of printable bytes worth treating as a string constant. A version is
    /// eleven characters, and anything longer than this is prose or a path, not a version.</summary>
    private const int MinRun = 3;
    private const int MaxRun = 32;

    /// <summary>MAJOR.MINOR.BUILD, where the build is the long patch number. The bound on the build is what keeps
    /// "1.0" and every "1.2.3" style constant in the file from passing for a game version.</summary>
    private static readonly Regex VersionLiteral =
        new(@"^\d{1,3}\.\d{1,3}\.\d{3,8}$", RegexOptions.Compiled);

    /// <summary>The game's "10.10" out of the SWF at swfPath, or null when the file holds no version, cannot be
    /// read or is not an SWF this app inflates. Never throws: the version is a nicety on one Settings line.
    /// </summary>
    public static string? Read(string swfPath)
    {
        try
        {
            return Parse(Literals(SwfKeyFinder.ReadBody(swfPath)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The major.minor of the first literal shaped like a full version, or null when there is none.
    /// </summary>
    public static string? Parse(IEnumerable<string> literals)
    {
        foreach (var literal in literals)
        {
            if (!VersionLiteral.IsMatch(literal))
            {
                continue;
            }

            var parts = literal.Split('.');
            return $"{parts[0]}.{parts[1]}";
        }

        return null;
    }

    /// <summary>Runs of printable ASCII in the inflated body. The SWF's constant pools are length-prefixed UTF-8
    /// rather than terminated, so the version is found by shape instead of by walking the pool.</summary>
    private static IEnumerable<string> Literals(byte[] body)
    {
        var start = 0;
        for (var i = 0; i <= body.Length; i++)
        {
            if (i < body.Length && body[i] is >= 0x20 and < 0x7F)
            {
                continue;
            }

            var length = i - start;
            if (length is >= MinRun and <= MaxRun)
            {
                yield return Encoding.ASCII.GetString(body, start, length);
            }

            start = i + 1;
        }
    }
}
