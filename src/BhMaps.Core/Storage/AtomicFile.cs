namespace BhMaps.Core.Storage;

/// <summary>Writes a whole file so that readers never see a half-written result.</summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents) =>
        Write(path, temp => File.WriteAllText(temp, contents));

    public static void WriteAllBytes(string path, byte[] bytes) =>
        Write(path, temp => File.WriteAllBytes(temp, bytes));

    /// <summary>Writes through a temp file beside the target, then moves it over the target.</summary>
    private static void Write(string path, Action<string> writeTemp)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            writeTemp(temp);
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}
