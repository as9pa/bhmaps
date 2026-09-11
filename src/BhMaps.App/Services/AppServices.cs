using BhMaps.Core.Hashing;
using BhMaps.Core.Imaging;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using BhMaps.Core.Settings;
using BhMaps.Core.Status;

namespace BhMaps.App.Services;

public sealed record ScanSnapshot(
    GameTree Tree,
    IReadOnlyList<Pack> Packs,
    StatusReport Status,
    MapCatalog Catalog,
    IReadOnlyDictionary<string, MapStatus> MapStatuses,
    IReadOnlyList<LibraryBackground> Backgrounds,
    Pack? DefaultPack,
    IReadOnlyList<CustomPicture> CustomPictures);

/// <summary>Composition root state: settings, hash cache, level data, previews, undo, and the scan that ties
/// them together.</summary>
public sealed class AppServices : IDisposable
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
        LevelData = new LevelDataService(appDataDir, () => GameRoot);
        Renderer = new RenderQueue();
        Previews = new PreviewCache(appDataDir, HashCache, Renderer);
        Undo = new UndoStore(appDataDir);
    }

    public string AppDataDir { get; }

    public string SettingsPath { get; }

    public string HashCachePath { get; }

    public string? GameOverride { get; }

    public string? LibraryOverride { get; }

    public AppSettings Settings { get; private set; }

    public HashCache HashCache { get; }

    public ThumbnailProvider Thumbnails { get; } = new();

    public LevelDataService LevelData { get; }

    public RenderQueue Renderer { get; }

    public PreviewCache Previews { get; }

    public UndoStore Undo { get; }

    /// <summary>A command-line override wins over the saved setting.</summary>
    public string GamePath => GameOverride ?? Settings.GamePath;

    public string LibraryPath => LibraryOverride ?? Settings.LibraryPath;

    /// <summary>The folder holding the game's four data files, which is the parent of mapArt. A trailing separator
    /// on the saved path is trimmed first, so "...\mapArt\" resolves to the same root as "...\mapArt".</summary>
    public string GameRoot => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(GamePath)) ?? GamePath;

    /// <summary>Disk first, memory second: a write that throws leaves this run using the settings the file still
    /// holds, so what the app is looking at and what it would load next start are never out of step.</summary>
    public void UpdateSettings(AppSettings settings)
    {
        SettingsStore.Save(SettingsPath, settings);
        Settings = settings;
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
        ct.ThrowIfCancellationRequested();

        // Spec 3.6: without level data the app still lists maps, one per game folder, under their folder names.
        var catalog = LevelData.Model is { } model ? MapCatalog.Build(model) : MapCatalog.FromFolders(tree);
        var mapStatuses = MapStatusDetector.Detect(catalog, tree, packs, HashCache);
        HashCache.Save();
        return new ScanSnapshot(
            tree,
            packs,
            status,
            catalog,
            mapStatuses,
            BackgroundLibrary.Build(packs, tree),
            DefaultPack.Find(packs),

            // Built here, inside the scan, because it hashes: every page reads the list rather than computing one.
            CustomPictureLibrary.Build(packs, tree, catalog, HashCache));
    }

    /// <summary>Stops the render thread. Called once, from App.Exit.</summary>
    public void Dispose() => Renderer.Dispose();
}
