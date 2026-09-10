using System.Windows.Threading;

namespace BhMaps.Core.Imaging;

/// <summary>One dedicated STA thread with a Dispatcher.
/// Composition never runs on the UI thread or the thread pool.</summary>
public sealed class RenderQueue : IDisposable
{
    private readonly Thread _thread;
    private readonly Dispatcher _dispatcher;
    private bool _disposed;

    public RenderQueue()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() =>
        {
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "BhMaps render",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _dispatcher = ready.Task.GetAwaiter().GetResult();
    }

    public Task<T> RunAsync<T>(Func<T> work, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _dispatcher.InvokeAsync(work, DispatcherPriority.Normal, ct).Task;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _dispatcher.InvokeShutdown();
        _thread.Join(TimeSpan.FromSeconds(2));
    }
}
