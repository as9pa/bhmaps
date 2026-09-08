namespace BhMaps.Core.Tests.Helpers;

/// <summary>Synchronous IProgress for tests. The BCL Progress&lt;T&gt; posts to a sync context and races the assertions.</summary>
public sealed class SyncProgress(List<string> sink) : IProgress<string>
{
    public void Report(string value) => sink.Add(value);
}
