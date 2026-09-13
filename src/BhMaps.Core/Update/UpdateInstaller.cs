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

        var text =
            "@echo off\r\n"
            + "setlocal\r\n"
            + "rem Written by BhMaps. Waits for the app to close, swaps the exe, starts it, removes itself.\r\n"
            + ":wait\r\n"
            + $"tasklist /FI \"PID eq {id}\" | find \"{id}\" >nul\r\n"
            + "if not errorlevel 1 (\r\n"
            + "  timeout /t 1 /nobreak >nul\r\n"
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

    /// <summary>The testable half. Self-contained means the runtime the app is running on lives inside the app's
    /// own folder; the framework-dependent build finds it under Program Files instead.</summary>
    internal static bool CanSwap(string exePath, string runtimeDir, string baseDir)
    {
        var folder = Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return false;
        }

        if (!Normalize(runtimeDir).StartsWith(Normalize(baseDir), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var probe = Path.Combine(folder, $".bhmaps-write-probe-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
}
