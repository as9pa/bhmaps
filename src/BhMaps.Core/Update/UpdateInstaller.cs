using System.Globalization;
using System.Reflection;

namespace BhMaps.Core.Update;

/// <summary>Spec 7.1, in place: the running exe is renamed to &lt;exe&gt;.old (Windows allows renaming a running
/// exe, only not deleting it), the verified download takes its name, the app starts the new exe with
/// --after-update and closes. The new instance waits for the old one to exit and deletes the .old file. No helper
/// script. Every step here works on paths it is handed, so the tests drive it on temp files.</summary>
public static class UpdateInstaller
{
    public const string OldSuffix = ".old";

    /// <summary>The argument the restarted exe gets, followed by the old process id to wait for.</summary>
    public const string AfterUpdateArg = "--after-update";

    /// <summary>The exe inside the framework-dependent zip.</summary>
    public const string ExeName = "BhMaps.exe";

    /// <summary>What the retired .cmd flow left in the updates folder: its script.</summary>
    private const string OldScriptName = "apply-update.cmd";

    /// <summary>The build this process is. Reads the running process, so it is not tested itself; the rule is.</summary>
    public static UpdateBuild DetectBuild()
    {
        // Assembly.Location is empty inside a single-file bundle, and that is exactly the question asked here.
#pragma warning disable IL3000
        var runtimeBundled = string.IsNullOrEmpty(typeof(object).Assembly.Location);
        var appBundled = string.IsNullOrEmpty(Assembly.GetEntryAssembly()?.Location);
#pragma warning restore IL3000
        return DetectBuild(Environment.ProcessPath, runtimeBundled, appBundled);
    }

    /// <summary>The testable rule. Both published builds are single-file, so the app's own assembly has no file
    /// of its own; a plain build output or dotnet run does, and is a development run that must never swap. Of the
    /// two single-file builds, only the self-contained one also bundles the runtime (the openmacro test).</summary>
    public static UpdateBuild DetectBuild(string? processPath, bool runtimeBundled, bool appBundled)
    {
        if (!appBundled
            || string.IsNullOrEmpty(processPath)
            || Path.GetFileName(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return UpdateBuild.Development;
        }

        return runtimeBundled ? UpdateBuild.SelfContained : UpdateBuild.FrameworkDependent;
    }

    /// <summary>Moves the running exe to .old and the new one into its place. If the second move fails the first
    /// is undone before the exception goes on, so a failed swap leaves the app exactly as it was. A folder the
    /// app cannot write to fails on the first move and touches nothing.</summary>
    public static void Swap(string newExe, string runningExe)
    {
        var oldExe = runningExe + OldSuffix;
        if (File.Exists(oldExe))
        {
            File.Delete(oldExe);
        }

        File.Move(runningExe, oldExe);
        try
        {
            File.Move(newExe, runningExe);
        }
        catch
        {
            File.Move(oldExe, runningExe);
            throw;
        }
    }

    /// <summary>Undoes a completed swap, for when the new exe would not start: the new exe goes back to where it
    /// was downloaded and the running one gets its name back.</summary>
    public static void Rollback(string newExe, string runningExe)
    {
        File.Move(runningExe, newExe, overwrite: true);
        File.Move(runningExe + OldSuffix, runningExe);
    }

    /// <summary>The arguments the new exe starts with: this run's own, so a development --appdata still applies,
    /// minus any earlier wait, plus a wait for this process.</summary>
    public static string[] RestartArgs(IReadOnlyList<string> current, int pid)
    {
        var args = new List<string>();
        for (var i = 0; i < current.Count; i++)
        {
            if (current[i] == AfterUpdateArg)
            {
                i++;
                continue;
            }

            args.Add(current[i]);
        }

        args.Add(AfterUpdateArg);
        args.Add(pid.ToString(CultureInfo.InvariantCulture));
        return [.. args];
    }

    /// <summary>Deletes &lt;exe&gt;.old. False while it is still locked, because the old process has not quite
    /// gone, so the caller can try again shortly.</summary>
    public static bool TryDeleteOld(string runningExe)
    {
        var oldExe = runningExe + OldSuffix;
        try
        {
            if (File.Exists(oldExe))
            {
                File.Delete(oldExe);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Removes what an interrupted download or the retired .cmd flow left in the updates folder. Nothing
    /// here is worth failing a start-up over.</summary>
    public static void CleanUpdatesFolder(string updatesDir)
    {
        if (!Directory.Exists(updatesDir))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(updatesDir))
            {
                var name = Path.GetFileName(file);
                if (name.Equals(OldScriptName, StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An updates folder that cannot be listed is left alone.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Tried again on the next start.
        }
    }
}
