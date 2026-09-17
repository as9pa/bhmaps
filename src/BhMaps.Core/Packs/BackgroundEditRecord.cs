using System.Text.Json;
using System.Text.Json.Serialization;

namespace BhMaps.Core.Packs;

/// <summary>How a picture fills its background slot.</summary>
public enum BackgroundMode
{
    Cover,
    Contain,
    Stretch,
    Center,
}

/// <summary>What the background editor remembers about one slot.</summary>
public sealed class BackgroundSlotEntry
{
    public DateTimeOffset SavedAt { get; set; }

    public string Picture { get; set; } = "";

    public BackgroundMode Mode { get; set; }

    public double PanX { get; set; } = 0.5;

    public double PanY { get; set; } = 0.5;

    public double Darken { get; set; }

    /// <summary>Lowercase hex SHA-256 of the file this entry was written for, so a slot replaced outside BhMaps
    /// can be spotted.</summary>
    public string Hash { get; set; } = "";

    /// <summary>Properties this version does not know. Written back untouched.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>The background edits a pack keeps beside its pictures, one entry per slot.</summary>
public sealed class BackgroundEditRecord
{
    public const string FileName = "backgrounds.bhmaps.json";

    public int Version { get; set; } = 1;

    public Dictionary<string, BackgroundSlotEntry> Slots { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Properties this version does not know. Written back untouched.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public static string PathFor(string packRoot) => Path.Combine(packRoot, FileName);

    /// <summary>Empty record when the file is missing or unreadable.</summary>
    public static BackgroundEditRecord Load(string packRoot)
    {
        var record = EditRecordFile.Read<BackgroundEditRecord>(PathFor(packRoot)) ?? new BackgroundEditRecord();
        record.Normalise();
        return record;
    }

    public void Save(string packRoot) => EditRecordFile.Write(PathFor(packRoot), this);

    public BackgroundSlotEntry? Entry(string relativePath) => Slots.GetValueOrDefault(relativePath);

    public void Set(string relativePath, BackgroundSlotEntry entry) => Slots[relativePath] = entry;

    public bool Remove(string relativePath) => Slots.Remove(relativePath);

    private void Normalise() => Slots = EditRecordFile.CaseInsensitive(Slots);
}
