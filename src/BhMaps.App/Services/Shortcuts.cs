using System.Runtime.InteropServices;
using BhMaps.Core.Settings;

namespace BhMaps.App.Services;

/// <summary>3.3 O1: the Start menu and desktop shortcuts. The files on disk are the state; nothing is saved in
/// settings. Written through the Windows Script Host's WScript.Shell, which every Windows has, so no package is
/// needed. Every call returns an error message, or null when it worked, and never throws to the caller.</summary>
public static class Shortcuts
{
    public static string StartMenuPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft",
        "Windows",
        "Start Menu",
        "Programs",
        "BhMaps.lnk");

    public static string DesktopPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "BhMaps.lnk");

    /// <summary>A dev run is one with an --appdata override. It never touches shortcuts, because the exe it runs
    /// from is a build output, not the one the user launches.</summary>
    public static bool IsDevRun(AppServices services) =>
        !string.Equals(
            Path.GetFullPath(services.AppDataDir).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(SettingsStore.DefaultAppDataDir).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    public static bool Exists(string path) => File.Exists(path);

    /// <summary>Writes the shortcut, replacing one already there: target the running exe, its icon, and its
    /// folder as the working folder.</summary>
    public static string? Create(string path)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            return "The running exe could not be found.";
        }

        return WithShell(shell =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            dynamic link = shell.CreateShortcut(path);
            try
            {
                link.TargetPath = exe;
                link.IconLocation = exe + ",0";
                link.WorkingDirectory = Path.GetDirectoryName(exe) ?? "";
                link.Description = "BhMaps";
                link.Save();
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }

            return null;
        });
    }

    /// <summary>Deletes the shortcut. One that is already gone is not an error.</summary>
    public static string? Remove(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>A shortcut whose target no longer exists (the exe was moved) is rewritten to the running exe.
    /// No shortcut, or one that still works, is left as it is.</summary>
    public static string? RepointIfStale(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        string? target = null;
        var error = WithShell(shell =>
        {
            dynamic link = shell.CreateShortcut(path);
            try
            {
                target = (string?)link.TargetPath;
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }

            return null;
        });
        if (error is not null)
        {
            return error;
        }

        return !string.IsNullOrEmpty(target) && File.Exists(target) ? null : Create(path);
    }

    /// <summary>Creates WScript.Shell, runs the call, and releases it, turning every failure into its message.</summary>
    private static string? WithShell(Func<dynamic, string?> call)
    {
        dynamic? shell = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null)
            {
                return "Windows Script Host is not available.";
            }

            shell = Activator.CreateInstance(type);
            if (shell is null)
            {
                return "Windows Script Host is not available.";
            }

            return call(shell);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            if (shell is not null)
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
