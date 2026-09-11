using BhMaps.Core.Game;

namespace BhMaps.App.Services;

/// <summary>Spec 8: a write goes straight into the game folder whether Brawlhalla is running or not, and shows
/// on the next match load. There is no restart flow left; this is the write, and the poll behind the top bar's
/// game line is GameProcess.</summary>
public sealed class GameLauncher
{
    public bool IsRunning => GameProcess.IsRunning();

    /// <summary>Runs the write. Always true: nothing turns a write away any more. The shape is kept so
    /// MainViewModel.RunGameWriteAsync reads as it did and the one call site did not have to be rebuilt.</summary>
    public async Task<bool> RunWriteAsync(string actionLabel, Func<Task> write)
    {
        _ = actionLabel;
        await write();
        return true;
    }
}
