using System.Globalization;
using System.Runtime.InteropServices;

namespace BhMaps.Core.Update;

/// <summary>Spec 7.1: the swap runs after the app is gone, so it cannot run inside the app. A .cmd waits for the
/// process to exit, moves the new exe over the old one and starts it. Every path is quoted: the app is normally
/// under a Program Files path with a space in it.</summary>
public static class UpdateInstaller
{
    public const string ScriptName = "apply-update.cmd";

    public const string OldSuffix = ".old";

    /// <summary>Writes the script and returns its path. The script is plain ASCII cmd, written fresh every time,
    /// and deletes itself last.</summary>
    public static string WriteApplyScript(string updatesDir, string newExe, string runningExe, int pid)
    {
        Directory.CreateDirectory(updatesDir);
        var scriptPath = Path.Combine(updatesDir, ScriptName);
        var oldExe = runningExe + OldSuffix;
        var id = pid.ToString(CultureInfo.InvariantCulture);

        // ping is the wait, not timeout: the script is started with no console of its own, and timeout fails
        // outright without one. Two pings to the loopback address are about one second.
        var text =
            "@echo off\r\n"
            + "setlocal\r\n"
            + "rem Written by BhMaps. Waits for the app to close, swaps the exe, starts it, removes itself.\r\n"
            + ":wait\r\n"
            + $"tasklist /FI \"PID eq {id}\" | find \"{id}\" >nul\r\n"
            + "if not errorlevel 1 (\r\n"
            + "  ping -n 2 127.0.0.1 >nul\r\n"
            + "  goto wait\r\n"
            + ")\r\n"
            + $"if exist \"{oldExe}\" del /f /q \"{oldExe}\" >nul 2>&1\r\n"
            + $"move /y \"{runningExe}\" \"{oldExe}\" >nul\r\n"
            + "if errorlevel 1 goto fail\r\n"
            + $"move /y \"{newExe}\" \"{runningExe}\" >nul\r\n"
            + "if errorlevel 1 goto restore\r\n"
            + $"start \"\" \"{runningExe}\"\r\n"
            + $"del /f /q \"{oldExe}\" >nul 2>&1\r\n"
            + "goto done\r\n"
            + ":restore\r\n"
            + $"move /y \"{oldExe}\" \"{runningExe}\" >nul 2>&1\r\n"
            + ":fail\r\n"
            + $"start \"\" \"{runningExe}\"\r\n"
            + ":done\r\n"
            + "del /f /q \"%~f0\"\r\n";

        File.WriteAllText(scriptPath, text);
        return scriptPath;
    }

    /// <summary>False means the button reads "Open release page" instead of offering a download (spec 7.3): the
    /// framework-dependent zip build cannot be swapped by one file, and a folder the user cannot write to cannot
    /// be swapped at all.</summary>
    public static bool CanSwap(string exePath) =>
        CanSwap(exePath, RuntimeEnvironment.GetRuntimeDirectory(), AppContext.BaseDirectory);

    /// <summary>The testable half. The test is for the framework-dependent build, not for the self-contained one:
    /// the shipped exe is single-file self-contained, so its runtime extracts to a folder under %TEMP%\.net and is
    /// nowhere near the app. Only the framework-dependent build runs on a shared framework install, and only that
    /// one cannot be swapped by moving a single file. <paramref name="baseDir"/> is the app's own folder; the rule
    /// no longer reads it, and it stays because the caller has it and a future rule may need it again.</summary>
    internal static bool CanSwap(string exePath, string runtimeDir, string baseDir)
    {
        _ = baseDir;

        var folder = Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return false;
        }

        var sep = Path.DirectorySeparatorChar;
        var sharedFramework = $"{sep}dotnet{sep}shared{sep}Microsoft.NETCore.App{sep}";
        if (Normalize(runtimeDir).Contains(sharedFramework, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var probe = Path.Combine(folder, $".bhmaps-write-probe-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, "");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(probe))
                {
                    File.Delete(probe);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A probe that cannot be removed is not a reason to refuse the update; it is an empty file.
            }
        }
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
}
