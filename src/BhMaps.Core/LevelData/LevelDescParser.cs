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
                .Where(e => !NonPlatformElements.Contains(e.Name.LocalName) && !IsCollision(e))
                .Select(BuildNode)
                .ToList())
        {
            Collisions = BuildCollisions(root),
        };
    }

    /// <summary>Every element named *Collision is collision, never part of the platform tree. DynamicCollision is
    /// the one container among them.</summary>
    private static bool IsCollision(XElement element) =>
        element.Name.LocalName.EndsWith("Collision", StringComparison.Ordinal);

    /// <summary>The lines of a DynamicCollision are relative to its X and Y and are stored offset by them, with its
    /// PlatID (see <see cref="CollisionLine"/> for why the offset belongs here). A kind the enum does not name is
    /// skipped.</summary>
    private static List<CollisionLine> BuildCollisions(XElement root)
    {
        var lines = new List<CollisionLine>();
        foreach (var element in root.Elements().Where(IsCollision))
        {
            if (element.Name.LocalName == "DynamicCollision")
            {
                var platId = Read(element, "PlatID");
                var x = Num(element, "X", 0);
                var y = Num(element, "Y", 0);
                foreach (var child in element.Elements().Where(IsCollision))
                {
                    AddLine(lines, child, x, y, platId);
                }
            }
            else
            {
                AddLine(lines, element, 0, 0, null);
            }
        }

        return lines;
    }

    /// <summary>A horizontal line names one Y and a vertical line one X; both ends take it.</summary>
    private static void AddLine(List<CollisionLine> lines, XElement element, double dx, double dy, string? platId)
    {
        var name = element.Name.LocalName[..^"Collision".Length];
        if (!Enum.TryParse<CollisionKind>(name, out var kind) || !Enum.IsDefined(kind))
        {
            return;
        }

        var x = NumOrNull(element, "X");
        var y = NumOrNull(element, "Y");
        var anchorX = NumOrNull(element, "AnchorX");
        var anchorY = NumOrNull(element, "AnchorY");
        lines.Add(new CollisionLine(
            kind,
            dx + (NumOrNull(element, "X1") ?? x ?? 0), dy + (NumOrNull(element, "Y1") ?? y ?? 0),
            dx + (NumOrNull(element, "X2") ?? x ?? 0), dy + (NumOrNull(element, "Y2") ?? y ?? 0),
            platId,
            dx + anchorX, dy + anchorY));
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

        // A node can name an image on itself instead of in an Asset child. It is drawn at the node's own origin,
        // sized by the node's own W and H, and before anything nested inside the node. A sized name is the only
        // one that is map art: the game also names SWF animation symbols this way (a__AnimationPressurePlate,
        // a_LevelAnim_*), and those carry no W or H and no file.
        var width = Num(element, "W", 0);
        var height = Num(element, "H", 0);
        if (width != 0 && height != 0 && Read(element, "AssetName") is { Length: > 0 } owned)
        {
            assets.Add(new LevelAsset(owned, 0, 0, width, height));
        }

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
            assets, children,
            element.Name.LocalName == "MovingPlatform");
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
