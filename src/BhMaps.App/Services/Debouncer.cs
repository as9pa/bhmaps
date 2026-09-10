namespace BhMaps.App.Services;

/// <summary>One live request at a time (spec 5.3): a new request cancels the one before it, waits out the quiet
/// period, and only then runs. The work is handed the token so it can drop a result a newer request has already
/// superseded. Call from the UI thread; the work itself is free to go off it.</summary>
public sealed class Debouncer
{
    /// <summary>The quiet period every live preview in the app uses.</summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(150);

    private readonly TimeSpan _delay;
    private CancellationTokenSource? _cts;

    public Debouncer()
        : this(DefaultDelay)
    {
    }

    public Debouncer(TimeSpan delay) => _delay = delay;

    /// <summary>Drops the pending request without starting one, for a change that has nothing to render.</summary>
    public void Cancel() => _cts?.Cancel();

    /// <summary>Runs <paramref name="work"/> after the quiet period, unless a later call arrives first. Never
    /// throws for a cancellation; anything else the work throws is the caller's to handle.</summary>
    public async Task RunAsync(Func<CancellationToken, Task> work)
    {
        Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        try
        {
            await Task.Delay(_delay, ct);
            await work(ct);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request.
        }
    }
}
