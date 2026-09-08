using BhMaps.Core.Hashing;
using BhMaps.Core.Storage;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class HashingTests
{
    private const string Sha256OfAbc = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [Fact]
    public void FileHasher_ReturnsLowercaseSha256Hex()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("a.png");
        File.WriteAllText(path, "abc");

        Assert.Equal(Sha256OfAbc, FileHasher.Hash(path));
    }

    [Fact]
    public void AtomicFile_WritesContentAndLeavesNoTempFile()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "nested", "settings.json");

        AtomicFile.WriteAllText(path, "one");
        AtomicFile.WriteAllText(path, "two");

        Assert.Equal("two", File.ReadAllText(path));
        Assert.Equal(new[] { "settings.json" }, Directory.GetFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName));
    }

    [Fact]
    public void HashCache_HitsWhenSizeAndMtimeMatch()
    {
        using var tmp = new TempDir();
        var file = tmp.Sub("a.png");
        File.WriteAllText(file, "abc");
        var cache = HashCache.Load(tmp.Sub("cache.json"));

        var first = cache.GetOrCompute(file, 3, 100);
        File.WriteAllText(file, "xyz");
        var hit = cache.GetOrCompute(file, 3, 100);

        Assert.Equal(Sha256OfAbc, first);
        Assert.Equal(first, hit);
    }

    [Fact]
    public void HashCache_RecomputesWhenSizeOrMtimeChanges()
    {
        using var tmp = new TempDir();
        var file = tmp.Sub("a.png");
        File.WriteAllText(file, "abc");
        var cache = HashCache.Load(tmp.Sub("cache.json"));
        var first = cache.GetOrCompute(file, 3, 100);

        File.WriteAllText(file, "abd");
        var afterMtime = cache.GetOrCompute(file, 3, 101);
        File.WriteAllText(file, "abcd");
        var afterSize = cache.GetOrCompute(file, 4, 101);

        Assert.NotEqual(first, afterMtime);
        Assert.NotEqual(afterMtime, afterSize);
        Assert.Equal(FileHasher.Hash(file), afterSize);
    }

    [Fact]
    public void HashCache_RoundTripsThroughJsonWithoutRehashing()
    {
        using var tmp = new TempDir();
        var file = tmp.Sub("a.png");
        File.WriteAllText(file, "abc");
        var cachePath = tmp.Sub("cache.json");
        var cache = HashCache.Load(cachePath);
        var hash = cache.GetOrCompute(file, 3, 100);
        cache.Save();

        var reloaded = HashCache.Load(cachePath);
        Assert.Equal(1, reloaded.Count);
        File.Delete(file);

        // The file is gone, so a recompute would throw. A hit proves the entry survived the round trip.
        Assert.Equal(hash, reloaded.GetOrCompute(file, 3, 100));
    }

    [Fact]
    public void HashCache_DropsMissingFilesOnSave()
    {
        using var tmp = new TempDir();
        var keep = tmp.Sub("keep.png");
        var gone = tmp.Sub("gone.png");
        File.WriteAllText(keep, "k");
        File.WriteAllText(gone, "g");
        var cachePath = tmp.Sub("cache.json");
        var cache = HashCache.Load(cachePath);
        cache.GetOrCompute(keep, 1, 1);
        cache.GetOrCompute(gone, 1, 1);
        File.Delete(gone);

        cache.Save();

        Assert.Equal(1, cache.Count);
        Assert.Equal(1, HashCache.Load(cachePath).Count);
        Assert.DoesNotContain("gone.png", File.ReadAllText(cachePath));
    }

    [Fact]
    public void HashCache_LoadIgnoresMissingOrCorruptFile()
    {
        using var tmp = new TempDir();
        var cachePath = tmp.Sub("cache.json");

        Assert.Equal(0, HashCache.Load(cachePath).Count);
        File.WriteAllText(cachePath, "{ not json");
        Assert.Equal(0, HashCache.Load(cachePath).Count);
    }
}
