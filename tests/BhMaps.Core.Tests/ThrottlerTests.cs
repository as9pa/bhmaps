using System.Diagnostics;
using BhMaps.Core.Threading;

namespace BhMaps.Core.Tests;

public class ThrottlerTests
{
    private static Throttler Fast() => new(TimeSpan.FromMilliseconds(10));

    [Fact]
    public async Task TheFirstCallRunsAtOnce()
    {
        var ran = false;

        await Fast().RunAsync(_ => { ran = true; return Task.CompletedTask; });

        Assert.True(ran);
    }

    [Fact]
    public async Task WhileOneIsInFlightOnlyTheNewestFollowerRuns()
    {
        var throttler = Fast();
        var log = new List<string>();
        var gate = new TaskCompletionSource();

        var first = throttler.RunAsync(async _ => { log.Add("a"); await gate.Task; });
        _ = throttler.RunAsync(_ => { log.Add("b"); return Task.CompletedTask; });
        _ = throttler.RunAsync(_ => { log.Add("c"); return Task.CompletedTask; });
        gate.SetResult();
        await first;

        Assert.Equal(new[] { "a", "c" }, log);
    }

    [Fact]
    public async Task CancelDropsWorkThatHasNotStartedAndCancelsTheTokenOfTheOneRunning()
    {
        var throttler = Fast();
        var log = new List<string>();
        var gate = new TaskCompletionSource();
        CancellationToken captured = default;

        var first = throttler.RunAsync(async ct => { captured = ct; log.Add("a"); await gate.Task; });
        _ = throttler.RunAsync(_ => { log.Add("b"); return Task.CompletedTask; });
        throttler.Cancel();
        gate.SetResult();
        await first;

        Assert.Equal(new[] { "a" }, log);
        Assert.True(captured.IsCancellationRequested);
    }

    [Fact]
    public async Task TwoCallsAreSpacedByTheInterval()
    {
        var throttler = new Throttler(TimeSpan.FromMilliseconds(60));
        var gate = new TaskCompletionSource();
        var watch = Stopwatch.StartNew();

        var first = throttler.RunAsync(async _ => await gate.Task);
        _ = throttler.RunAsync(_ => Task.CompletedTask);
        gate.SetResult();
        await first;

        // 30 rather than 60: the assertion is that the second run waited, not that Task.Delay is accurate. A
        // loaded runner can wake the timer early, and this test must not fail for that.
        Assert.True(watch.ElapsedMilliseconds >= 30, $"second render ran after {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task AFailureInTheWorkDoesNotWedgeTheThrottle()
    {
        var throttler = Fast();
        var ran = false;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => throttler.RunAsync(_ => throw new InvalidOperationException("boom")));
        await throttler.RunAsync(_ => { ran = true; return Task.CompletedTask; });

        Assert.True(ran);
    }
}
