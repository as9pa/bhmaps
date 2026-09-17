using BhMaps.Core.Maps;

namespace BhMaps.App.ViewModels;

/// <summary>Which maps the Add Custom Image window opens on (spec 7.1): none, one named map, or every map. The
/// caller names the target; the window decides which radios it can offer.</summary>
public sealed record AddPicturesTarget(AddPicturesTargetKind Kind, MapEntry? Map);

public enum AddPicturesTargetKind
{
    None,
    Map,
    All,
}
