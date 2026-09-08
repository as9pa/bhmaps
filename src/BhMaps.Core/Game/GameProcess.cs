using System.Diagnostics;

namespace BhMaps.Core.Game;

public static class GameProcess
{
    public const string ProcessName = "Brawlhalla";
    public const string SteamRunUrl = "steam://rungameid/291550";

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
}
