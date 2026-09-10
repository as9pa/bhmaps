using BhMaps.Core.Hashing;
using BhMaps.Core.Imaging;
using BhMaps.Core.LevelData;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class PreviewCacheTests
{
    /// <summary>Every wait on the render queue is bounded so a stuck worker fails the test instead of hanging it.</summary>
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private static LevelDesc Level() =>
        new("Grove", "Grove", new CameraBounds(0, 0, 100, 100),
            [new LevelBackground("BG_Grove.jpg", null, null)],
            [new PlatformNode(0, 0, 1, 1, 1, 0, null, [new LevelAsset("a.png", 0, 0, 50, 50)], [])]);

    private static void Art(TempDir tmp)
    {
        SyntheticImage.SavePng(Path.Combine(tmp.Path, "game", "Backgrounds", "BG_Grove.jpg"), 8, 8, (_, _) => (0, 0, 255, 255));
        SyntheticImage.SavePng(Path.Combine(tmp.Path, "game", "Grove", "a.png"), 8, 8, (_, _) => (255, 0, 0, 255));
    }

    private static AssetSources Sources(TempDir tmp) => new(Path.Combine(tmp.Path, "game"));

    private static PreviewCache Cache(TempDir tmp, RenderQueue queue, string cacheFile = "hashes.json") =>
        new(tmp.Path, HashCache.Load(tmp.Sub(cacheFile)), queue);

    [Fact]
    public async Task RunAsync_RunsWorkOnAnStaThreadThatIsNotTheCaller()
    {
        using var queue = new RenderQueue();

        var (apartment, threadId) = await queue
            .RunAsync(() => (Thread.CurrentThread.GetApartmentState(), Environment.CurrentManagedThreadId))
            .WaitAsync(Wait);

        Assert.Equal(ApartmentState.STA, apartment);
        Assert.NotEqual(Environment.CurrentManagedThreadId, threadId);
    }

    [Fact]
    public async Task RunAsync_PropagatesExceptionsToTheCaller()
    {
        using var queue = new RenderQueue();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => queue.RunAsync<int>(() => throw new InvalidOperationException("boom")).WaitAsync(Wait));

        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public void KeyFor_ChangesWithTheSizeTheLevelNameAndTheInputContents()
    {
        using var tmp = new TempDir();
        Art(tmp);
        using var queue = new RenderQueue();
        var cache = Cache(tmp, queue);
        var inputs = MapCompositor.CollectInputs(Level(), Sources(tmp));

        var key = cache.KeyFor("Grove", 640, 360, inputs);

        Assert.NotEqual(key, cache.KeyFor("Grove", 1280, 720, inputs));
        Assert.NotEqual(key, cache.KeyFor("Enigma", 640, 360, inputs));

        SyntheticImage.SavePng(Path.Combine(tmp.Path, "game", "Grove", "a.png"), 8, 8, (_, _) => (0, 255, 0, 255));
        Assert.NotEqual(key, Cache(tmp, queue, "hashes2.json").KeyFor("Grove", 640, 360, inputs));
    }

    [Fact]
    public async Task GetOrRenderAsync_WritesAJpegOnceAndReusesItOnTheSecondCall()
    {
        using var tmp = new TempDir();
        Art(tmp);
        using var queue = new RenderQueue();
        var cache = Cache(tmp, queue);

        var first = await cache.GetOrRenderAsync(Level(), 64, 36, Sources(tmp)).WaitAsync(Wait);
        var backDated = DateTime.UtcNow - TimeSpan.FromDays(10);
        File.SetLastWriteTimeUtc(first, backDated);

        var second = await cache.GetOrRenderAsync(Level(), 64, 36, Sources(tmp)).WaitAsync(Wait);

        Assert.Equal(first, second);
        Assert.Single(Directory.GetFiles(cache.Root, "*.jpg"));
        Assert.True(File.GetLastWriteTimeUtc(second) > backDated);
        var bytes = File.ReadAllBytes(second);
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xD8, bytes[1]);
    }

    [Fact]
    public void Sweep_DeletesOnlyFilesOlderThanThirtyDays()
    {
        using var tmp = new TempDir();
        using var queue = new RenderQueue();
        var cache = Cache(tmp, queue);
        var now = DateTimeOffset.UtcNow;
        var stale = Stub(cache.Root, "stale.jpg", now.UtcDateTime - TimeSpan.FromDays(31));
        var fresh = Stub(cache.Root, "fresh.jpg", now.UtcDateTime - TimeSpan.FromDays(29));

        Assert.Equal(1, cache.Sweep(now));

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void Sweep_ReturnsZeroWhenTheCacheFolderDoesNotExistYet()
    {
        using var tmp = new TempDir();
        using var queue = new RenderQueue();
        var cache = Cache(tmp, queue);

        Assert.Equal(0, cache.Sweep(DateTimeOffset.UtcNow));
        Assert.False(Directory.Exists(cache.Root));
    }

    private static string Stub(string root, string name, DateTime writtenUtc)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, name);
        File.WriteAllBytes(path, [0xFF, 0xD8]);
        File.SetLastWriteTimeUtc(path, writtenUtc);
        return path;
    }
}
