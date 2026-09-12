namespace BhMaps.App.ViewModels;

/// <summary>What a tile's Edit hands the editor: the picture that was clicked, the pack it lives in when it is in
/// one, and the slot it fills when the tile knows it. Every field but the path may be absent, because a custom
/// picture belongs to no pack and no map (spec 7.2).</summary>
public sealed record BackgroundEditorRequest(string SourcePath, string? PackName, string? Slot);

/// <summary>One line of the editor's Map combo: the file name the game expects, and the maps that share it,
/// joined with ", ". The slot is never the label; the user picks a map (spec 7.2).</summary>
public sealed record MapSlotChoice(string Slot, string DisplayNames)
{
    /// <summary>The first row of the Map combo: no slot, so Save writes the picture under its own name and it
    /// belongs to every map rather than to one (spec 5).</summary>
    public static readonly MapSlotChoice AllMaps = new("", "All maps");

    /// <summary>True for the "All maps" row, which is the only row without a slot.</summary>
    public bool IsAllMaps => Slot.Length == 0;

    /// <summary>Under DisplayMemberPath a combo still names its row with the item's ToString, so the record has to
    /// say the map names out loud or a screen reader reads the field dump instead (spec 7.2).</summary>
    public override string ToString() => DisplayNames;
}
