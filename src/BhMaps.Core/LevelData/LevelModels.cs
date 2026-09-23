using System.Text.Json.Serialization;

namespace BhMaps.Core.LevelData;

public sealed record CameraBounds(double X, double Y, double W, double H);

/// <summary>A background slot of a level. W and H are absent in some files.</summary>
public sealed record LevelBackground(string AssetName, double? W, double? H);

/// <summary>One drawn image. A negative W or H means a flip on that axis and is kept verbatim.</summary>
public sealed record LevelAsset(string AssetName, double X, double Y, double W, double H);

/// <summary>One transform node of a level's platform tree. Absent attributes mean identity. A node that names an
/// image on itself owns the first entry of <see cref="Assets"/>, at the node's own origin. <see cref="Moving"/> marks a
/// MovingPlatform, whose subtree moves as one piece apart from the rest of the level (3.2 O1).</summary>
public sealed record PlatformNode(
    double X, double Y, double Scale, double ScaleX, double ScaleY, double Rotation,
    string? Theme,
    IReadOnlyList<LevelAsset> Assets,
    IReadOnlyList<PlatformNode> Children,
    bool Moving = false)
{
    /// <summary>A themed node is seasonal, and the compositor skips it.</summary>
    [JsonIgnore]
    public bool IsThemed => !string.IsNullOrEmpty(Theme);

    [JsonIgnore]
    public double EffectiveScaleX => Scale * ScaleX;

    [JsonIgnore]
    public double EffectiveScaleY => Scale * ScaleY;
}

public sealed record LevelDesc(
    string LevelName,
    string AssetDir,
    CameraBounds Camera,
    IReadOnlyList<LevelBackground> Backgrounds,
    IReadOnlyList<PlatformNode> Platforms);

/// <summary><see cref="ThumbnailFile"/> is the map-select picture the game names in ThumbnailPNGFile, kept
/// verbatim because the game names a .jpg in that field. Null when the level names none.</summary>
public sealed record LevelType(
    string LevelName, string DisplayName, bool DevOnly, bool TestLevel, string? ThumbnailFile = null)
{
    [JsonIgnore]
    public bool Included => !DevOnly && !TestLevel;
}

public sealed record LevelSet(string Name, IReadOnlyList<string> LevelNames);

/// <summary><see cref="Version"/> is the game's "10.10" when the SWF named one, null when it did not (3.0).
/// </summary>
public sealed record LevelDataModel(
    IReadOnlyList<LevelDesc> Levels,
    IReadOnlyList<LevelType> Types,
    IReadOnlyList<LevelSet> Sets,
    DateTimeOffset ReadAtUtc,
    string? Version = null);

/// <summary>Turns the asset names in a LevelDesc into paths relative to mapArt.</summary>
public static class AssetPath
{
    /// <summary>"Snow1.png" under AssetDir "Snow" becomes "Snow\Snow1.png";
    /// "../Snow/Snow1.png" becomes "Snow\Snow1.png".</summary>
    public static string Resolve(string assetDir, string assetName)
    {
        var name = assetName.Replace('/', '\\');
        if (name.StartsWith(@"..\", StringComparison.Ordinal))
        {
            return name[3..];
        }

        return Path.Combine(assetDir, name);
    }

    /// <summary>Backgrounds live in one shared folder: "BG_Grove.jpg" becomes "Backgrounds\BG_Grove.jpg".</summary>
    public static string Background(string assetName) => Resolve("Backgrounds", assetName);

    /// <summary>The folder part of a resolved path, or "" when it names no folder.</summary>
    public static string FolderOf(string relativePath) => Path.GetDirectoryName(relativePath) ?? "";
}
