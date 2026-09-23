using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BhMaps.App.Services;
using BhMaps.App.ViewModels.Pages;
using BhMaps.App.Views;
using BhMaps.Core.Game;
using BhMaps.Core.Hashing;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Packs;
using BhMaps.Core.Scanning;
using BhMaps.Core.Text;
using BhMaps.Core.Update;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>One part of one map's art, put back to default alongside a delete. Slots is empty when only the
/// map's platforms reset, and Platforms is false when only its pictures do; both are set when the delete takes
/// away everything the game was showing for the map.</summary>
public sealed record PartReset(MapEntry Map, bool Platforms, IReadOnlyList<string> Slots);

/// <summary>The shell (decision D13): the busy boundary, navigation between the five pages, the scan that feeds
/// them, and the undo of the last game write.</summary>
public partial class MainViewModel : ObservableObject
{
    /// <summary>The level-set filter before a chip is picked, and the chip label the pages match it against.</summary>
    public const string AllLevelSet = "All";

    private static readonly TimeSpan GamePollInterval = TimeSpan.FromSeconds(3);

    /// <summary>Spec 7.3: how long after the first scan the one update check of the run starts.</summary>
    private static readonly TimeSpan UpdateCheckDelay = TimeSpan.FromSeconds(5);

    private readonly GameLauncher _launcher;
    private readonly IReadOnlyList<PageViewModel> _pages;

    /// <summary>Spec 10.4: why a map's map-select thumbnail was left alone by the last write that named it, by
    /// map folder. A write replaces the entry of every map it named: removed when the thumbnail was written.</summary>
    private readonly Dictionary<string, string> _thumbnailNotes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Game-relative paths a top-up of the Default pack has already tried this run, so a file that
    /// cannot be copied is not tried again by every rescan after it.</summary>
    private readonly HashSet<string> _defaultTopUpTried = new(StringComparer.OrdinalIgnoreCase);

    private readonly DispatcherTimer _gameTimer;
    private readonly DispatcherTimer _updateTimer;

    /// <summary>Spec 7.3: the one token every update check runs under, cancelled when the window closes so a
    /// request still in flight does not outlive it.</summary>
    private readonly CancellationTokenSource _updateCts = new();

    private CancellationTokenSource? _cts;

    /// <summary>Set the first time a scan finishes, so the update check is started once a run and no rescan
    /// starts another.</summary>
    private bool _updateCheckStarted;

    /// <summary>Set when the game data lands while a scan is running, so the rescan it needs happens once the
    /// busy boundary clears instead of being dropped.</summary>

    /// <summary>Whether a game-data read is already running. The game poll can ask twice in a row, and a read
    /// takes longer than one tick.</summary>
    private bool _readingGameData;

