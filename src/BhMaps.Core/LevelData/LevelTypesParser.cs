using System.Xml.Linq;

namespace BhMaps.Core.LevelData;

/// <summary>Reads LevelTypes.xml and LevelSetTypes.xml. Values come from an attribute or a same-named child
/// element, whichever is present.</summary>
public static class LevelTypesParser
{
    public static IReadOnlyList<LevelType> ParseTypes(string xml) =>
        XDocument.Parse(xml).Root!
            .Elements("LevelType")
            .Select(e => new LevelType(
                Read(e, "LevelName") ?? "",
                Read(e, "DisplayName") ?? "",
                Flag(e, "DevOnly"),
                Flag(e, "TestLevel"),
                NullIfEmpty(Read(e, "ThumbnailPNGFile"))))
            .ToList();

    public static IReadOnlyList<LevelSet> ParseSets(string xml) =>
        XDocument.Parse(xml).Root!
            .Elements("LevelSetType")
            .Select(e => new LevelSet(
                Read(e, "LevelSetName") ?? "",
                (Read(e, "LevelTypes") ?? "")
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)))
            .ToList();

    /// <summary>An attribute or a same-named child element, whichever is present.</summary>
    private static string? Read(XElement element, string name) =>
        (string?)element.Attribute(name) ?? element.Element(name)?.Value;

    /// <summary>An absent value and an empty one both mean the level names no file.</summary>
    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>True only for an explicit "true"; a missing or unreadable flag is false.</summary>
    private static bool Flag(XElement element, string name) =>
        bool.TryParse(Read(element, name), out var value) && value;
}
