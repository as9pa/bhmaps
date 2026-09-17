using System.Text.Json;
using System.Text.Json.Serialization;
using BhMaps.Core.Operations;

namespace BhMaps.Core.Packs;

/// <summary>Where a piece takes its picture from: its own, one picture repeated on each piece, one picture cut
/// across every piece, or the working copy the editor is holding.</summary>
public enum PlatformArt
{
    Own,
    EachPiece,
    Across,
    WorkingCopy,
}

/// <summary>What the platform editor remembers about one piece.</summary>
public sealed class PlatformPieceEntry
{
    public int? Opacity { get; set; }

    public int? Hue { get; set; }

    public PlatformArt Art { get; set; }

    public string? Picture { get; set; }

    /// <summary>How the picture fills the piece, or the whole stage for an Across piece. Missing in a version 1
    /// file, which knew one fit only, and reads as Fill.</summary>
    public PictureFit? Fit { get; set; }

    public double? PanX { get; set; }

    public double? PanY { get; set; }

    /// <summary>Lowercase hex SHA-256 of the file this entry was written for, so a piece replaced outside BhMaps
    /// can be spotted.</summary>
    public string Hash { get; set; } = "";

    /// <summary>Properties this version does not know. Written back untouched.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>Every piece of one map, as the editor last saved them.</summary>
public sealed class PlatformMapEntry
{
    public DateTimeOffset SavedAt { get; set; }

    public Dictionary<string, PlatformPieceEntry> Pieces { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Properties this version does not know. Written back untouched.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>The platform edits a pack keeps beside its pictures, one entry per piece.</summary>
public sealed class PlatformEditRecord
{
    public const string FileName = "platforms.bhmaps.json";

    /// <summary>2 since the pieces carry a fit; a version 1 file reads as Fill and is stamped 2 when it is saved.</summary>
    public const int SchemaVersion = 2;

    public int Version { get; set; } = SchemaVersion;

    public Dictionary<string, PlatformMapEntry> Maps { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Properties this version does not know. Written back untouched.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public static string PathFor(string packRoot) => Path.Combine(packRoot, FileName);

    /// <summary>Empty record when the file is missing or unreadable.</summary>
    public static PlatformEditRecord Load(string packRoot)
    {
        var record = EditRecordFile.Read<PlatformEditRecord>(PathFor(packRoot)) ?? new PlatformEditRecord();
        record.Normalise();
        return record;
    }

    public void Save(string packRoot)
    {
        Version = SchemaVersion;
        EditRecordFile.Write(PathFor(packRoot), this);
    }

    public PlatformMapEntry? Map(string mapFolder) => Maps.GetValueOrDefault(mapFolder);

    public PlatformPieceEntry? Entry(string mapFolder, string relativePath) =>
        Map(mapFolder)?.Pieces.GetValueOrDefault(relativePath);

    /// <summary>Replaces the whole entry set of one map (spec 4, last bullet).</summary>
    public void SetMap(string mapFolder, DateTimeOffset savedAt, IReadOnlyDictionary<string, PlatformPieceEntry> pieces)
    {
        Maps[mapFolder] = new PlatformMapEntry
        {
            SavedAt = savedAt,
            Pieces = new Dictionary<string, PlatformPieceEntry>(pieces, StringComparer.OrdinalIgnoreCase),
        };
    }

    public bool RemoveMap(string mapFolder) => Maps.Remove(mapFolder);

    private void Normalise()
    {
        Maps = EditRecordFile.CaseInsensitive(Maps);
        foreach (var map in Maps.Values)
        {
            map.Pieces = EditRecordFile.CaseInsensitive(map.Pieces);
        }
    }
}