    public MainViewModel(AppServices services, IDialogs dialogs)
    {
        Services = services;
        Dialogs = dialogs;
        Status = new StatusViewModel(Cancel, UndoAsync);
        _launcher = new GameLauncher();

        // Before the pages, because each of the three chip rows reads it as it is built.
        SelectedLevelSet = AllLevelSet;
        Maps = new MapsViewModel(this);
        Backgrounds = new BackgroundsViewModel(this);
        Platforms = new PlatformsViewModel(this);
        Packs = new PacksViewModel(this);
        PackDetail = new PackDetailViewModel(this);
        SettingsPage = new SettingsPageViewModel(this);
        _pages = [Maps, Backgrounds, Platforms, Packs, PackDetail, SettingsPage];
        CurrentPage = Maps;

        // The startup read usually lands after the first scan, and the catalog that scan built came from the cache
        // or from nothing, so the names have to be rebuilt when it does (spec 3.5). Subscribed once, for the life
        // of the app: the shell outlives the service.
        services.LevelData.Changed += OnLevelDataChanged;

        // Nothing tells the app when Brawlhalla starts or stops, so the top bar's game line asks every 3 seconds.
        // The same tick watches the game folder (3.0): a folder renamed or unmounted under the app shows the
        // pages' missing-folder state on its own, without waiting for a scan to fail on it.
        GameRunning = GameProcess.IsRunning();
        _gameTimer = new DispatcherTimer { Interval = GamePollInterval };
        _gameTimer.Tick += (_, _) =>
        {
            var running = GameProcess.IsRunning();
            var flipped = running != GameRunning;
            GameRunning = running;
            if (!IsBusy)
            {
                GameFolderMissing = !GameFolderExists();
            }

            // The game coming up and the game going down are both moments a patch may just have landed: the
            // launcher updates before it starts the game, and an update can install while it runs (3.0). Nothing
            // else tells the app the four data files moved on, so both edges ask.
            if (flipped)
            {
                _ = RefreshGameDataIfStaleAsync();
            }
        };
        _gameTimer.Start();

        // One shot: the tick stops the timer and starts the check.
        _updateTimer = new DispatcherTimer { Interval = UpdateCheckDelay };
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Stop();
            _ = CheckForUpdateAsync(force: false);
        };
    }

    public AppServices Services { get; }

    public IDialogs Dialogs { get; }

    /// <summary>Result of the last successful scan. Null until the first scan completes.</summary>
    public ScanSnapshot? Snapshot { get; private set; }

    public MapsViewModel Maps { get; }

    public BackgroundsViewModel Backgrounds { get; }

    /// <summary>Addendum C: the tab the owner asked back, between Backgrounds and Packs.</summary>
    public PlatformsViewModel Platforms { get; }

    public PacksViewModel Packs { get; }

    public PackDetailViewModel PackDetail { get; }

    public SettingsPageViewModel SettingsPage { get; }

    /// <summary>Whether Brawlhalla is running, as of the last poll. The top bar's game line shows it.</summary>
    [ObservableProperty]
    public partial bool GameRunning { get; set; }

    [ObservableProperty]
    public partial PageViewModel? CurrentPage { get; set; }

    /// <summary>3.1: the level-set chip Maps, Backgrounds and Platforms all filter by. One value on the shell
    /// rather than one per page, so a chip picked on any of the three is the chip the next one shows. Not
    /// persisted: every run starts on All, as it did before.</summary>
    [ObservableProperty]
    public partial string SelectedLevelSet { get; set; }

    /// <summary>Addendum B: unfolded rows fold back when the page is left, so coming back to a rows page shows
    /// the same thing it shows on a first visit.</summary>
    partial void OnCurrentPageChanging(PageViewModel? oldValue, PageViewModel? newValue)
    {
        if (oldValue is RowsPageViewModel rows)
        {
            rows.FoldAll();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy), nameof(CanWrite))]
    [NotifyCanExecuteChangedFor(
        nameof(RescanFromKeyCommand),
        nameof(RefreshGameCommand),
        nameof(LaunchGameCommand),
        nameof(ImportCommand),
        nameof(UndoCommand),
        nameof(CaptureDefaultsCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>Spec 7.8: the configured game folder is not there. The page header says so and every write into
    /// the game is off until a rescan finds it again. Recomputed around each scan, never guessed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanWrite), nameof(ShowFirstRunBanner))]
    [NotifyCanExecuteChangedFor(nameof(RefreshGameCommand), nameof(UndoCommand), nameof(CaptureDefaultsCommand))]
    public partial bool GameFolderMissing { get; set; }

    /// <summary>The status strip's line (3.0): what is running, what the last operation did, or why it did
    /// nothing. Every outcome in the app is said through it, so nothing else here keeps a line of its own.</summary>
    public StatusViewModel Status { get; }

    /// <summary>Spec 7.3: the latest release the last check found, or null when nothing has been found yet. Set
    /// off the UI thread's work but assigned on it, because the top bar binds to it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUpdateLine))]
    public partial ReleaseInfo? AvailableUpdate { get; set; }

    /// <summary>Whether a check is running right now. The Settings row reads it; the top bar does not.</summary>
    [ObservableProperty]
    public partial bool UpdateChecking { get; set; }

    /// <summary>Whether the last check came back with nothing, which is every failure there is (spec 7.1).</summary>
    [ObservableProperty]
    public partial bool UpdateCheckFailed { get; set; }

    /// <summary>The top bar line shows only for a release newer than this build whose tag has not been waved
    /// away. A later release carries a different tag, so the line comes back on its own.</summary>
    public bool ShowUpdateLine =>
        AvailableUpdate is { } release
        && ReleaseChecker.IsNewer(release, Services.AppVersion)
        && !string.Equals(release.TagName, Services.Settings.DismissedUpdate, StringComparison.OrdinalIgnoreCase);

    /// <summary>Folder name of the map most recently opened on Maps, or null before any. Maps sets it; part B's
    /// panel reads it back.</summary>
    [ObservableProperty]
    public partial string? LastOpenedMap { get; set; }

    public bool IsNotBusy => !IsBusy;

    /// <summary>IsNotBusy's sibling for anything that writes into the game folder: a write also needs the folder
    /// to be there (spec 7.8).</summary>
    public bool CanWrite => !IsBusy && !GameFolderMissing;

    /// <summary>3.0: whether the line under the top bar offering the first capture is up. A library with no pack
    /// at all is a first run whichever page is open, and the capture it offers is the only thing that can end it;
    /// with no game folder there is nothing to capture, so the line stays away. Null before the first scan: the
    /// window would otherwise open saying the library is empty before anything has looked at it.</summary>
    public bool ShowFirstRunBanner => !GameFolderMissing && Snapshot is { Packs.Count: 0 };

    private bool CanAct() => !IsBusy;

    /// <summary>Whether the configured game folder is there right now. The only test for GameFolderMissing, so a
    /// folder that came back clears the notice by itself on the next scan.</summary>
    private bool GameFolderExists() => Directory.Exists(Services.GamePath);

    [RelayCommand]
    private void NavigateMaps() => CurrentPage = Maps;

    [RelayCommand]
    private void NavigateBackgrounds() => CurrentPage = Backgrounds;

    [RelayCommand]
    private void NavigatePlatforms() => CurrentPage = Platforms;

    [RelayCommand]
    public void NavigatePacks() => CurrentPage = Packs;

    [RelayCommand]
    private void NavigateSettings() => CurrentPage = SettingsPage;

    /// <summary>Spec 7.3: the dismiss x remembers the tag, so this release never asks again and the next one
    /// does. Saving is best effort: a settings file that cannot be written is not worth a dialog here.</summary>
    [RelayCommand]
    private void DismissUpdate()
    {
        if (AvailableUpdate is not { } release)
        {
            return;
        }

        try
        {
            Services.UpdateSettings(Services.Settings with { DismissedUpdate = release.TagName });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine($"BhMaps: the dismissed update was not saved: {ex.Message}");
        }

        OnPropertyChanged(nameof(ShowUpdateLine));
    }

    /// <summary>The line itself is the way to the Settings row that offers the download. Nothing downloads here.</summary>
    [RelayCommand]
    private void OpenUpdate() => CurrentPage = SettingsPage;

    /// <summary>Opens the pack detail page on one pack (spec 7.5).</summary>
    public void NavigateToPack(Pack pack)
    {
        PackDetail.Pack = pack;
        CurrentPage = PackDetail;
    }

    /// <summary>Spec 2.6 4.3: the one tile Ctrl+C or Ctrl+X remembered, held by the shell so it survives moving
    /// between pack pages. Never the Windows clipboard: this carries a pack, a tile and how it was taken.</summary>
    public PackClipboardItem? PackClipboard { get; set; }

    /// <summary>F5's read-only rescan. The keyboard is the only thing that asks for it, so the command carries
    /// the key in its name and leaves "Refresh" to the top bar's game write.</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task RescanFromKeyAsync() => RescanAsync();

    /// <summary>What the strip's Cancel link and Escape run; nothing binds it as a command any more (3.0).</summary>
    private void Cancel() => _cts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void LaunchGame() => GameProcess.Launch();

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ImportAsync()
    {
        if (Snapshot is null)
        {
            return;
        }

        var vm = new ImportViewModel(Snapshot.Tree, Snapshot.Packs.Select(p => p.Name).ToList(), Dialogs);
        var window = new ImportWindow { DataContext = vm, Owner = Application.Current.MainWindow };
        if (window.ShowDialog() != true)
        {
            return;
        }

        await RunImportAsync(vm.Jobs);
    }

    /// <summary>Spec 2.8: makes the game match what the app says is on. Every file the app wrote whose source has
    /// been edited since goes in again, every file a map folder has lost comes back from the pack the rest of the
    /// folder matches, and the map-select thumbnails are written again. A file someone else wrote over since is
    /// left alone and counted in the done line: the refresh only ever puts back the app's own writes. It is a game
    /// write like any other, so it goes through the wrapper with its undo snapshot.</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task RefreshGameAsync()
    {
        if (Snapshot is not { } snapshot || GameFolderMissing)
        {
            return;
        }

        var gamePath = Services.GamePath;
        var libraryPath = Services.LibraryPath;
        var hashes = Services.HashCache;
        var recordPath = AppliedRecord.PathFor(Services.AppDataDir);

        string? HashOf(string fullPath)
        {
            var file = new FileInfo(fullPath);
            return file.Exists ? hashes.GetOrCompute(fullPath, file.Length, file.LastWriteTimeUtc.Ticks) : null;
        }

        // Hashing a source for every recorded file is file reading, so it waits off the UI thread like a write.
        var plan = await Task.Run(() => RefreshPlanner.Plan(
            gamePath,
            libraryPath,
            snapshot.Tree,
            snapshot.Packs,
            snapshot.DefaultPack,
            snapshot.MapStatuses,
            AppliedRecord.Load(recordPath),
            HashOf));

        // Art the game did not ship is what a map-select thumbnail is for, whether a pack put it there or the
        // user did, so a refresh writes the thumbnail of every map showing either.
        var artMaps = snapshot.Catalog.Maps
            .Where(map => snapshot.MapStatuses.TryGetValue(map.FolderName, out var status)
                && status.State is MapState.Packs or MapState.Custom)
            .ToList();

        if (plan.Copies.Count == 0 && ThumbnailPlans(artMaps).Count == 0)
        {
            // Nothing to write, so nothing takes an undo snapshot: a refresh that does nothing must not throw the
            // last write's undo away.
            Status.Note("Nothing to refresh.");
            return;
        }

        var copies = plan.Copies;
        var catalog = snapshot.Catalog;
        var failures = new List<FileFailure>();
        var touched = MapsTouched(catalog, copies);
        var mapsTouched = copies.Count > 0 ? touched.Count : artMaps.Count;
        var doneText = $"Refreshed {Count(mapsTouched, "map")}";
        if (plan.Skipped.Count > 0)
        {
            doneText += $". {Count(plan.Skipped.Count, "file")} changed by hand, left alone";
        }

        await RunGameWriteAsync(
            "Refreshing",
            [.. copies.Select(c => c.GameRelativePath)],
            (progress, ct) => Task.Run(
                () =>
                {
                    foreach (var copy in copies)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress.Report(RefreshedName(catalog, copy.GameRelativePath));
                        failures.AddRange(RefreshOne(copy, gamePath, ct));
                    }
                },
                ct),
            doneText,
            // The copies can land in the shared backgrounds folder as well as a map's own, so the folders are the
            // maps the copies touch rather than the top folders of the undo paths (spec 11).
            writtenFolders: touched,
            artMaps: artMaps,
            sources: [.. copies.Select(c => new AppliedSource(c.GameRelativePath, c.SourceFullPath, c.PackName))]);

        Dialogs.ShowFailures("Some files could not be refreshed", failures);
    }

    /// <summary>One file of a refresh. A picture goes back through the fitter that sized it for the slot the
    /// first time, never in raw: the slot is the file's own name, which is what the applier joined to the
    /// backgrounds folder to build the path. Everything else is the raw copy a pack write makes.</summary>
    private static IReadOnlyList<FileFailure> RefreshOne(RefreshCopy copy, string gamePath, CancellationToken ct)
    {
        if (copy.Fit)
        {
            return BackgroundApplier
                .Apply(copy.SourceFullPath, gamePath, [Path.GetFileName(copy.GameRelativePath)], null, ct)
                .Failures;
        }

        var target = Path.Combine(gamePath, copy.GameRelativePath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(copy.SourceFullPath, target, overwrite: true);
            return [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [new FileFailure(target, ex.Message)];
        }
    }

    /// <summary>What a refreshed file is called in the progress line: the map whose folder it lands in, or the
    /// file's own name for a background, which belongs to no one map.</summary>
    private static string RefreshedName(MapCatalog catalog, string gameRelativePath) =>
        catalog.ByFolder(AssetPath.FolderOf(gameRelativePath))?.DisplayName ?? Path.GetFileName(gameRelativePath);

    /// <summary>Spec 6.4 and 8: puts back the files the last game write was about to overwrite or delete, through
    /// the same wrapper as every other write. It is a game write, so it is off while the folder is missing, and
    /// it takes no snapshot of its own: the one it is restoring is the only one there is.</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task UndoAsync()
    {
        if (Services.Undo.Latest is not { } session)
        {
            return;
        }

        var gamePath = Services.GamePath;
        var libraryPath = Services.LibraryPath;
        ApplyResult? result = null;
        var ok = await RunWriteCoreAsync(
            "Undoing",
            undoPaths: null,
            (_, ct) => Task.Run(
                () =>
                {
                    result = Services.Undo.Restore(
                        session,
                        gamePath,
                        libraryPath,
                        ThumbnailWriter.ThumbnailsDir(Services.GameRoot),
                        AppliedRecord.PathFor(Services.AppDataDir));
                },
                ct),
            UndoDoneText,
            undoable: false);

        if (ok)
        {
            // Over the done line the wrapper left: an undo is not a write waiting to be undone, and the strip says
            // so in its own words.
            Status.Undone(DoneLine(
                UndoneLine(
                    result,
                    session,
                    Snapshot is { } snapshot ? MapFolders.Of(snapshot.Catalog.Maps) : null),
                GameRunning));
        }

        if (result is not null)
        {
            Dialogs.ShowFailures("Some files could not be restored", result.Failures);
        }
    }

    /// <summary>Spec 7.1: the window collects pictures, a pack and one of four outcomes. The import and any apply
    /// belong here, because both belong to the busy boundary.</summary>
    public async Task OpenAddPicturesAsync(AddPicturesTarget target)
    {
        if (Snapshot is not { } snapshot)
        {
            return;
        }

        var mapName = target.Map?.DisplayName;
        var vm = new AddPicturesViewModel(
            Dialogs,
            snapshot.Packs.Select(p => p.Name).ToList(),
            target.Kind,
            mapName,
            snapshot.Catalog.Maps.Count);
        var window = new AddPicturesWindow { DataContext = vm, Owner = Application.Current.MainWindow, ShowActivated = !App.Quiet };
        if (window.ShowDialog() != true)
        {
            return;
        }

        var sources = vm.Files.Select(f => f.FullPath).ToList();
        var packName = vm.EffectivePackName;

        // 3.0 C: a picture is called what its file is called, so the names are settled before the copy. One
        // picture is worth asking about; a batch takes the cleaned names rather than a dialog per file.
        var existing = PictureImporter.ExistingNames(Services.LibraryPath, packName);
        var names = PictureNames.ForImport(sources.Select(PictureImporter.TargetFileName), existing);
        if (sources.Count == 1 && NameForOnePicture(sources[0], existing) is { } chosen)
        {
            names = [chosen];
        }

        // A library write: no undo snapshot and no game-running policy, so RunBusyAsync rather than a game write.
        PictureImportResult? result = null;
        var ok = await RunBusyAsync(
            $"Importing into {packName}",
            (progress, ct) => Task.Run(
                () => { result = PictureImporter.Import(sources, Services.LibraryPath, packName, vm.Fit, progress, ct, names); },
                ct));
        if (result is not null)
        {
            Dialogs.ShowFailures("Some pictures could not be imported", result.Failures);
        }

        // Spec 4: a picture just added is one the user wants to see, so the page showing the rows turns its
        // switch on for it. A picture added from anywhere else leaves the switch where it was.
        if (result is { Copied: > 0 } && CurrentPage == Backgrounds)
        {
            Backgrounds.ShowPictures = true;
        }

        var maps = ThenMaps(vm.Then, target, snapshot);
        if (ok && maps.Count > 0 && result is { Written.Count: > 0 })
        {
            // The apply rescans on its way out, and its done line is the one that ends up in the header.
            await ApplyPicturesAsync(maps, packName, result.Written);
            return;
        }

        if (ok)
        {
            Status.Done(
                result?.Copied == 1
                    ? $"Imported 1 picture into {packName}."
                    : $"Imported {result?.Copied ?? 0} pictures into {packName}.",
                undoable: false);
        }

        await RescanAsync();
    }

    /// <summary>Asks what to call the one picture being imported, offered the cleaned name to start from. Null
    /// when the prompt was cancelled or left empty, which takes the cleaned name: the question is an offer to
    /// name the picture, not a thing the import waits on.</summary>
    private string? NameForOnePicture(string source, IReadOnlyList<string> existing)
    {
        var cleaned = PictureNames.Clean(PictureImporter.TargetFileName(source));
        var typed = Dialogs.PromptText(
            "Name this picture",
            "Shown on its tile and on the maps it goes on.",
            Path.GetFileNameWithoutExtension(cleaned));

        return string.IsNullOrWhiteSpace(typed)
            ? null
            : PictureNames.Unique(PictureNames.FileName(typed, Path.GetExtension(cleaned)), existing);
    }

    /// <summary>The maps the chosen radio names. A map with no background slots is left out: without level data
    /// there is nothing to write into (spec 3.6).</summary>
    private IReadOnlyList<MapEntry> ThenMaps(AddPicturesThen then, AddPicturesTarget target, ScanSnapshot snapshot)
    {
        IEnumerable<MapEntry> maps = then switch
        {
            AddPicturesThen.Map => target.Map is null ? [] : [target.Map],
            AddPicturesThen.All => snapshot.Catalog.Maps,
            _ => [],
        };

        return maps.Where(m => m.BackgroundSlots.Count > 0).ToList();
    }

    /// <summary>Spec 6.8's optional half, as one game write: the imported pictures go to the maps in order,
    /// starting again from the first picture when there are more maps than pictures, and a picture past the last
    /// map is imported only.</summary>
    private async Task ApplyPicturesAsync(
        IReadOnlyList<MapEntry> maps, string packName, IReadOnlyList<string> written)
    {
        // Spec 6.3: the maps are named before more than one of them is written. The import has already run, so
        // declining still rescans: the library has changed even though nothing was applied.
        if (maps.Count > 1
            && !Dialogs.Confirm(
                "Apply pictures",
                ConfirmBody(
                    $"Apply these pictures to {Count(maps.Count, "map")}?",
                    WritesBackgrounds,
                    [.. maps.Select(m => m.DisplayName)]),
                $"Apply to {Count(maps.Count, "map")}"))
        {
            await RescanAsync();
            return;
        }

        var backgrounds = Path.Combine(
            PackScanner.PacksRoot(Services.LibraryPath), packName, PictureImporter.BackgroundsFolder);
        var pictures = written.Select(name => Path.Combine(backgrounds, name)).ToList();

        var gamePath = Services.GamePath;
        var undoPaths = maps
            .SelectMany(m => BackgroundApplier.TargetPaths(m.BackgroundSlots))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // One source per slot the write lands on: the picture that map's turn uses, cycling as the loop below does.
        IReadOnlyList<AppliedSource> sources = pictures.Count == 0
            ? []
            : [.. maps.SelectMany(
                (m, i) => BackgroundApplier.TargetPaths(m.BackgroundSlots)
                    .Select(p => new AppliedSource(p, pictures[i % pictures.Count], packName)))];

        var used = Math.Min(pictures.Count, maps.Count);
        var failures = new List<FileFailure>();
        await RunGameWriteAsync(
            "Applying pictures",
            undoPaths,
            (progress, ct) => Task.Run(
                () =>
                {
                    for (var i = 0; i < maps.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress.Report(maps[i].DisplayName);
                        var result = BackgroundApplier.Apply(pictures[i % pictures.Count], gamePath, maps[i].BackgroundSlots, null, ct);
                        failures.AddRange(result.Failures);
                    }
                },
                ct),
            $"{Count(used, "picture")} applied to {Count(maps.Count, "map")}.",
            packName,
            // The undo paths are all in the shared backgrounds folder, so the maps whose slots were written are
            // named here rather than read back off them (spec 11).
            writtenFolders: [.. maps.Select(m => m.FolderName)],
            artMaps: maps,
            sources: sources);

        Dialogs.ShowFailures("Some pictures could not be applied", failures);
    }

    /// <summary>Spec 3.2 and 4: one picture into the background slots of every map given, as one game write.
    /// More than one map confirms with the count first (spec 3.3).
    /// displayName is the name the user chose the picture by, which is the pack on a panel tile and the picture's
    /// own name in the custom library: spec 2.2 words the done line "flowermap applied to Brawlhaven", never the
    /// file name inside the pack, which the user never picked. Null for a caller with no such name, and then the
    /// file name is the best there is. Whichever it is, the confirm, the busy title and the done line all use the
    /// one name, so the user reads the same word from the first prompt to the last line.</summary>
    public async Task ApplyPictureAsync(
        string sourcePath,
        IReadOnlyList<MapEntry> maps,
        string? displayName = null,
        string? packName = null)
    {
        // Without level data a map has no background slots at all, so there is nowhere to write (spec 3.6).
        var targets = maps.Where(m => m.BackgroundSlots.Count > 0).ToList();
        var name = displayName ?? Path.GetFileName(sourcePath);
        if (targets.Count == 0)
        {
            Dialogs.Info(
                "Nothing to apply",
                "These maps have no background slots. Slots come from the game's own level data.");
            return;
        }

        if (targets.Count > 1
            && !Dialogs.Confirm(
                "Apply background",
                ConfirmBody(
                    $"Apply {name} to {Count(targets.Count, "map")}?",
                    WritesBackgrounds,
                    [.. targets.Select(m => m.DisplayName)]),
                $"Apply to {Count(targets.Count, "map")}"))
        {
            return;
        }

        var gamePath = Services.GamePath;
        var undoPaths = targets
            .SelectMany(m => BackgroundApplier.TargetPaths(m.BackgroundSlots))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var failures = new List<FileFailure>();
        await RunGameWriteAsync(
            $"Applying {name}",
            undoPaths,
            (progress, ct) => Task.Run(
                () =>
                {
                    // Counted, not just named: a line that says how far along it is tells the user whether to
                    // wait (3.0), and the label the boundary puts in front of it makes the whole line.
                    for (var i = 0; i < targets.Count; i++)
                    {
                        var map = targets[i];
                        ct.ThrowIfCancellationRequested();
                        progress.Report($"{map.DisplayName}, {i + 1} of {targets.Count}");
                        failures.AddRange(
                            BackgroundApplier.Apply(sourcePath, gamePath, map.BackgroundSlots, null, ct).Failures);
                    }
                },
                ct),
            targets.Count == 1
                ? $"{name} applied to {targets[0].DisplayName}"
                : $"{name} applied to {targets.Count} maps",
            packName,
            // The undo paths are all in the shared backgrounds folder, so the maps whose slots were written are
            // named here rather than read back off them (spec 11).
            writtenFolders: [.. targets.Select(m => m.FolderName)],
            artMaps: targets,
            sources: [.. undoPaths.Select(p => new AppliedSource(p, sourcePath, packName))]);

        Dialogs.ShowFailures("Some backgrounds could not be applied", failures);
    }

    /// <summary>Spec 3.2: one pack's platform art onto every map given, each map getting its own set from the
    /// same pack. A map the pack has nothing for is left out rather than cleared.</summary>
    public async Task ApplySetAsync(Pack pack, IReadOnlyList<MapEntry> maps)
    {
        var targets = maps.Where(m => pack.FindFolder(m.FolderName) is { Files.Count: > 0 }).ToList();
        if (targets.Count == 0)
        {
            Dialogs.Info("Nothing to apply", $"{pack.Name} has no platform art for these maps.");
            return;
        }

        if (targets.Count > 1
            && !Dialogs.Confirm(
                "Apply platform set",
                ConfirmBody(
                    $"Apply {pack.Name} to {Count(targets.Count, "map")}?",
                    WritesPlatforms,
                    [.. targets.Select(m => m.DisplayName)]),
                $"Apply to {Count(targets.Count, "map")}"))
        {
            return;
        }

        var gamePath = Services.GamePath;
        var undoPaths = targets
            .SelectMany(m => PlatformSetApplier.TargetPaths(pack, m.FolderName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var failures = new List<FileFailure>();
        await RunGameWriteAsync(
            $"Applying {pack.Name}",
            undoPaths,
            (progress, ct) => Task.Run(
                () =>
                {
                    // Counted, not just named: a line that says how far along it is tells the user whether to
                    // wait (3.0), and the label the boundary puts in front of it makes the whole line.
                    for (var i = 0; i < targets.Count; i++)
                    {
                        var map = targets[i];
                        ct.ThrowIfCancellationRequested();
                        progress.Report($"{map.DisplayName}, {i + 1} of {targets.Count}");
                        failures.AddRange(PlatformSetApplier.Apply(pack, map.FolderName, gamePath, null, ct).Failures);
                    }
                },
                ct),
            targets.Count == 1
                ? $"{pack.Name} applied to {targets[0].DisplayName}"
                : $"{pack.Name} applied to {targets.Count} maps",
            pack.Name,
            artMaps: targets,
            sources: AppliedSources.FromPack(pack, undoPaths));

        Dialogs.ShowFailures("Some files could not be applied", failures);
    }

    /// <summary>"1 map" or "3 maps": the done lines count things and every one of them can be one.</summary>
    public static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    /// <summary>What an apply writes, for the confirm that asks about it (3.0's verb table). One sentence per
    /// kind of write, so two pages cannot word the same effect differently.</summary>
    public const string WritesArt = "Their backgrounds and platforms are written into the game.";

    public const string WritesBackgrounds = "Their backgrounds are written into the game.";

    public const string WritesPlatforms = "Their platforms are written into the game.";

    /// <summary>Reset's own effect sentence: the files go back rather than in.</summary>
    public const string RestoresArt = "Their backgrounds and platforms go back to the game's own art.";

    /// <summary>The maps a confirm lists under its question: eight names at most, then a count, because a confirm
    /// is read, not scanned.</summary>
    public static string NameList(IReadOnlyList<string> names) =>
        names.Count <= 8
            ? string.Join(", ", names)
            : $"{string.Join(", ", names.Take(8))}, and {Count(names.Count - 8, "other map")}";

    /// <summary>The body of a write confirm (3.0): the question with the count in it, what the write touches,
    /// that Undo puts it back, then the maps themselves.</summary>
    /// <summary>A confirm's verb with the thing it acts on named after it, when that name is short enough to
    /// sit on a button. A longer one takes the bare verb: the title already says which one it is, and a button
    /// that wraps is not a button.</summary>
    public static string Verb(string verb, string name) => name.Length <= 20 ? $"{verb} {name}" : verb;

    public static string ConfirmBody(string question, string effect, IReadOnlyList<string> names) =>
        $"{question} {effect} Undo puts them back.\n\n{NameList(names)}";

    /// <summary>Spec 7.2: the editor fits a picture and saves it into a pack, which is a library write of its own.
    /// The "Apply to game" it offers is a game write, so the pack file it left is copied into the slot here,
    /// with the boundary, the snapshot and the undo every other write gets.</summary>
    public async Task OpenBackgroundEditorAsync(BackgroundEditorRequest request)
    {
        if (Snapshot is not { } snapshot)
        {
            return;
        }

        var vm = new BackgroundEditorViewModel(
            Services,
            Dialogs,
            MapSlotChoices(snapshot),
            snapshot.Packs.Select(p => p.Name).ToList(),
            request with { SourcePack = BackgroundSourcePack(request, snapshot) });
        var window = new BackgroundEditorWindow { DataContext = vm, Owner = Application.Current.MainWindow, ShowActivated = !App.Quiet };
        if (window.ShowDialog() != true)
        {
            return;
        }

        if (vm.Saved is not { ApplyToGame: true } saved)
        {
            // Saved into the pack and no further, so nothing in the game folder moved and there is nothing to undo.
            await RescanAsync();
            return;
        }

        if (saved.AllMaps)
        {
            // An any-map picture goes on every map, which is the apply a tile's "Apply to all maps" already runs,
            // count confirm and all (spec 5).
            await ApplyPictureAsync(
                saved.PackFile,
                snapshot.Catalog.Maps,
                Path.GetFileNameWithoutExtension(saved.PackFile),
                saved.PackName);
            return;
        }

        var gamePath = Services.GamePath;
        var source = saved.PackFile;
        var slot = saved.Slot;
        var slotMaps = snapshot.Catalog.Maps
            .Where(m => m.BackgroundSlots.Contains(slot, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var failures = new List<FileFailure>();
        var editorTargets = BackgroundApplier.TargetPaths([slot]);
        await RunGameWriteAsync(
            $"Applying {Path.GetFileName(source)}",
            editorTargets,
            (_, ct) => Task.Run(
                () => failures.AddRange(BackgroundApplier.Apply(source, gamePath, [slot], null, ct).Failures),
                ct),
            $"{Path.GetFileName(source)} applied to {saved.MapNames}",
            packName: saved.PackName,
            // One slot, and the maps that name it are the cards it changes: the backgrounds folder it is written
            // into belongs to no map of its own (spec 11).
            writtenFolders: [.. slotMaps.Select(m => m.FolderName)],
            artMaps: slotMaps,
            sources: [.. editorTargets.Select(p => new AppliedSource(p, source, saved.PackName))]);

        Dialogs.ShowFailures("Some backgrounds could not be applied", failures);
    }

    /// <summary>Spec 8: the pack whose background record the editor opens on. The tile's own pack when it named
    /// one, so Edit on a pack picture shows that pack's values; otherwise the first pack the slot's file status
    /// names that remembers the slot. Null when the request names no slot, because a custom picture belongs to no
    /// slot and there is nothing to look up.</summary>
    private static Pack? BackgroundSourcePack(BackgroundEditorRequest request, ScanSnapshot snapshot)
    {
        if (request.Slot is not { Length: > 0 } slot)
        {
            return null;
        }

        if (snapshot.Packs.FirstOrDefault(
            p => p.Name.Equals(request.PackName, StringComparison.OrdinalIgnoreCase)) is { } named)
        {
            return named;
        }

        var map = snapshot.Catalog.Maps.FirstOrDefault(
            m => m.BackgroundSlots.Any(s => s.Equals(slot, StringComparison.OrdinalIgnoreCase)));
        var status = map is null ? null : snapshot.MapStatuses.GetValueOrDefault(map.FolderName);
        return SourcePackFinder.ForBackground(AssetPath.Background(slot), status, snapshot.Packs);
    }

    /// <summary>Spec 4.2: pick one map for a picture. Null when the window was cancelled. Applying is the caller's,
    /// through the path it already uses, so the chooser itself never writes.</summary>
    public Task<MapEntry?> ChooseMapAsync(string picturePath, string caption)
    {
        if (Snapshot is not { } snapshot)
        {
            return Task.FromResult<MapEntry?>(null);
        }

        var rows = new List<ChooserRow>();
        foreach (var map in snapshot.Catalog.Maps.Where(m => m.BackgroundSlots.Count > 0))
        {
            snapshot.MapStatuses.TryGetValue(map.FolderName, out var status);
            rows.Add(new ChooserRow(map.DisplayName, status?.Text ?? "Default", picturePath, map));
        }

        var vm = new ChooserViewModel(
            $"Apply {caption} to a map",
            "One map. Pick it and the picture is written to the game.",
            "map",
            rows)
        { PrimaryPrefix = "Apply to" };
        return Task.FromResult(ShowChooser(vm) ? vm.Selected?.Map : null);
    }

    /// <summary>Spec 4.2: pick one picture for a map. The file, or null when cancelled.</summary>
    public Task<string?> ChoosePictureAsync(MapEntry map)
    {
        if (Snapshot is not { } snapshot)
        {
            return Task.FromResult<string?>(null);
        }

        var rows = new List<ChooserRow>();
        foreach (var pack in snapshot.Packs.OrderByDescending(
                     p => p.Name.Equals(BackgroundEditorViewModel.DefaultPackName, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var file in pack.FindFolder("Backgrounds")?.Files ?? Array.Empty<GameFile>())
            {
                rows.Add(new ChooserRow(
                    Path.GetFileNameWithoutExtension(file.Name), pack.Name, file.FullPath, null));
            }
        }

        var vm = new ChooserViewModel(
            $"Apply a picture to {map.DisplayName}",
            "One picture. Pick it and it is written to the game.",
            "picture",
            rows);
        return Task.FromResult(ShowChooser(vm) ? vm.Selected?.Path : null);
    }

    /// <summary>Spec 2.6 4.3: pick one pack, in the map chooser's window. The pack the tile is already in is
    /// left out, and the last line makes a new one and returns it. Null when the window was cancelled.</summary>
    public async Task<Pack?> ChoosePackAsync(string title, Pack exclude)
    {
        if (Snapshot is not { } snapshot)
        {
            return null;
        }

        var rows = new List<ChooserRow>();
        foreach (var pack in snapshot.Packs.Where(
                     p => !p.Name.Equals(exclude.Name, StringComparison.OrdinalIgnoreCase)))
        {
            var maps = snapshot.Catalog.Maps.Count(m => pack.FindFolder(m.FolderName) is { Files.Count: > 0 });
            var backgrounds = pack.FindFolder(PackCopier.BackgroundsFolder)?.Files.Count ?? 0;
            var lead = (pack.FindFolder(PackCopier.BackgroundsFolder)?.Files ?? Array.Empty<GameFile>())
                .FirstOrDefault(f => Path.GetExtension(f.Name)
                    .Equals(PackCopier.BackgroundExtension, StringComparison.OrdinalIgnoreCase));
            rows.Add(new ChooserRow(
                pack.Name,
                $"{Count(maps, "map")}, {Count(backgrounds, "background")}",
                lead?.FullPath ?? "",
                null));
        }

        rows.Add(new ChooserRow("New pack...", "", "", null, isNewPack: true));

        var vm = new ChooserViewModel(title, "One pack. Nothing is written to the game.", "pack", rows)
        {
            PrimaryPrefix = "Choose",
        };
        if (!ShowChooser(vm) || vm.Selected is not { } chosen)
        {
            return null;
        }

        return chosen.IsNewPack
            ? await NewPackAsync()
            : snapshot.Packs.FirstOrDefault(p => p.Name.Equals(chosen.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Spec 13's new-pack flow, as the Packs page runs it, returning the pack it made so a chooser can
    /// hand it straight back to the operation that asked for one. Null when the name was empty or refused.</summary>
    public async Task<Pack?> NewPackAsync()
    {
        var name = Dialogs.PromptText("New pack", "Name", "");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (!PackCreator.TryCreate(Services.LibraryPath, name.Trim(), out var error))
        {
            Dialogs.Error("Could not make the pack", error);
            return null;
        }

        await RescanAsync();
        return FindPack(name.Trim());
    }

    /// <summary>The chooser's window, opened the way every other owned window here is. The thumbnails load while it
    /// is up and are cancelled when it closes.</summary>
    private bool ShowChooser(ChooserViewModel vm)
    {
        var window = new ChooserWindow
        {
            DataContext = vm,
            Owner = Application.Current.MainWindow,
            ShowActivated = !App.Quiet,
        };
        using var cts = new CancellationTokenSource();
        _ = vm.LoadThumbnailsAsync(Services, cts.Token);
        var ok = window.ShowDialog() == true;
        cts.Cancel();
        return ok;
    }

    /// <summary>Spec 2.6 section 5: one pack's maps and pictures into another, off the UI thread, inside a
    /// library undo session holding every path the plan will write or replace.</summary>
    public async Task ImportFromPackAsync(Pack target)
    {
        if (Snapshot is not { } snapshot)
        {
            return;
        }

        var sources = snapshot.Packs
            .Where(p => !p.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (sources.Count == 0)
        {
            return;
        }

        var vm = new ImportFromPackViewModel(this, target, sources);
        var window = new ImportFromPackWindow
        {
            DataContext = vm,
            Owner = Application.Current.MainWindow,
            ShowActivated = !App.Quiet,
        };
        bool accepted;
        try
        {
            accepted = window.ShowDialog() == true;
        }
        finally
        {
            // The thumbnails the window started decoding stop with it.
            vm.Cleanup();
        }

        if (!accepted || vm.BuildPlan() is not { } plan)
        {
            return;
        }

        var catalog = snapshot.Catalog;
        // Replace removes what the target holds for a map before the source's files land, and that is not always
        // the same set, so the target's own files are captured as well. A loose picture has the same pack-relative
        // path in both packs, and capturing a path the target does not have costs nothing.
        var files = plan.Maps
            .SelectMany(m => PackCopier.MapFiles(plan.Source, m, catalog)
                .Concat(PackCopier.MapFiles(plan.Target, m, catalog)))
            .Concat(plan.LooseFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        PackImportResult? result = null;
        var touched = PackCopier.Touched(target, files);
        var ok = await RunLibraryWriteAsync(
            $"Importing into {target.Name}",
            touched,
            (progress, ct) => Task.Run(() => { result = PackCopier.Import(plan, catalog, progress, ct); }, ct),
            $"Importing into {target.Name}");
        if (!ok)
        {
            return;
        }

        // The line names what the import did, which is only known once the work is over, so it is written over
        // the placeholder the write boundary left rather than handed to it.
        Status.Done(Sentences.First(ImportDone(result, plan, target)), touched.Count > 0 && Services.Undo.Latest is not null);
        if (result is not null)
        {
            Dialogs.ShowFailures("Some files could not be imported", result.Failures);
        }
    }

    /// <summary>Spec 5.2's done line: the count, or the count with the first failure named.</summary>
    private static string ImportDone(PackImportResult? result, PackImportPlan plan, Pack target)
    {
        var wanted = plan.Maps.Count + plan.LooseFiles.Count;
        var skipped = result?.Skipped.Count ?? 0;
        var done = wanted - skipped;
        return result?.Failures.Count > 0
            ? $"{done} of {wanted} imported. {Path.GetFileName(result.Failures[0].Path)}: {result.Failures[0].Error}"
            : $"{Count(done, "map")} imported into {target.Name}";
    }

    /// <summary>Spec 6: the editor recolours a map's own pieces into a pack, which is a library write of its own.
    /// The "Apply to game" it offers is a game write, so the set it left is applied here, with the boundary,
    /// the snapshot and the undo every other write gets. The rescan comes first either way, because the pack the
    /// apply needs is one the last scan may never have seen. <paramref name="onlyFile" /> is the one file the
    /// editor opens ticked, for the panel row that asked for it (ruling 7). Spec 9: the editor opens on a set of
    /// maps, which is one map for every way in, and one Save writes all of them.</summary>
    public async Task OpenPlatformEditorAsync(IReadOnlyList<MapEntry> maps, Pack? pack, string? onlyFile = null)
    {
        if (Snapshot is not { } snapshot || maps.Count == 0)
        {
            return;
        }

        // Spec 5.1: opened without a pack, the editor still takes its values from the pack each map's files came
        // from, when that pack remembers that map, which is why it is given the statuses and the packs.
        var vm = new PlatformEditorViewModel(
            Services,
            Dialogs,
            snapshot.Packs,
            snapshot.MapStatuses,
            new PlatformEditorRequest(maps, pack, onlyFile));
        var window = new PlatformEditorWindow { DataContext = vm, Owner = Application.Current.MainWindow, ShowActivated = !App.Quiet };
        bool accepted;
        try
        {
            accepted = window.ShowDialog() == true;
        }
        finally
        {
            vm.Cleanup();
        }

        if (!accepted)
        {
            // Cancel drops what the sliders were showing, but a file handed to another program was written into
            // the pack when it was handed over and is the pack's now, so the line says where it is (ruling 8).
            if (vm.WorkingCopies.Count > 0)
            {
                await RescanAsync();
                Status.Done(WorkingCopyDone(vm.WorkingCopies), undoable: false);
            }

            return;
        }

        if (vm.Saved is not { } saved)
        {
            return;
        }

        await RescanAsync([.. saved.Maps.Select(m => m.FolderName)]);
        if (!saved.ApplyToGame)
        {
            // Saved into the pack and no further, so nothing in the game folder moved and there is nothing to undo.
            if (vm.SeamFixSkipped)
            {
                Status.Note(PlatformPieceViewModel.SeamFixNote);
            }

            return;
        }

        if (Snapshot?.Packs.FirstOrDefault(p => p.Name.Equals(saved.PackName, StringComparison.OrdinalIgnoreCase))
            is { } target)
        {
            await ApplySetAsync(target, saved.Maps);
        }
    }

    /// <summary>What the editor's Cancel leaves behind: the files it wrote into the library for another program
    /// to edit, named when there is one and counted when there are more, in the pack they share or in the
    /// library when they do not share one.</summary>
    private static string WorkingCopyDone(IReadOnlyList<(string Path, string PackName)> copies)
    {
        if (copies.Count == 1)
        {
            return $"{Path.GetFileName(copies[0].Path)} stays in {copies[0].PackName}.";
        }

        var packs = copies.Select(c => c.PackName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return packs.Count == 1
            ? $"{Count(copies.Count, "file")} stay in {packs[0]}."
            : $"{Count(copies.Count, "file")} stay in the library.";
    }

    /// <summary>"All maps" first (spec 5), then one entry per background slot the maps name, labelled with every
    /// map that shares it (spec 7.2), then any slot the game folder has that no map names, labelled with its own
    /// file name (decision C-D8).</summary>
    private static IReadOnlyList<MapSlotChoice> MapSlotChoices(ScanSnapshot snapshot)
    {
        var bySlot = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in snapshot.Catalog.Maps)
        {
            foreach (var slot in map.BackgroundSlots)
            {
                if (!bySlot.TryGetValue(slot, out var names))
                {
                    names = [];
                    bySlot[slot] = names;
                }

                if (!names.Contains(map.DisplayName))
                {
                    names.Add(map.DisplayName);
                }
            }
        }

        var choices = bySlot
            .Select(pair => new MapSlotChoice(pair.Key, string.Join(", ", pair.Value)))
            .OrderBy(c => c.DisplayNames, StringComparer.OrdinalIgnoreCase)
            .ToList();
        choices.Insert(0, MapSlotChoice.AllMaps);

        foreach (var file in snapshot.Tree.FindFolder("Backgrounds")?.Files ?? Array.Empty<GameFile>())
        {
            if (!bySlot.ContainsKey(file.Name))
            {
                choices.Add(new MapSlotChoice(file.Name, file.Name));
            }
        }

        return choices;
    }

    /// <summary>Spec 6: all of the plans as one long operation, one failure summary, and one rescan at the end
    /// rather than one per pack. The import window has already asked about any pack these add to, so nothing here
    /// stops to ask again.</summary>
    protected async Task RunImportAsync(IReadOnlyList<ImportJob> jobs)
    {
        if (jobs.Count == 0)
        {
            return;
        }

        var packsRoot = PackScanner.PacksRoot(Services.LibraryPath);
        var failures = new List<FileFailure>();
        var ok = await RunBusyAsync(
            jobs.Count == 1 ? $"Importing into {jobs[0].PackName}" : "Importing",
            (progress, ct) => Task.Run(
                () =>
                {
                    for (var i = 0; i < jobs.Count; i++)
                    {
                        var job = jobs[i];

                        // With several packs the line is which pack and how far through the list it is; the per-file
                        // progress would overwrite that, so it is only forwarded when there is one pack to report.
                        // The line is the whole sentence, which is why the label is not prefixed to it below.
                        if (jobs.Count > 1)
                        {
                            progress.Report($"Importing {job.PackName} ({i + 1} of {jobs.Count})");
                        }

                        try
                        {
                            var result = ImportRouter.Execute(
                                job.Plan,
                                job.PackName,
                                Services.LibraryPath,
                                jobs.Count == 1 ? progress : null,
                                ct);
                            failures.AddRange(result.Failures);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            // One pack that cannot be written is a line in the summary, not the end of the run.
                            failures.Add(new FileFailure(Path.Combine(packsRoot, job.PackName), ex.Message));
                        }
                    }
                },
                ct),
            prefixProgress: jobs.Count == 1);

        Dialogs.ShowFailures("Some files could not be imported", failures);
        if (ok)
        {
            Status.Done(
                jobs.Count == 1 ? $"Imported {jobs[0].PackName}." : $"Imported {Count(jobs.Count, "pack")}.",
                undoable: false);
        }

        await RescanAsync();
    }

    /// <summary>Scans, then refreshes every page, not just the current one, so switching pages never shows stale
    /// data. <paramref name="writtenFolders"/> names the map folders the write that led here touched, so the pages
    /// showing those maps can put them first (spec 11); null when nothing was written or the caller cannot say.</summary>
    public async Task RescanAsync(IReadOnlyList<string>? writtenFolders = null)
    {
        // Before the scan, because a missing folder scans to an empty tree rather than throwing, and after it,
        // because the folder can go or come back while the scan is reading it.
        GameFolderMissing = !GameFolderExists();
        ScanSnapshot? snapshot = null;
        var ok = await RunBusyAsync(
            "Scanning",
            (progress, ct) => Task.Run(() => { snapshot = Services.Scan(progress, ct); }, ct));
        GameFolderMissing = !GameFolderExists();
        if (!ok || snapshot is null)
        {
            return;
        }

        Snapshot = snapshot;
        OnPropertyChanged(nameof(ShowFirstRunBanner));

        // A file may be a different picture now, so the rows pages' decodes are forgotten before they rebuild.
        Services.RowThumbnails.Clear();

        // Maps rebuilds its cards first, because the rows pages build their own rows from its cards.
        foreach (var page in _pages)
        {
            page.Refresh(snapshot, writtenFolders);
        }

        // Spec 7.3: once a run, 5 s after the first scan finished, off the UI thread and blocking nothing. A
        // timer rather than an await, so the scan's caller is not held by it.
        if (!_updateCheckStarted)
        {
            _updateCheckStarted = true;
            _updateTimer.Start();
        }

        // Level data being re-read needs no wiring of its own: a re-read rebuilds the catalog, the next scan
        // sees the new map's folder, and the top-up reads the snapshot in front of it rather than any history.
        await TopUpDefaultAsync(snapshot);
    }

    /// <summary>Adds a new map's own art to the Default pack after a game update. The pack was captured before
    /// the update, so the new map's files read as custom and Reset to default has nothing to put back. Only what
    /// the pack lacks is copied, and only files nothing has applied over, so this can never write over the art
    /// the pack already holds the way a capture would. No confirm: the first scan that sees the folder is the one
    /// moment the app knows those files are the game's own.</summary>
    private async Task TopUpDefaultAsync(ScanSnapshot snapshot)
    {
        // Without a Default pack there is nothing to add to, and a missing game folder has nothing to add from.
        if (snapshot.DefaultPack is not { } defaultPack || GameFolderMissing || IsBusy)
        {
            return;
        }

        var recordPath = AppliedRecord.PathFor(Services.AppDataDir);
        var packed = new HashSet<string>(
            snapshot.MapStatuses.Values
                .SelectMany(s => s.Files)
                .Where(f => f.State == MapFileState.Pack)
                .Select(f => f.RelativePath),
            StringComparer.OrdinalIgnoreCase);

        var tree = snapshot.Tree;
        var catalog = snapshot.Catalog;
        var missing = await Task.Run(() =>
        {
            // Reading the record is file reading, so it waits off the UI thread with the search itself.
            var applied = AppliedRecord.Load(recordPath);

            // A file a pack matches, or one this app wrote, is applied art rather than the game's own. Default
            // keeps lacking it, which is honest: the app has no way to know what the vanilla bytes were.
            bool IsVanilla(string relative) => !packed.Contains(relative) && !applied.Entries.ContainsKey(relative);

            return DefaultPack.FindMissing(tree, catalog, defaultPack, IsVanilla);
        });

        // A file that failed to copy is still missing at the next scan; tried once a run, not every rescan. The
        // set is filled only after the copy ran, so a run that never started (busy) offers the files again.
        var fresh = missing.Where(item => !_defaultTopUpTried.Contains(item.GameRelativePath)).ToList();
        if (fresh.Count == 0 || IsBusy)
        {
            return;
        }

        var gamePath = Services.GamePath;
        var library = Services.LibraryPath;
        AddMissingResult? result = null;

        // A library write: no undo snapshot and no game-running policy, as a capture is.
        var ok = await RunBusyAsync(
            "Adding to Default",
            (progress, ct) => Task.Run(
                () => { result = DefaultPack.AddMissing(gamePath, library, fresh, progress, ct); }, ct));
        if (ok)
        {
            foreach (var item in fresh)
            {
                _defaultTopUpTried.Add(item.GameRelativePath);
            }
        }
        if (result is not null)
        {
            Dialogs.ShowFailures("Some files could not be added to Default", result.Failures);
        }

        if (ok && result is { MapsAdded.Count: > 0 })
        {
            Status.Done(
                result.MapsAdded.Count == 1
                    ? $"Brawlhalla updated: {result.MapsAdded[0]} added to Default."
                    : $"Brawlhalla updated: {Count(result.MapsAdded.Count, "map")} added to Default.",
                undoable: false);
        }

        // The map reads as default art from here on. This rescan's own top-up finds nothing: the paths above are
        // in the pack now, and whatever failed is in _defaultTopUpTried, so there is no way round again.
        await RescanAsync();
    }

    /// <summary>Rebuilds the pages from the snapshot already in hand, for a setting that changes what the pages
    /// show rather than what is on disk (2.8's hidden packs). No scan, because nothing on disk moved.
    /// <paramref name="except" /> is the page that made the change and has already put itself right, which is how
    /// the Packs page keeps its scroll position.</summary>
    public void RefreshPages(PageViewModel? except = null)
    {
        // Nothing to rebuild before the first scan, and mid-scan the pages are about to be rebuilt anyway.
        if (Snapshot is not { } snapshot || IsBusy)
        {
            return;
        }

        foreach (var page in _pages)
        {
            if (page != except)
            {
                page.Refresh(snapshot);
            }
        }
    }

    /// <summary>Spec 7.3: the check itself. Off unless the setting is on; at most once a day unless the Settings
    /// page's Check now button forces it. Never throws, never shows a dialog, never downloads, never blocks: the
    /// only thing it can do is set AvailableUpdate and stamp lastUpdateCheck.</summary>
    public async Task CheckForUpdateAsync(bool force)
    {
        // Check now pressed twice, or pressed while the once-a-run check is still out: one check at a time.
        if (UpdateChecking)
        {
            return;
        }

        if (!force && (!Services.Settings.CheckForUpdates || !DueForCheck(Services.Settings.LastUpdateCheck)))
        {
            return;
        }

        UpdateCheckFailed = false;
        UpdateChecking = true;
        try
        {
            var release = await Task.Run(() => Services.Updates.CheckAsync(_updateCts.Token));
            AvailableUpdate = release;
            UpdateCheckFailed = release is null;
            if (release is not null || force)
            {
                try
                {
                    Services.UpdateSettings(Services.Settings with { LastUpdateCheck = DateTimeOffset.UtcNow });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    System.Diagnostics.Trace.WriteLine($"BhMaps: the update stamp was not saved: {ex.Message}");
                }
            }
        }
        finally
        {
            UpdateChecking = false;
            SettingsPage.RefreshUpdateRow();
        }
    }

    /// <summary>Null, or older than a day. A stamp in the future (a clock that moved) counts as due.</summary>
    private static bool DueForCheck(DateTimeOffset? last) =>
        last is not { } then || DateTimeOffset.UtcNow - then >= TimeSpan.FromHours(24) || then > DateTimeOffset.UtcNow;

    /// <summary>Spec 3.5 (3.0): re-reads the game's four data files when they have moved on since the model in
    /// hand, on the strip rather than in a dialog, and rescans so every page picks up the new maps and names.
    /// Does nothing when the files are unchanged, when the game folder is gone, while anything else holds the
    /// busy boundary, or while a read is already running: one read at a time, and never over a write.</summary>
    public async Task RefreshGameDataIfStaleAsync()
    {
        if (IsBusy || _readingGameData || !Services.LevelData.IsStale())
        {
            return;
        }

        _readingGameData = true;
        try
        {
            if (!await RunBusyAsync("Reading game data", (_, ct) => Services.LevelData.RefreshAsync(ct)))
            {
                // The read failed or was cancelled, and the strip says so; the pages still need a scan, because
                // on a first run this is the only one they get.
                await RescanAsync();
                return;
            }
        }
        finally
        {
            _readingGameData = false;
        }

        // After the rescan, not before it: the scan takes the strip over while it runs and puts back whatever
        // line it found there, so a note set first would be the line it puts back rather than the last word.
        await RescanAsync();
        if (Services.LevelData.Available)
        {
            Status.Note(Services.LevelData.ReadNote);
        }
    }

    /// <summary>Spec 3.5: the game data has been read, so the catalog the last scan built from the cache, or from
    /// nothing, is out of date.</summary>
    private void OnLevelDataChanged()
    {
        // A read that lands inside the busy boundary is one of this app's own: the stale check at start and on the
        // game's edges, or Refresh now on Settings. Both rescan and then write their note once the read returns,
        // so a rescan started from here would run beside theirs and its progress would land on top of the note
        // (the strip stuck on "Scanning: hashing files" in the note's colours). Only a read from nowhere else
        // needs the rescan started here.
        if (IsBusy)
        {
            return;
        }

        _ = RescanAsync();
    }

    /// <summary>Stops the two timers, cancels the update work and drops the level-data subscription. Called once,
    /// when the window closes, so none of them keeps waking a dispatcher that is on its way out.</summary>
    public void Shutdown()
    {
        _gameTimer.Stop();
        _updateTimer.Stop();
        _updateCts.Cancel();
        _updateCts.Dispose();
        SettingsPage.Shutdown();
        Services.LevelData.Changed -= OnLevelDataChanged;
    }

    /// <summary>Copies the game folder into the Default pack (spec 6.1), asking before replacing one that already
    /// exists. Shared by the Packs and Settings pages so the confirm text and the busy boundary are the same
    /// from both. A library-only write: no undo snapshot and no game-running policy.</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    public async Task CaptureDefaultsAsync()
    {
        if (GameFolderMissing)
        {
            // Nothing to copy from. The header already says the folder is gone, so this is silent (spec 7.8).
            return;
        }

        var library = Services.LibraryPath;
        if (DefaultPack.Exists(library)
            && !Dialogs.Confirm(
                "Replace the Default pack",
                "A Default pack already exists. Replace it with the game folder as it is now?\n\n"
                + "To be sure the capture is vanilla, verify the game files through Steam first.",
                "Capture"))
        {
            return;
        }

        var gamePath = Services.GamePath;
        ApplyResult? result = null;
        var ok = await RunBusyAsync(
            "Capturing the Default pack",
            (progress, ct) => Task.Run(() => { result = DefaultPack.Capture(gamePath, library, progress, ct); }, ct));
        if (result is not null)
        {
            Dialogs.ShowFailures("Some files could not be captured", result.Failures);
        }

        if (ok)
        {
            Status.Done(
                result is { } captured
                    ? $"Captured the Default pack, {Count(captured.Copied, "file")}."
                    : "Captured the Default pack.",
                undoable: false);
        }

        await RescanAsync();
    }

    public Pack? FindPack(string name) =>
        Snapshot?.Packs.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Runs one long operation with the busy flag, the strip's progress line, and Cancel. False when
    /// cancelled or failed. A progress message is normally a fragment, so it is shown as
    /// "&lt;label&gt;: &lt;message&gt;"; <paramref name="prefixProgress"/> false is for an operation whose messages
    /// already read as the whole line. <paramref name="retry"/> is what the strip's Retry link runs after a
    /// failure; left null the strip offers no Retry, because running the bare work again would skip whatever the
    /// caller does after it (its done line, its rescan) and leave the window saying the wrong thing.</summary>
    public async Task<bool> RunBusyAsync(
        string label,
        Func<IProgress<string>, CancellationToken, Task> work,
        bool prefixProgress = true,
        Func<Task>? retry = null)
    {
        if (IsBusy)
        {
            return false;
        }

        // What the strip was saying before this took it over. An operation that runs inside another one, which is
        // the rescan every write ends with, must not swallow the line the write left.
        var resume = Status.Capture();
        var live = true;
        _cts = new CancellationTokenSource();
        IsBusy = true;
        Status.Running(label);
        var progress = new Progress<string>(
            message =>
            {
                // A report is posted to this thread rather than run on it, so one can land after the operation is
                // over. Late is the same as never here: it must not be written over the outcome.
                if (live)
                {
                    Status.Text = prefixProgress ? $"{label}: {message}" : message;
                }
            });
        try
        {
            await work(progress, _cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            // What a cancel left behind is the caller's to describe: a write that took a snapshot says so over this
            // line, and everything else really has changed nothing.
            Status.Cancelled("Cancelled. Nothing changed.", undoable: false);
            return false;
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException && !GameFolderExists())
        {
            // The game folder went away under the operation. Spec 7.8 calls that a state the top bar reports, not a
            // failure worth a line of its own: every write stays off until a scan finds the folder again.
            GameFolderMissing = true;
            return false;
        }
        catch (Exception ex)
        {
            // The strip carries the failure now, rather than a dialog to dismiss before the window can be read
            // again (3.0). Retry is beside it, because the usual reason is a file that was in use a moment ago.
            Status.Error(FailureLine(label, ex.Message), retry);
            return false;
        }
        finally
        {
            live = false;
            IsBusy = false;
            if (Status.Kind is StatusKind.Running)
            {
                // Nothing wrote over the progress line: either the operation succeeded and its caller says what it
                // did once this has returned, or it was a scan, which says nothing at all.
                Status.Restore(resume);
            }

            _cts.Dispose();
            _cts = null;
        }
    }

    /// <summary>Spec 2.6 4.2: one write that touches the library and not the game. The busy boundary, an undo
    /// session holding the library paths the work is about to write or remove, a done line with Undo beside it,
    /// and a rescan. No launcher and no game side: nothing lands in the game folder, so the game may keep
    /// running and a missing game folder does not stop it.</summary>
    public async Task<bool> RunLibraryWriteAsync(
        string label,
        IReadOnlyList<string> libraryUndoPaths,
        Func<IProgress<string>, CancellationToken, Task> work,
        string doneText,
        IReadOnlyList<string>? writtenFolders = null)
    {
        if (IsBusy)
        {
            // One operation at a time (spec 7.1), refused silently as a game write is. No GameFolderMissing beside
            // it: nothing lands in the game folder. A refused write must not clear the done line or rescan.
            return false;
        }

        var libraryPath = Services.LibraryPath;
        var undoable = libraryUndoPaths.Count > 0;
        var ok = await RunBusyAsync(
            label,
            async (progress, ct) =>
            {
                if (undoable)
                {
                    // The capture is the first step of the work, as it is on the game side: file copying behind a
                    // progress line, inside the boundary that turns an IO failure into the usual dialog.
                    progress.Report("Saving undo");
                    await Task.Run(
                        () =>
                        {
                            var session = Services.Undo.Begin();
                            session.CaptureLibrary(libraryPath, libraryUndoPaths);
                        },
                        ct);
                }

                await work(progress, ct);
            });

        if (ok)
        {
            Status.Done(Sentences.First(doneText), undoable && Services.Undo.Latest is not null);
        }
        else if (Status.Kind is StatusKind.Cancelled && undoable && Services.Undo.Latest is not null)
        {
            // Begin has already replaced the previous snapshot, and the work does not say how far it got, so the
            // line says only what is certain: whatever was written is in the snapshot the capture took.
            Status.Cancelled("Cancelled part way. Undo puts back anything that was written.", undoable: true);
        }

        await RescanAsync(writtenFolders);
        return ok;
    }

    /// <summary>The Delete lines on the tile menus: the library files go, and where the game is showing that art
    /// the affected part of the map goes back to default, both inside one write so one Undo puts everything back.
    /// <paramref name="name" /> is what the menu line was about, for the confirm title and the done line;
    /// <paramref name="packName" /> the pack the words name; <paramref name="libraryUndoPaths" /> the
    /// library-relative files the delete touches; <paramref name="delete" /> the Core call, run on a worker
    /// thread; <paramref name="resets" /> the parts of maps that go back to default with it, empty when the game
    /// is showing none of the deleted art.</summary>
    public async Task DeleteFromLibraryAsync(
        string name,
        string packName,
        IReadOnlyList<string> libraryUndoPaths,
        Func<PackCopyResult> delete,
        IReadOnlyList<PartReset> resets)
    {
        // A reset needs a Default pack to restore from, and the Maps page refuses to reset without one; it also
        // needs the game folder, and a game write is refused outright while that is missing, which would take
        // the library delete down with it. In both cases the delete still goes ahead on its own, so the promise
        // the confirm makes is dropped rather than broken.
        var snapshot = Snapshot;
        var defaultPack = snapshot?.DefaultPack;
        if (defaultPack is null || GameFolderMissing)
        {
            resets = [];
        }

        var who = ResetWho(resets);
        if (!Dialogs.Confirm(
                $"Delete {name}?",
                who is null
                    ? $"It will be removed from {packName}."
                    : $"It will be removed from {packName} and {who.Value.Who} will reset to default.",
                Verb("Remove", name),
                destructive: true))
        {
            return;
        }

        if (defaultPack is null || snapshot is null || who is null)
        {
            PackCopyResult? removed = null;
            await RunLibraryWriteAsync(
                "Deleting",
                libraryUndoPaths,
                (_, ct) => Task.Run(() => { removed = delete(); }, ct),
                $"Deleted {name}.");
            if (removed is not null)
            {
                Dialogs.ShowFailures("Some files could not be deleted", removed.Failures);
            }

            return;
        }

        var gamePath = Services.GamePath;
        var failures = new List<FileFailure>();
        var maps = resets.Select(r => r.Map).ToList();
        var resetPaths = resets
            .SelectMany(r => (r.Platforms
                    ? PackApplier.ResetPlatformPaths(snapshot.Tree, r.Map, defaultPack)
                    : Array.Empty<string>())
                .Concat(PackApplier.ResetBackgroundPaths(defaultPack, r.Slots)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        await RunGameWriteAsync(
            "Deleting",
            resetPaths,
            (progress, ct) => Task.Run(
                () =>
                {
                    // The library files first: the reset that follows is what the game is left showing, so a
                    // delete that failed leaves the pack holding art the map no longer uses rather than the
                    // other way round.
                    ct.ThrowIfCancellationRequested();
                    progress.Report(name);
                    failures.AddRange(delete().Failures);
                    foreach (var reset in resets)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress.Report(reset.Map.DisplayName);
                        if (reset.Platforms)
                        {
                            failures.AddRange(
                                MapReset.ResetPlatforms(gamePath, reset.Map.FolderName, defaultPack).Failures);
                        }

                        if (reset.Slots.Count > 0)
                        {
                            failures.AddRange(
                                MapReset.ResetBackgrounds(gamePath, reset.Slots, defaultPack).Failures);
                        }
                    }
                },
                ct),
            $"Deleted {name}. {who.Value.Who} {(who.Value.Plural ? "are" : "is")} back to default.",
            libraryUndoPaths: libraryUndoPaths,
            artMaps: maps,
            // One flag for every map in the write, so it is only the kept original when every one of them ends
            // fully default; a map that keeps custom art elsewhere is re-rendered with the rest.
            resetThumbnails: resets.All(r => EndsDefault(r, MapStatusOf(snapshot, r.Map))),
            // The reset copies Default's files over these paths; a path Default has nothing for is deleted
            // instead, and the note drops it because the game file is then missing.
            sources: AppliedSources.FromPack(defaultPack, resetPaths));

        Dialogs.ShowFailures("Some files could not be deleted", failures);
    }

    /// <summary>The maps a delete puts back to default, as the confirm and the done line name them, and whether
    /// they take "are" rather than "is". Null when nothing resets. A reset of a map's platforms alone names the
    /// platforms, because the map's pictures are staying.</summary>
    private static (string Who, bool Plural)? ResetWho(IReadOnlyList<PartReset> resets)
    {
        if (resets.Count == 0)
        {
            return null;
        }

        var names = resets
            .Select(r => r.Slots.Count == 0 ? $"{r.Map.DisplayName}'s platforms" : r.Map.DisplayName)
            .ToList();

        // Two names join with "and", three list out, and past that the tail becomes a count: a confirm is read,
        // not scanned, and a line of eleven map names is neither.
        var who = names.Count switch
        {
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            3 => $"{names[0]}, {names[1]} and {names[2]}",
            _ => $"{names[0]}, {names[1]} and {Count(names.Count - 2, "other map")}",
        };

        return (who, names.Count > 1 || resets[0].Slots.Count == 0);
    }

    /// <summary>True when the reset leaves the map showing the game's own art throughout: every file the scan
    /// found that is not default is one this reset puts back. That is the map whose map-select thumbnail should
    /// be the kept original rather than a fresh render.</summary>
    private static bool EndsDefault(PartReset reset, MapStatus? status)
    {
        if (status is null)
        {
            return true;
        }

        var folder = reset.Map.FolderName + Path.DirectorySeparatorChar;
        return status.Files.All(file =>
            file.State == MapFileState.Default
            || (reset.Platforms && file.RelativePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            || reset.Slots.Any(slot => AssetPath.Background(slot)
                .Equals(file.RelativePath, StringComparison.OrdinalIgnoreCase)));
    }

    private static MapStatus? MapStatusOf(ScanSnapshot snapshot, MapEntry map) =>
        snapshot.MapStatuses.TryGetValue(map.FolderName, out var status) ? status : null;

    /// <summary>Spec 2.2: what a write reports about when it will show. One string, appended by the wrapper, so
    /// two pages cannot word it differently. Read at completion, not at the start: a game that was launched while
    /// the write ran gets the sentence that is true when the line appears.</summary>
    public static string DoneSentence(bool gameRunning) =>
        gameRunning ? "Shows on the next match load." : "Shows when Brawlhalla starts.";

    /// <summary>The hash of a game file as the undo session copied it just before the write, or null when the path
    /// held nothing, the session did not capture it, or there was no session at all. It is what tells a write that
    /// landed as different bytes, which a fitted picture does, from one that never ran.</summary>
    private static string? CapturedHash(UndoSession? session, string relativePath)
    {
        if (session is null)
        {
            return null;
        }

        var captured = Path.Combine(session.Path, relativePath);
        try
        {
            return File.Exists(captured) ? FileHasher.Hash(captured) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable copy is one we cannot compare against, so the write counts as landed.
            return null;
        }
    }

    /// <summary>The line an undo leaves. Spec 11 names no string for it, so this is the plan's (A-D7).</summary>
    public const string UndoDoneText = "Undone.";

    /// <summary>The whole done line (3.1): the first sentence of the page's fragment, and the shared sentence
    /// after it only while the game is up. The line lives in the top bar now, where the room is one sentence
    /// long, and the fragment's first sentence is the one that says what happened; a closed game shows the new
    /// art the next time it opens whatever the line says, so that case spends its words on nothing. The wrapper
    /// settles the period between the two for the same reason it settles the sentence: a page cannot get it
    /// wrong if a page does not decide it. An empty fragment leaves the sentence standing alone rather than a
    /// line that opens with a stop.</summary>
    private static string DoneLine(string doneText, bool gameRunning)
    {
        var fragment = Sentences.First(doneText).TrimEnd('.');
        if (fragment.Length == 0)
        {
            return DoneSentence(gameRunning);
        }

        return gameRunning ? $"{fragment}. {DoneSentence(gameRunning)}" : $"{fragment}.";
    }

    /// <summary>What an undo put back. The count is maps rather than files (3.0): the session's game side names
    /// the map folders it captured, and a map is what the owner sees go back. Only the catalog's own map folders
    /// count, so the number matches the one the apply said; with no catalog to hand every folder counts. A restore
    /// that copied nothing back, or that put back nothing outside the library, says only that it is undone.</summary>
    private static string UndoneLine(ApplyResult? result, UndoSession session, MapFolders? mapFolders) => result?.Copied switch
    {
        null or 0 => UndoDoneText,
        _ => session.MapFolderCount(mapFolders) switch
        {
            0 => UndoDoneText,
            1 => "Undone. 1 map is back to what it was.",
            var maps => $"Undone. {maps} maps are back to what they were.",
        },
    };

    /// <summary>What a failure says (3.0): the operation named as a verb, the reason, and the state things are in.
    /// The verbs are the ones the app's own labels open with; a label opening with anything else leaves the line to
    /// the exception's own words, which is all we could honestly say about it.</summary>
    /// <summary>The strip's line for a failure: what could not be done, why, and that nothing changed. Public
    /// because the welcome window's own capture runs before this view model exists and says it the same way.</summary>
    public static string FailureLine(string label, string message)
    {
        var space = label.IndexOf(' ');
        var verb = (space < 0 ? label : label[..space]) switch
        {
            "Adding" => "add",
            "Applying" => "apply",
            "Capturing" => "capture",
            "Deleting" => "delete",
            "Duplicating" => "duplicate",
            "Exporting" => "export",
            "Importing" => "import",
            "Refreshing" => "refresh",
            "Removing" => "remove",
            "Resetting" => "reset",
            "Saving" => "save",
            "Scanning" => "scan",
            "Undoing" => "undo",
            "Writing" => "write",
            _ => null,
        };

        var reason = Sentence(message);
        if (verb is null)
        {
            return reason;
        }

        var what = $"Could not {verb}{(space < 0 ? "" : label[space..])}";
        return reason.Length == 0 ? $"{what}. Nothing changed." : $"{what}: {reason} Nothing changed.";
    }

    /// <summary>A fragment as a sentence: one period at the end, however it was punctuated.</summary>
    private static string Sentence(string text)
    {
        var trimmed = text.TrimEnd('.', ' ');
        return trimmed.Length == 0 ? "" : $"{trimmed}.";
    }

    /// <summary>One write into the game folder (spec 8): the busy boundary, an undo snapshot of the paths it is
    /// about to touch, a done line the wrapper finishes with the period and the shared sentence, and a rescan.
    /// False when the folder was missing, another operation held the boundary, or the write was cancelled or
    /// failed. <paramref name="packName"/> is the pack
    /// the write came out of, stamped as last applied when it succeeds; null for undo, reset and a picture that
    /// was only ever in the game folder. <paramref name="libraryUndoPaths"/> names library files the write also
    /// changes, relative to the library folder, so the undo puts them back with the game files (spec 7).
    /// <paramref name="writtenFolders"/> names the map folders the write lands in, for the rescan it ends with
    /// (spec 11); left null it is read off the undo paths, which is what every write into a map's own folder
    /// wants. <paramref name="artMaps"/> names the maps whose art the write changes, so their map-select
    /// thumbnails are written after it (spec 10.4); <paramref name="resetThumbnails"/>
    /// makes that step put the game's own thumbnail back instead, for a write that resets the art.
    /// <paramref name="sources"/> names the library file behind every game path the write lays down, so the applied
    /// record can remember what the app itself wrote; left null nothing is recorded, which is what a write of
    /// thumbnails alone wants.</summary>
    public Task<bool> RunGameWriteAsync(
        string label,
        IReadOnlyList<string> undoPaths,
        Func<IProgress<string>, CancellationToken, Task> work,
        string doneText,
        string? packName = null,
        IReadOnlyList<string>? libraryUndoPaths = null,
        IReadOnlyList<string>? writtenFolders = null,
        IReadOnlyList<MapEntry>? artMaps = null,
        bool resetThumbnails = false,
        IReadOnlyList<AppliedSource>? sources = null) =>
        RunWriteCoreAsync(
            label,
            undoPaths,
            work,
            doneText,
            undoable: true,
            packName,
            libraryUndoPaths,
            writtenFolders,
            artMaps,
            resetThumbnails,
            sources: sources);

    /// <summary>The one path every game write takes. <paramref name="undoPaths"/> null means take no snapshot,
    /// which is Undo's case and only Undo's: the snapshot it is restoring is the only one there is, and Begin
    /// would replace it with an empty one.</summary>
    private async Task<bool> RunWriteCoreAsync(
        string label,
        IReadOnlyList<string>? undoPaths,
        Func<IProgress<string>, CancellationToken, Task> work,
        string doneText,
        bool undoable,
        string? packName = null,
        IReadOnlyList<string>? libraryUndoPaths = null,
        IReadOnlyList<string>? writtenFolders = null,
        IReadOnlyList<MapEntry>? artMaps = null,
        bool resetThumbnails = false,
        IReadOnlyList<AppliedSource>? sources = null)
    {
        if (GameFolderMissing || IsBusy)
        {
            // Nothing to write into, or one operation at a time (spec 7.1). Refused silently: the top bar already
            // carries the missing-folder notice, and a refused write must not clear the done line or rescan.
            return false;
        }

        var gamePath = Services.GamePath;
        var libraryPath = Services.LibraryPath;
        var thumbnailsDir = ThumbnailWriter.ThumbnailsDir(Services.GameRoot);
        var recordPath = AppliedRecord.PathFor(Services.AppDataDir);
        var thumbnailPlans = ThumbnailPlans(artMaps);
        var thumbnailWrites = 0;
        var ok = await RunBusyAsync(
            label,
            async (progress, ct) =>
            {
                await _launcher.RunWriteAsync(
                    label,
                    async () =>
                    {
                        // The session is held past its capture: the note after the write reads the copies it took
                        // to tell the files the write changed from the ones it left alone.
                        UndoSession? session = null;
                        if (undoPaths is not null)
                        {
                            // The capture is the first step of the work, not a step before it: it is file copying,
                            // so it belongs off the UI thread, behind a progress line, and inside the boundary that
                            // turns an IO failure into the same dialog any other write failure gets.
                            progress.Report("Saving undo");
                            await Task.Run(
                                () =>
                                {
                                    // One session holds both sides: the library records a write clears go back
                                    // with the game files it cleared them for, in the same undo.
                                    session = Services.Undo.Begin();
                                    session.Capture(gamePath, undoPaths);
                                    if (libraryUndoPaths is { Count: > 0 })
                                    {
                                        session.CaptureLibrary(libraryPath, libraryUndoPaths);
                                    }

                                    // The thumbnails the step below is about to write.
                                    session.CaptureThumbnails(
                                        thumbnailsDir,
                                        thumbnailPlans.SelectMany(planned => planned.Plan.Targets.Select(t => t.FileName)));

                                    // The applied record describes the files this session is holding, so it goes
                                    // into the same session and comes back with them.
                                    session.CaptureRecord(recordPath);
                                },
                                ct);
                        }

                        await work(progress, ct);

                        if (sources is { Count: > 0 })
                        {
                            // After the work, because the record is the hash of what really landed: a write that
                            // failed part way must not leave the record claiming files it never wrote.
                            var captured = session;
                            await Task.Run(
                                () => AppliedRecord.Note(
                                    recordPath,
                                    gamePath,
                                    libraryPath,
                                    sources,
                                    DateTimeOffset.Now,
                                    relativePath => CapturedHash(captured, relativePath)),
                                ct);
                        }

                        if (thumbnailPlans.Count > 0)
                        {
                            // After the art it is a picture of, and inside the same boundary: a thumbnail written
                            // from art the write failed to lay down would show something the game never loads.
                            thumbnailWrites = await Task.Run(
                                () => WriteThumbnails(thumbnailPlans, resetThumbnails, gamePath, progress), ct);
                        }
                    });
            },
            retry: () => RunWriteCoreAsync(
                label,
                undoPaths,
                work,
                doneText,
                undoable,
                packName,
                libraryUndoPaths,
                writtenFolders,
                artMaps,
                resetThumbnails,
                sources));

        // A restore that fully succeeds discards its snapshot, so whether there is anything to undo is read back
        // from the store rather than remembered.
        if (ok)
        {
            Status.Done(
                DoneLine(WithThumbnailsDone(doneText, thumbnailWrites), GameRunning),
                undoable && Services.Undo.Latest is not null);
        }
        else if (Status.Kind is StatusKind.Cancelled && undoPaths is not null && Services.Undo.Latest is not null)
        {
            // Begin has already replaced the previous snapshot, and the work does not report how many maps it got
            // through, so the line claims no count: what it can say is that anything already written is in the
            // snapshot the capture took.
            Status.Cancelled("Cancelled part way. Undo puts back anything that was written.", undoable: true);
        }

        if (ok && packName is not null)
        {
            // Spec 8: the stamp is written before the rescan, so the scan this write ends with already sorts the
            // pack that was applied to the top. A cancelled or failed write leaves the old order standing.
            var stamps = new Dictionary<string, DateTimeOffset>(
                Services.Settings.LastApplied, StringComparer.OrdinalIgnoreCase)
            {
                [packName] = DateTimeOffset.Now,
            };
            Services.UpdateSettings(Services.Settings with { PackLastApplied = stamps });
        }

        await RescanAsync(writtenFolders ?? WrittenFolders(undoPaths));
        return ok;
    }

    /// <summary>The note a map's panel shows about its map-select thumbnail, by map folder name, or no entry when
    /// the last write that named the map wrote it.</summary>
    public IReadOnlyDictionary<string, string> ThumbnailNotes => _thumbnailNotes;

    /// <summary>The thumbnail plan of every map a write names, or nothing at all when the write names no maps
    /// or there is no scan to read the other maps' names out of (spec 10.4).</summary>
    private IReadOnlyList<(MapEntry Map, ThumbnailPlan Plan)> ThumbnailPlans(IReadOnlyList<MapEntry>? artMaps) =>
        artMaps is { Count: > 0 } && Snapshot is { } snapshot
            ? [.. artMaps.Select(map =>
                (map, ThumbnailWriter.Plan(map, snapshot.Catalog.Maps, Services.GameRoot, Services.AppDataDir)))]
            : [];

    /// <summary>Spec section 8 and 10.4: the thumbnail step, run after the caller's work succeeded. A map is
    /// rendered once and the same picture is written over every file it owns, so a folder whose levels name two
    /// or three pictures gets all of them. Each map stands on its own, so a keep or a write that fails for one
    /// becomes that map's panel note rather than a failure of a write that is already done. Returns how many
    /// files it wrote. Off the UI thread: the render is the composite the map page builds, and the rest is file
    /// copying.</summary>
    private int WriteThumbnails(
        IReadOnlyList<(MapEntry Map, ThumbnailPlan Plan)> plans,
        bool reset,
        string gamePath,
        IProgress<string> progress)
    {
        var written = 0;
        foreach (var (map, plan) in plans)
        {
            var note = SkipNote(map, plan);
            if (plan.Targets.Count == 0)
            {
                _thumbnailNotes[map.FolderName] = note;
                continue;
            }

            progress.Report("Map-select thumbnail " + map.DisplayName);
            try
            {
                // One render per level of the map, not one for the map: each of its own files is the picture of
                // the level that names it, so a folder holding a big and a small level writes each file from
                // its own level. Levels that share a file share the one render.
                var composites = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
                foreach (var target in plan.Targets)
                {
                    // The game's own picture is kept before the first write over it, so every write can be
                    // undone even after the undo snapshot it was taken with has been replaced. One kept copy
                    // per file, under that file's own name.
                    ThumbnailWriter.KeepOriginal(target);
                    if (reset)
                    {
                        if (ThumbnailWriter.RestoreOriginal(target))
                        {
                            written++;
                        }

                        continue;
                    }

                    if (!composites.TryGetValue(target.Level.LevelName, out var composite))
                    {
                        composite = ThumbnailWriter.Render(target.Level, gamePath);
                        composites[target.Level.LevelName] = composite;
                    }

                    ThumbnailWriter.Write(composite, target.TargetPath);
                    written++;
                }

                if (note.Length == 0)
                {
                    _thumbnailNotes.Remove(map.FolderName);
                }
                else
                {
                    _thumbnailNotes[map.FolderName] = note;
                }
            }
            catch (Exception ex)
            {
                _thumbnailNotes[map.FolderName] = $"Map-select thumbnail not written: {ex.Message}";
            }
        }

        return written;
    }

    /// <summary>The panel note for a map the write could not aim every picture of, or "" when it wrote them all.
    /// A map that owns nothing keeps 2.5's sentence, which the manual quotes; a map that owns some files and not
    /// others names the ones that were left alone.</summary>
    private static string SkipNote(MapEntry map, ThumbnailPlan plan)
    {
        if (plan.NamesNoFile)
        {
            return $"Map-select thumbnail not written: no file is named for {map.DisplayName}.";
        }

        var shared = plan.Files.Where(f => f.Skip == ThumbnailSkip.Shared).ToList();
        var missing = plan.Files.Where(f => f.Skip == ThumbnailSkip.Missing).ToList();
        if (shared.Count == 0 && missing.Count == 0)
        {
            return "";
        }

        if (plan.Targets.Count == 0 && missing.Count == 0)
        {
            return $"Map-select thumbnail not written: {map.DisplayName} shares its picture with {SharedWith(shared)}.";
        }

        var parts = new List<string>();
        if (shared.Count > 0)
        {
            parts.Add($"{Names(shared)} {(shared.Count == 1 ? "is" : "are")} shared with {SharedWith(shared)}.");
        }

        if (missing.Count > 0)
        {
            parts.Add($"{Names(missing)} {(missing.Count == 1 ? "is" : "are")} missing from the game folder.");
        }

        var lead = plan.Targets.Count == 0 ? "Map-select thumbnail not written: " : "Map-select thumbnail: ";
        return lead + string.Join(" ", parts);
    }

    /// <summary>The file names of a group of skipped plan entries, in level order.</summary>
    private static string Names(IReadOnlyList<ThumbnailFilePlan> files) =>
        string.Join(", ", files.Select(f => f.FileName));

    /// <summary>The other maps a shared group's pictures belong to, each named once in the order it turns up and
    /// read as a list. A group whose entries name no other map falls back to "another map", so the sentence still
    /// reads.</summary>
    private static string SharedWith(IReadOnlyList<ThumbnailFilePlan> files)
    {
        var names = files
            .Where(f => !string.IsNullOrWhiteSpace(f.OtherMap))
            .Select(f => f.OtherMap!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return names.Count switch
        {
            0 => "another map",
            1 => names[0],
            _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
        };
    }

    /// <summary>The caller's done fragment with the thumbnail step's own on the end, or the fragment untouched
    /// when the step wrote nothing. The period between them is settled here for the reason DoneLine settles the
    /// last one: the page does not decide it.</summary>
    private static string WithThumbnailsDone(string doneText, int thumbnailWrites)
    {
        if (thumbnailWrites == 0)
        {
            return doneText;
        }

        var updated = thumbnailWrites == 1 ? "Map-select thumbnail updated." : "Map-select thumbnails updated.";
        var fragment = doneText.TrimEnd('.');
        return fragment.Length == 0 ? updated : $"{fragment}. {updated}";
    }

    /// <summary>Spec 11: the map folders a write touched, read off the paths it took its undo snapshot of. The
    /// backgrounds folder is shared by every map rather than owned by one, so it names no map and is left out;
    /// the callers that write only backgrounds pass the folders themselves.</summary>
    /// <summary>The folders of the maps a set of refresh copies touches, for the done line's count and the rescan.
    /// A copy in the shared Backgrounds folder belongs to every map whose slot it fills, not to a folder called
    /// Backgrounds: counting first path segments would call two slot pictures one map, and one map's platform
    /// file plus its slot two.</summary>
    private static IReadOnlyList<string> MapsTouched(MapCatalog catalog, IReadOnlyList<RefreshCopy> copies)
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var copy in copies)
        {
            var top = TopFolder(copy.GameRelativePath);
            if (top.Equals(PictureImporter.BackgroundsFolder, StringComparison.OrdinalIgnoreCase))
            {
                var slot = Path.GetFileName(copy.GameRelativePath);
                foreach (var map in catalog.Maps)
                {
                    if (map.BackgroundSlots.Contains(slot, StringComparer.OrdinalIgnoreCase))
                    {
                        folders.Add(map.FolderName);
                    }
                }
            }
            else if (top.Length > 0)
            {
                folders.Add(top);
            }
        }

        return [.. folders];
    }

    private static IReadOnlyList<string> WrittenFolders(IReadOnlyList<string>? undoPaths) =>
        undoPaths is null
            ? []
            : [.. undoPaths
                .Select(TopFolder)
                .Where(folder => folder.Length > 0
                    && !folder.Equals(PictureImporter.BackgroundsFolder, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The first folder of a relative path, which for a game file is the map's own folder. "" when the
    /// path names no folder at all.</summary>
    private static string TopFolder(string relativePath)
    {
        var folder = AssetPath.FolderOf(relativePath);
        var cut = folder.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        return cut < 0 ? folder : folder[..cut];
    }
}
