namespace BhMaps.Core.Threading;

/// <summary>A rate limit rather than a quiet period (spec 7.2): the first call runs at once, calls that arrive
/// while one is in flight collapse into a single follow-up, and only the newest of those survives. One piece of
/// work runs at a time and two are never closer together than the interval. The app's Debouncer is the other
/// shape: it waits for the typing to stop. Call from the UI thread; the work itself is free to go off it.</summary>
public sealed class Throttler
{
    /// <summary>One 60 Hz frame, the fastest a preview can usefully be redrawn.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(16);

    private readonly TimeSpan _interval;
    private Func<CancellationToken, Task>? _pending;
    private CancellationTokenSource? _cts;
    private bool _running;

    public Throttler()
        : this(DefaultInterval)
    {
    }

    public Throttler(TimeSpan interval) => _interval = interval;

    /// <summary>Drops the queued call and cancels the token the running one holds.</summary>
    public void Cancel()
    {
        _pending = null;
        _cts?.Cancel();
    }

    /// <summary>The fire-and-forget form, for a caller with no task to await. Deliberately async void, exactly as
    /// the app's Debouncer.Run: a non-cancellation failure is posted back to the UI thread and reaches App's
    /// DispatcherUnhandledException handler instead of being swallowed as an unobserved task.</summary>
    public async void Run(Func<CancellationToken, Task> work) => await RunAsync(work);

    /// <summary>Completes when the queue this call joined has drained. Cancellation is swallowed; anything else
    /// the work throws comes back out to the caller, and the throttle is left usable.</summary>
    public async Task RunAsync(Func<CancellationToken, Task> work)
    {
        _pending = work;
        if (_running)
        {
            // The loop below is still turning and will pick up whatever _pending holds by then.
            return;
        }

        _running = true;
        try
        {
            while (_pending is { } next)
            {
                _pending = null;
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                try
                {
                    await next(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Superseded by a newer set of values.
                }

                // The floor between two runs, and the window a call arriving now is collapsed into.
                await Task.Delay(_interval);
            }
        }
        finally
        {
            _running = false;
        }
    }
}
