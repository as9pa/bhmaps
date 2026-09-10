using BhMaps.Core.Game;
using BhMaps.Core.Settings;

namespace BhMaps.App.Services;

/// <summary>Spec 6.7: what happens to a write while Brawlhalla is running. The game does not pick up new map art
/// while it is open, so the default is to close it, write, and start it again through Steam.</summary>
public sealed class GameLauncher
{
    private readonly AppServices _services;
    private readonly IDialogs _dialogs;

    public GameLauncher(AppServices services, IDialogs dialogs)
    {
        _services = services;
        _dialogs = dialogs;
    }

    public bool IsRunning => GameProcess.IsRunning();

    /// <summary>Spec 6.7. With whileRunning=restart and the game up: confirm, close, run, relaunch
    /// through Steam. With whileRunning=live, or the game down: just run.
    /// False means the user declined.</summary>
    public async Task<bool> RunWriteAsync(string actionLabel, Func<Task> write)
    {
        if (_services.Settings.WhileRunning == AppSettings.LiveWhileRunning || !IsRunning)
        {
            await write();
            return true;
        }

        var message =
            "Brawlhalla is running, and it does not pick up new map art while it is open. "
            + $"{actionLabel} closes the game, applies the change, and starts it again through Steam.";
        if (!_dialogs.Confirm("Restart and apply", message))
        {
            return false;
        }

        // Closing can take up to GameProcess.CloseTimeout, and it waits, so it stays off the UI thread.
        if (!await Task.Run(() => GameProcess.CloseAndWait()))
        {
            _dialogs.Error(
                "Brawlhalla did not close",
                "Brawlhalla is still running, so nothing was changed. Close the game and try again.");
            return false;
        }

        try
        {
            await write();
        }
        finally
        {
            // BhMaps closed the game, so it starts it again whether or not the write worked.
            Relaunch();
        }

        return true;
    }

    private void Relaunch()
    {
        try
        {
            GameProcess.Launch();
        }
        catch (Exception ex)
            when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ObjectDisposedException)
        {
            // Steam is not there to take the URL. The write is already done, so this is a notice, not a failure.
            _dialogs.Error(
                "Brawlhalla could not be started",
                $"The change was applied, but Brawlhalla could not be started again: {ex.Message} Start it from Steam.");
        }
    }
}
