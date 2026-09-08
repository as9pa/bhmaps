using BhMaps.Core.Hashing;
using BhMaps.Core.Imaging;
using BhMaps.Core.Model;
using BhMaps.Core.Scanning;
using BhMaps.Core.Settings;
using BhMaps.Core.Status;

namespace BhMaps.App.Services;

public sealed record ScanSnapshot(GameTree Tree, IReadOnlyList<Pack> Packs, StatusReport Status);

/// <summary>Composition root state: settings, hash cache, thumbnails, and the scan that ties them together.</summary>
public sealed class AppServices
{
    public AppServices(string appDataDir, string? gameOverride, string? libraryOverride)
    {
        AppDataDir = appDataDir;
        SettingsPath = Path.Combine(appDataDir, "settings.json");
        HashCachePath = Path.Combine(appDataDir, "hashcache.json");
        GameOverride = gameOverride;
        LibraryOverride = libraryOverride;
        Settings = SettingsStore.Load(SettingsPath);
        HashCache = HashCache.Load(HashCachePath);
    }

    public string AppDataDir { get; }

    public string SettingsPath { get; }

    public string HashCachePath { get; }

    public string? GameOverride { get; }

    public string? LibraryOverride { get; }

    public AppSettings Settings { get; private set; }

    public HashCache HashCache { get; }

    public ThumbnailProvider Thumbnails { get; } = new();

    /// <summary>A command-line override wins over the saved setting.</summary>
    public string GamePath => GameOverride ?? Settings.GamePath;

    public string LibraryPath => LibraryOverride ?? Settings.LibraryPath;

    public void UpdateSettings(AppSettings settings)
    {
        Settings = settings;
        SettingsStore.Save(SettingsPath, settings);
    }

    /// <summary>Scans game and packs, hashes through the cache, saves the cache. Call from a thread pool thread.</summary>
    public ScanSnapshot Scan(IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("game folder");
        var tree = GameTreeScanner.Scan(GamePath);
        ct.ThrowIfCancellationRequested();
        progress?.Report("packs");
        var packs = PackScanner.ScanAll(LibraryPath);
        ct.ThrowIfCancellationRequested();
        progress?.Report("hashing files");
        var status = StatusDetector.Detect(tree, packs, HashCache);
        HashCache.Save();
        return new ScanSnapshot(tree, packs, status);
    }
}
