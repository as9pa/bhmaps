using BhMaps.Core.LevelData;

namespace BhMaps.App.Services;

/// <summary>Owns the game's level data for the whole app: adopts the cache when the game has not changed,
/// re-reads in the background when it has, and hands Settings one sentence about the state of it (spec 3.5).</summary>
public sealed class LevelDataService
{
    private readonly string _cachePath;
    private readonly Func<string> _gameRoot;

    /// <summary>Captured where the service is built, which is the UI thread, so Changed is raised there.</summary>
    private readonly SynchronizationContext? _context;

    private State _state = new(null, null, null, null);

    public LevelDataService(string appDataDir, Func<string> gameRootAccessor)
    {
        _cachePath = LevelDataCache.PathFor(appDataDir);
        _gameRoot = gameRootAccessor;
        _context = SynchronizationContext.Current;
    }

    /// <summary>Raised on the thread the service was built on once a refresh has published its result.</summary>
    public event Action? Changed;

    public LevelDataModel? Model => Volatile.Read(ref _state).Model;

    /// <summary>Local time of the read the current model came from. Null when there is no model.</summary>
    public DateTimeOffset? ReadAt => Volatile.Read(ref _state).ReadAt;

    public bool Available => Model is not null;

    /// <summary>The Settings line (spec 7.6), carrying the reason when the data could not be read (spec 3.6).</summary>
    public string StatusSentence
    {
        get
        {
            var state = Volatile.Read(ref _state);
            if (state.Model is not null)
            {
                return $"Maps and names come from the game's files and refresh after a game update. Read on {state.ReadAt:d MMMM yyyy}.";
            }

            return state.Error is { } error
                ? "Maps and names come from the game's files. They could not be read: " + error
                : "Maps and names come from the game's files. They have not been read yet.";
        }
    }

    /// <summary>Loads the cache when its four-file stamp still matches.
    /// Fast, synchronous, called at startup. False means a refresh is needed.</summary>
    public bool LoadCached()
    {
        if (LevelDataCache.Load(_cachePath) is not { } cached)
        {
            return false;
        }

        // The fresh stamp is taken with the cached key on purpose: scanning the SWF for a key would cost seconds
        // at startup, and a key that stopped working shows up as a failed read in RefreshAsync instead.
        var stamp = cached.Stamp;
        if (!stamp.Equals(LevelDataReader.Stamp(_gameRoot(), stamp.Key)))
        {
            // The game updated. Keep the key anyway, because it still unlocks the files after most updates and
            // that is the difference between a refresh that takes a moment and one that takes seconds.
            Publish(new State(null, null, null, stamp.Key), raiseChanged: false);
            return false;
        }

        Publish(new State(cached.Model, cached.Model.ReadAtUtc.ToLocalTime(), null, stamp.Key), raiseChanged: false);
        return true;
    }

    /// <summary>Re-reads off the UI thread, writes the cache atomically, raises Changed on completion.
    /// Never throws.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var gameRoot = _gameRoot();
        var key = Volatile.Read(ref _state).Key;
        LevelDataResult result;
        try
        {
            result = await Task.Run(() => LevelDataReader.Read(gameRoot, key, ct), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A cancelled refresh leaves whatever was published before it in place.
            return;
        }
        catch (Exception ex)
        {
            // The reader turns its own failures into a sentence; this catches anything that still escapes,
            // because the startup refresh is not awaited and nothing may throw out of it.
            Publish(new State(null, null, ex.Message, key), raiseChanged: true);
            return;
        }

        if (result.Model is not { } model)
        {
            Publish(new State(null, null, result.Error, key), raiseChanged: true);
            return;
        }

        try
        {
            LevelDataCache.Save(_cachePath, model, LevelDataReader.Stamp(gameRoot, result.Key));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // A cache that cannot be written only costs the next start another read.
        }

        Publish(new State(model, model.ReadAtUtc.ToLocalTime(), result.Error, result.Key), raiseChanged: true);
    }

    private void Publish(State state, bool raiseChanged)
    {
        Volatile.Write(ref _state, state);
        if (!raiseChanged || Changed is not { } changed)
        {
            return;
        }

        if (_context is null)
        {
            changed();
        }
        else
        {
            _context.Post(_ => changed(), null);
        }
    }

    /// <summary>Everything one read produces. Published as a single field so a reader on another thread
    /// never sees a model paired with the previous read's date.</summary>
    private sealed record State(LevelDataModel? Model, DateTimeOffset? ReadAt, string? Error, uint? Key);
}
