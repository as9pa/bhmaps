using System.Diagnostics;

namespace BhMaps.Core.Game;

public static class GameProcess
{
    public const string ProcessName = "Brawlhalla";
    public const string SteamRunUrl = "steam://rungameid/291550";
    public static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(10);

    public static bool IsRunning()
    {
        var processes = Process.GetProcessesByName(ProcessName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }
    }

    /// <summary>Asks Steam to launch Brawlhalla. Steam handles the case where the game is already running.</summary>
    public static void Launch() =>
        Process.Start(new ProcessStartInfo(SteamRunUrl) { UseShellExecute = true })?.Dispose();

    /// <summary>Asks every Brawlhalla process to close its main window, waits up to CloseTimeout,
    /// then terminates the ones still running. True when nothing is left running.</summary>
    public static bool CloseAndWait(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? CloseTimeout);
        var processes = Process.GetProcessesByName(ProcessName);
        try
        {
            foreach (var p in processes)
            {
                try
                {
                    p.CloseMainWindow();
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Already gone, or no main window; the kill below covers it.
                }
            }

            foreach (var p in processes)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining > TimeSpan.Zero)
                {
                    try
                    {
                        p.WaitForExit((int)remaining.TotalMilliseconds);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or SystemException)
                    {
                        // Nothing left to wait on; the kill below covers it.
                    }
                }

                try
                {
                    if (!p.HasExited)
                    {
                        p.Kill(entireProcessTree: true);
                        p.WaitForExit(2000);
                    }
                }
                catch (Exception ex)
                    when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    // Exited on its own, or cannot be killed; the final IsRunning check reports the truth.
                }
            }
        }
        finally
        {
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }

        return !IsRunning();
    }
}
