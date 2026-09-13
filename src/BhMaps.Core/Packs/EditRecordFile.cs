using System.Text.Json;
using System.Text.Json.Serialization;
using BhMaps.Core.Storage;

namespace BhMaps.Core.Packs;

/// <summary>Reads and writes the json records a pack keeps beside its pictures. Missing or unreadable files read
/// as empty; writes go to a temporary file and move over the old one.</summary>
public static class EditRecordFile
{
    /// <summary>One shape for every record file: camelCase names, camelCase enum values, indented text somebody
    /// can hand-edit, and nothing written for a field that was never set.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Null when the file is missing or does not parse.</summary>
    public static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Write<T>(string path, T record) =>
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(record, Options));

    /// <summary>System.Text.Json fills a dictionary with the default comparer, so every dictionary a record read
    /// back is rebuilt this way: map folders and picture paths have to match the way Windows matches them.</summary>
    internal static Dictionary<string, T> CaseInsensitive<T>(Dictionary<string, T>? read) =>
        read is null
            ? new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, T>(read, StringComparer.OrdinalIgnoreCase);
}
