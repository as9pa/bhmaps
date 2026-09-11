namespace BhMaps.Core.Operations;

/// <summary>One picture of the user's own, however many maps it is on: the library copies of it, the in-game
/// slots holding it, and the pack it lives in when it has one. Part A consumes the type for the Apply picture
/// menu; part B builds the library that fills it (spec 4).</summary>
public sealed record CustomPicture(
    string Hash,
    string DisplayName,
    IReadOnlyList<string> LibraryPaths,
    IReadOnlyList<string> InGameSlots,
    string? PackName);
