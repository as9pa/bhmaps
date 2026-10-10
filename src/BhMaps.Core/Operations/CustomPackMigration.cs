using BhMaps.Core.Scanning;
using BhMaps.Core.Settings;

namespace BhMaps.Core.Operations;

/// <summary>3.9.3: the pack the app used to make on its own was "My Backgrounds"; it is now "Custom Pack". Once, on
/// the first scan after the update, a packs\My Backgrounds folder is renamed to the next free Custom Pack name the
/// same way Rename pack does it: the folder moves, then the per-pack settings and the applied record follow.
/// Discovered packs are never touched: only folders under packs\ are looked at.</summary>
public static class CustomPackMigration
{
    public const string OldName = "My Backgrounds";

    /// <summary>The settings to save after the migration, or null when there is nothing to save: the flag is
    /// already set, or the rename failed (a locked folder, an IO error) and the next start tries again.</summary>
    public static AppSettings? Run(string libraryPath, string appliedRecordPath, AppSettings settings)
    {
        if (settings.MyBackgroundsMigrated)
        {
            return null;
        }

        var packsRoot = PackScanner.PacksRoot(libraryPath);
        List<string> folders;
        try
        {
            folders = Directory.Exists(packsRoot)
                ? [.. new DirectoryInfo(packsRoot).EnumerateDirectories().Select(d => d.Name)]
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var oldName = folders.FirstOrDefault(n => n.Equals(OldName, StringComparison.OrdinalIgnoreCase));
        if (oldName is null)
        {
            return settings with { MyBackgroundsMigrated = true };
        }

        var newName = PackNames.NextFree(folders);
        if (!PackRenamer.TryRename(libraryPath, oldName, newName, out _))
        {
            return null;
        }

        AppliedRecord.PackRenamed(appliedRecordPath, oldName, newName);
        return settings.WithPackRenamed(oldName, newName) with { MyBackgroundsMigrated = true };
    }
}
