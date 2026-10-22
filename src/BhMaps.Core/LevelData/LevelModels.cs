using System.Text.Json.Serialization;

namespace BhMaps.Core.LevelData;

public sealed record CameraBounds(double X, double Y, double W, double H);

/// <summary>A background slot of a level. W and H are absent in some files.</summary>
public sealed record LevelBackground(string AssetName, double? W, double? H);

/// <summary>One drawn image. A negative W or H means a flip on that axis and is kept verbatim.</summary>
public sealed record LevelAsset(string AssetName, double X, double Y, double W, double H);

/// <summary>One transform node of a level's platform tree. Absent attributes mean identity. A node that names an
/// image on itself owns the first entry of <see cref="Assets"/>, at the node's own origin. <see cref="Moving"/> marks a
/// MovingPlatform, whose subtree moves as one piece apart from the rest of the level (3.2 O1), and
/// <see cref="Travel"/> is where its animation takes it.</summary>
public sealed record PlatformNode(
    double X, double Y, double Scale, double ScaleX, double ScaleY, double Rotation,
    string? Theme,
    IReadOnlyList<LevelAsset> Assets,
    IReadOnlyList<PlatformNode> Children,
    bool Moving = false,
    PlatformTravel? Travel = null)
{
    /// <summary>A themed node is seasonal, and the compositor skips it.</summary>
    [JsonIgnore]
    public bool IsThemed => !string.IsNullOrEmpty(Theme);

    [JsonIgnore]
    public double EffectiveScaleX => Scale * ScaleX;

    [JsonIgnore]
    public double EffectiveScaleY => Scale * ScaleY;
}

/// <summary>Where a MovingPlatform's animation takes it, in its parent's space. <see cref="Offsets"/> are the
/// offsets from its rest position that its path stays inside: every KeyFrame's X and Y (a missing one is 0), in the
/// Animation and in its Phases, and every curve centre (CenterX, CenterY) on a KeyFrame or on the Animation, which
/// bends the path between two keyframes but keeps it inside their hull with the centre. <see cref="Turns"/> is true
/// when a KeyFrame rotates the platform, which offsets cannot describe.</summary>
public sealed record PlatformTravel(IReadOnlyList<TravelOffset> Offsets, bool Turns);

/// <summary>One offset from a moving platform's rest position.</summary>
public sealed record TravelOffset(double X, double Y);

/// <summary>The kind of a collision line: its element name without the "Collision" suffix, so HardCollision is
/// <see cref="Hard"/>.</summary>
public enum CollisionKind
{
    Hard,
    Soft,
    NoSlide,
    BouncyHard,
    BouncySoft,
    BouncyNoSlide,
    Lava,
    PressurePlate,
    SoftPressurePlate,
    GameModeHard,
    Mud,
}

/// <summary>One collision segment in level units, whichever of the game's three forms it was written in (X1 X2 Y,
/// X Y1 Y2, or X1 X2 Y1 Y2). A curved line also names the control point it bends towards in
/// <see cref="AnchorX"/> and <see cref="AnchorY"/>. <see cref="PlatId"/> is the PlatID of the DynamicCollision the
/// line came from, null for a line that never moves. Such a line is already offset by its container's X and Y, which
/// puts it where the game has it at rest: MapCompositor.Walk draws a MovingPlatform at its own X and Y and ignores its
/// Animation, and the game moves the platform and the collision of the same PlatID by the same animation offset, so a
/// later step that animates either adds that offset to both and needs nothing from the parser.</summary>
public sealed record CollisionLine(
    CollisionKind Kind, double X1, double Y1, double X2, double Y2, string? PlatId = null,
    double? AnchorX = null, double? AnchorY = null);

/// <summary><see cref="Collisions"/> sits outside the positional list so every caller that builds a level without
/// collision keeps compiling; it is empty unless the parser fills it.</summary>
public sealed record LevelDesc(
    string LevelName,
    string AssetDir,
    CameraBounds Camera,
    IReadOnlyList<LevelBackground> Backgrounds,
    IReadOnlyList<PlatformNode> Platforms)
{
    public IReadOnlyList<CollisionLine> Collisions { get; init; } = [];
}

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
