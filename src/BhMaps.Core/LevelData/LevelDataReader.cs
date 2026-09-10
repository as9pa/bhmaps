using System.Text.RegularExpressions;
using System.Xml;

namespace BhMaps.Core.LevelData;

/// <summary>Size and last-write time of one game data file. A missing file stamps as (-1, -1).</summary>
public sealed record FileStamp(string Name, long Size, long MtimeTicks);

/// <summary>What a cached model was read from: the four files plus the key that unlocked them.</summary>
public sealed record LevelDataStamp(uint Key, IReadOnlyList<FileStamp> Files)
{
    /// <summary>Compared by value. The generated record equality would compare Files by reference, and a stamp
    /// read back from the cache is never the same list instance as a freshly taken one.</summary>
    public bool Equals(LevelDataStamp? other) =>
        other is not null && Key == other.Key && Files.SequenceEqual(other.Files);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Key);
        foreach (var file in Files)
        {
            hash.Add(file);
        }

        return hash.ToHashCode();
    }
}

/// <summary>The outcome of one read. Model is null only when the read failed outright; Error carries a sentence
/// for Settings whenever anything went wrong, including a partial read that skipped a level.</summary>
public sealed record LevelDataResult(LevelDataModel? Model, uint Key, string? Error)
{
    public bool Available => Model is not null;
}

/// <summary>Reads the game's four data files into one model. Spec 3.6: nothing throws to the caller, a failure
/// comes back as a sentence in <see cref="LevelDataResult.Error"/> and the app runs without level data.</summary>
public static class LevelDataReader
{
    private const string SwfName = "BrawlhallaAir.swf";
    private const string DynamicName = "Dynamic.swz";
    private const string InitName = "Init.swz";
    private const string GameName = "Game.swz";

    private const int HeaderSize = 8;

    public static readonly string[] DataFileNames = [SwfName, DynamicName, InitName, GameName];

    /// <summary>The LevelName out of markup too malformed to parse, so a skipped entry can still be named.</summary>
    private static readonly Regex LevelNameInMarkup =
        new("LevelName\\s*=\\s*\"([^\"]*)\"|<LevelName>([^<]*)</LevelName>", RegexOptions.Compiled);

    /// <summary>Reads the four files under gameRoot. Never throws.
    /// knownKey is checked first, before scanning the SWF.</summary>
    public static LevelDataResult Read(string gameRoot, uint? knownKey, CancellationToken ct = default)
    {
        var dynamicPath = Path.Combine(gameRoot, DynamicName);
        byte[] header;
        try
        {
            header = ReadHeader(dynamicPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new LevelDataResult(null, 0, $"{DynamicName} could not be read: {ex.Message}");
        }

        var (resolved, keyError) = ResolveKey(gameRoot, dynamicPath, header, knownKey, ct);
        if (resolved is not { } key)
        {
            return new LevelDataResult(null, 0, keyError);
        }

        IReadOnlyList<SwzEntry> entries;
        try
        {
            entries = SwzReader.ReadFile(dynamicPath, key);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new LevelDataResult(null, key, $"{DynamicName} could not be read: {ex.Message}");
        }

        var notes = new List<string>();
        var levels = new List<LevelDesc>();
        var skipped = new List<string>();
        foreach (var entry in entries.Where(e => e.RootElement == "LevelDesc"))
        {
            if (ct.IsCancellationRequested)
            {
                return new LevelDataResult(null, key, "Reading the game data was cancelled.");
            }

            try
            {
                levels.Add(LevelDescParser.Parse(StripBom(entry.Xml)));
            }
            catch (XmlException)
            {
                // Spec 3.6: one unreadable level is skipped by name, not a failed read.
                skipped.Add(LevelNameOf(entry.Xml));
            }
        }

        if (skipped.Count > 0)
        {
            notes.Add($"These level entries could not be read and were skipped: {string.Join(", ", skipped)}.");
        }

        if (levels.Count == 0)
        {
            notes.Add($"{DynamicName} holds no readable level entries.");
        }

        var types = ReadOne(Path.Combine(gameRoot, InitName), key, "LevelTypes", LevelTypesParser.ParseTypes, notes);
        var sets = ReadOne(Path.Combine(gameRoot, GameName), key, "LevelSetTypes", LevelTypesParser.ParseSets, notes);

        var model = new LevelDataModel(levels, types, sets, DateTimeOffset.UtcNow);
        return new LevelDataResult(model, key, notes.Count == 0 ? null : string.Join(" ", notes));
    }

    /// <summary>Size and mtime of the four files. A missing file stamps as (-1, -1).</summary>
    public static LevelDataStamp Stamp(string gameRoot, uint key) =>
        new(key, DataFileNames.Select(name => StampOne(gameRoot, name)).ToList());

    /// <summary>knownKey when it unlocks Dynamic.swz, otherwise whatever the SWF scan turns up.</summary>
    private static (uint? Key, string? Error) ResolveKey(
        string gameRoot, string dynamicPath, byte[] header, uint? knownKey, CancellationToken ct)
    {
        if (knownKey is { } known && SwzKey.Check(header, known, out _))
        {
            return (known, null);
        }

        if (ct.IsCancellationRequested)
        {
            return (null, "Reading the game data was cancelled.");
        }

        var search = SwfKeyFinder.Find(Path.Combine(gameRoot, SwfName), dynamicPath);
        if (search.Key is { } found)
        {
            return (found, null);
        }

        return (null, search.Error is { } why
            ? $"No key could be read from {SwfName}: {why}"
            : $"No key in {SwfName} unlocks {DynamicName}, after {search.CandidatesScanned} candidates.");
    }

    /// <summary>The first entry with the given root element, parsed. Spec 3.6: a missing or unreadable side file
    /// is not fatal, it notes the reason and yields an empty list.</summary>
    private static IReadOnlyList<T> ReadOne<T>(
        string path, uint key, string rootElement, Func<string, IReadOnlyList<T>> parse, List<string> notes)
    {
        try
        {
            var entry = SwzReader.ReadFile(path, key).FirstOrDefault(e => e.RootElement == rootElement);
            if (entry is null)
            {
                notes.Add($"{Path.GetFileName(path)} holds no {rootElement} entry.");
                return [];
            }

            return parse(StripBom(entry.Xml));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or XmlException or ArgumentException or NotSupportedException)
        {
            notes.Add($"{Path.GetFileName(path)} could not be read: {ex.Message}");
            return [];
        }
    }

    private static FileStamp StampOne(string gameRoot, string name)
    {
        try
        {
            var info = new FileInfo(Path.Combine(gameRoot, name));
            return info.Exists
                ? new FileStamp(name, info.Length, info.LastWriteTimeUtc.Ticks)
                : new FileStamp(name, -1, -1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // A file we cannot stat stamps like a missing one, so the cache is simply never valid for it.
            return new FileStamp(name, -1, -1);
        }
    }

    private static byte[] ReadHeader(string path)
    {
        using var stream = File.OpenRead(path);
        var header = new byte[HeaderSize];
        stream.ReadExactly(header);
        return header;
    }

    /// <summary>SwzReader decodes UTF-8 without dropping a byte-order mark and XDocument.Parse rejects a document
    /// that starts with one, so the mark and any leading whitespace come off before parsing.</summary>
    private static string StripBom(string xml) => xml.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');

    private static string LevelNameOf(string xml)
    {
        var match = LevelNameInMarkup.Match(xml);
        if (!match.Success)
        {
            return "an unnamed level";
        }

        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }
}
