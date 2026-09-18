namespace BhMaps.Core.Text;

/// <summary>Cutting a written line down to the part that has to be read (3.1). The app writes its done lines as
/// whole sentences, and now that the line sits in the top bar there is room for one of them.</summary>
public static class Sentences
{
    /// <summary>The first sentence of <paramref name="text"/>, up to and including the period that ends it, or
    /// the whole text when nothing in it ends a sentence. The boundary is ". " rather than a period on its own,
    /// because a file name and a game version carry periods that end nothing.</summary>
    public static string First(string text)
    {
        var trimmed = text.Trim();
        var end = trimmed.IndexOf(". ", StringComparison.Ordinal);
        return end < 0 ? trimmed : trimmed[..(end + 1)];
    }
}
