namespace BhMaps.Core.Settings;

/// <summary>The library folders Steam lists in steamapps\libraryfolders.vdf. A plain text scan rather than a
/// VDF parse: the file is a nest of quoted key/value lines and only the "path" values matter here.</summary>
public static class SteamLibraryFolders
{
    /// <summary>Every "path" value, in file order, with the doubled backslashes VDF escapes them with undone.
    /// Text that holds none, including an empty or garbled file, yields an empty list.</summary>
    public static IReadOnlyList<string> Parse(string vdfText)
    {
        var paths = new List<string>();
        foreach (var line in vdfText.Split('\n'))
        {
            // A key/value line splits into indent, key, gap, value and tail, so the value is always the fourth
            // piece. A carriage return, when there is one, lands in the tail and never in the value.
            var parts = line.Split('"');
            if (parts.Length >= 5 && parts[1] == "path")
            {
                paths.Add(parts[3].Replace(@"\\", @"\"));
            }
        }

        return paths;
    }
}
