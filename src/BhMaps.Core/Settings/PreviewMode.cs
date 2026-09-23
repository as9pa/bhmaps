namespace BhMaps.Core.Settings;

/// <summary>3.2 P1 and P2: what the previews on Packs, a pack's page and Maps draw. Both is the whole map, the look
/// every earlier version had. Platforms draws the pieces with no background at all, on a checkerboard. Backgrounds
/// draws the background picture alone, cropped to fill.</summary>
public enum PreviewMode
{
    Both,
    Platforms,
    Backgrounds,
}
