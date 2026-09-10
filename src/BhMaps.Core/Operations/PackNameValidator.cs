namespace BhMaps.Core.Operations;

public static class PackNameValidator
{
    /// <summary>True when <paramref name="name"/> can be a folder name directly under packs\. Sets <paramref name="error"/> to "" on success.</summary>
    public static bool IsValid(string? name, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Pack name is empty.";
            return false;
        }

        if (name is "." or "..")
        {
            error = "Pack name cannot be '.' or '..'.";
            return false;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
        {
            error = "Pack name contains characters that are not allowed in a folder name.";
            return false;
        }

        if (name != name.Trim() || name.EndsWith('.'))
        {
            error = "Pack name cannot start or end with a space, or end with a period.";
            return false;
        }

        return true;
    }
}
