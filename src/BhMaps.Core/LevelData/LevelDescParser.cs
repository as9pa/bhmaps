using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BhMaps.Core.LevelData;

/// <summary>Reads one LevelDesc_*.xml. Every value is taken from an attribute or a same-named child element,
/// and anything unreadable falls back to its default rather than failing the file.</summary>
public static class LevelDescParser
{
    /// <summary>The elements of a LevelDesc that are not part of the platform tree.</summary>
    private static readonly string[] NonPlatformElements = ["LevelName", "AssetDir", "CameraBounds", "Background"];

    private static readonly Regex MissingSpace = new("\"([A-Za-z]+=\")", RegexOptions.Compiled);

    /// <summary>Inserts the space missing in LevelDesc_ThreeShips.xml before parsing. Well-formed XML always has
    /// whitespace between attributes, so the pattern cannot match valid markup.</summary>
    public static string FixMalformedAttributes(string xml) => MissingSpace.Replace(xml, "\" $1");

    public static LevelDesc Parse(string xml)
    {
        var root = XDocument.Parse(FixMalformedAttributes(xml)).Root!;
        return new LevelDesc(
            Read(root, "LevelName") ?? "",
            Read(root, "AssetDir") ?? "",
            BuildCamera(root.Element("CameraBounds")),
            root.Elements("Background").Select(BuildBackground).ToList(),
            root.Elements()
                .Where(e => !NonPlatformElements.Contains(e.Name.LocalName))
                .Select(BuildNode)
                .ToList());
    }

    private static CameraBounds BuildCamera(XElement? element) =>
        element is null
            ? new CameraBounds(0, 0, 0, 0)
            : new CameraBounds(Num(element, "X", 0), Num(element, "Y", 0), Num(element, "W", 0), Num(element, "H", 0));

    private static LevelBackground BuildBackground(XElement element) =>
        new(Read(element, "AssetName") ?? "", NumOrNull(element, "W"), NumOrNull(element, "H"));

    private static PlatformNode BuildNode(XElement element)
    {
        var assets = new List<LevelAsset>();
        var children = new List<PlatformNode>();
        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName == "Asset")
            {
                assets.Add(new LevelAsset(
                    Read(child, "AssetName") ?? "",
                    Num(child, "X", 0), Num(child, "Y", 0),
                    Num(child, "W", 0), Num(child, "H", 0)));
            }
            else
            {
                children.Add(BuildNode(child));
            }
        }

        return new PlatformNode(
            Num(element, "X", 0), Num(element, "Y", 0),
            Num(element, "Scale", 1), Num(element, "ScaleX", 1), Num(element, "ScaleY", 1),
            Num(element, "Rotation", 0),
            (string?)element.Attribute("Theme"),
            assets, children);
    }

    /// <summary>An attribute or a same-named child element, whichever is present.</summary>
    private static string? Read(XElement element, string name) =>
        (string?)element.Attribute(name) ?? element.Element(name)?.Value;

    private static double Num(XElement element, string name, double fallback) =>
        NumOrNull(element, name) ?? fallback;

    private static double? NumOrNull(XElement element, string name) =>
        double.TryParse(Read(element, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
