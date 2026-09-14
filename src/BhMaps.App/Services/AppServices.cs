using System.Net.Http;
using System.Reflection;
using BhMaps.Core.Hashing;
using BhMaps.Core.Imaging;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using BhMaps.Core.Settings;
using BhMaps.Core.Status;
using BhMaps.Core.Update;

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
    private readonly HttpClient _http;

    public AppServices(string appDataDir, string? gameOverride, string? libraryOverride, HttpClient http)
    {
        _http = http;
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
        RowThumbnails = new ThumbnailCache(Thumbnails);
        Updates = new UpdateClient(http);

        // Nothing creates this folder here: UpdateClient.DownloadAsync makes it on the first real download, so a
        // user who never updates never gets it.
        UpdatesDir = Path.Combine(appDataDir, "updates");
        AppVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
    }

    /// <summary>Spec 4.1: Windows' own animation switch, read once, statically, because the behaviours that
    /// ask (Fold, and item 8's scrolling) are attached to elements and have no AppServices to hand. Every fold
    /// snaps and every scroll is instant when it is false.</summary>
    public static bool AnimationsEnabled { get; } = System.Windows.SystemParameters.ClientAreaAnimation;

    public string AppDataDir { get; }

    public string SettingsPath { get; }

    public string HashCachePath { get; }

    public string? GameOverride { get; }

    public string? LibraryOverride { get; }

    public AppSettings Settings { get; private set; }

    public HashCache HashCache { get; }

    public ThumbnailProvider Thumbnails { get; } = new();

    /// <summary>The rows pages' loader over <see cref="Thumbnails" /> (addendum B). One instance for the app, so
    /// a picture decoded for a Backgrounds row is already decoded when a Platforms row asks for it.</summary>
    public ThumbnailCache RowThumbnails { get; }

    public LevelDataService LevelData { get; }

    public RenderQueue Renderer { get; }

    public PreviewCache Previews { get; }

    public UndoStore Undo { get; }

    /// <summary>Spec 7.1: the check and the download, over the one HttpClient the App owns.</summary>
    public UpdateClient Updates { get; }

    /// <summary>Where a downloaded exe lands. Created by the download, not by start-up.</summary>
    public string UpdatesDir { get; }

    /// <summary>This build's version, 0.0.0 when the entry assembly has none, which is what every release
    /// comparison is made against.</summary>
    public Version AppVersion { get; }

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
        // Spec 8: the shell sorts the packs once, here, and every list downstream keeps the order it is given.
        var packs = PackOrder.Sort(PackScanner.ScanAll(LibraryPath), Settings.LastApplied);
        DropStaleStamps(packs);
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

    /// <summary>Spec 8: a stamp naming a pack the library no longer holds would sit in settings.json for good,
    /// so the scan that cannot find it drops it.</summary>
    private void DropStaleStamps(IReadOnlyList<Pack> packs)
    {
        var stamps = Settings.LastApplied;
        if (stamps.Count == 0)
        {
            return;
        }

        var names = packs.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kept = stamps.Where(s => names.Contains(s.Key)).ToDictionary(StringComparer.OrdinalIgnoreCase);
        if (kept.Count != stamps.Count)
        {
            UpdateSettings(Settings with { PackLastApplied = kept });
        }
    }

    /// <summary>Stops the render thread and drops the HttpClient. Called once, from App.Exit.</summary>
    public void Dispose()
    {
        Renderer.Dispose();
        _http.Dispose();
    }
}
