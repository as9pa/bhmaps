namespace BhMaps.App.ViewModels;

/// <summary>What a tile's Edit hands the editor: the picture that was clicked, the pack it lives in when it is in
/// one, and the slot it fills when the tile knows it. Part C rebuilds the editor around it (spec 7.2).</summary>
public sealed record BackgroundEditorRequest(string SourcePath, string? PackName, string? Slot);
