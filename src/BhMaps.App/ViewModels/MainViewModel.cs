using System.Windows;
using System.Windows.Threading;
using BhMaps.App.Services;
using BhMaps.App.ViewModels.Pages;
using BhMaps.App.Views;
using BhMaps.Core.Game;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Packs;
using BhMaps.Core.Scanning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>The shell (decision D13): the busy boundary, navigation between the five pages, the scan that feeds
/// them, and the undo of the last game write.</summary>
public partial class MainViewModel : ObservableObject
{
    private static readonly TimeSpan GamePollInterval = TimeSpan.FromSeconds(3);

    private readonly GameLauncher _launcher;
    private readonly IReadOnlyList<PageViewModel> _pages;

    private readonly DispatcherTimer _gameTimer;
    private CancellationTokenSource? _cts;

    /// <summary>Set when the game data lands while a scan is running, so the rescan it needs happens once the
    /// busy boundary clears instead of being dropped.</summary>
    private bool _rescanPending;

    public MainViewModel(AppServices services, IDialogs dialogs)
    {
        Services = services;
        Dialogs = dialogs;
        ProgressText = "";
        DoneText = "";
        _launcher = new GameLauncher();
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
        GameRunning = GameProcess.IsRunning();
        _gameTimer = new DispatcherTimer { Interval = GamePollInterval };
        _gameTimer.Tick += (_, _) => GameRunning = GameProcess.IsRunning();
        _gameTimer.Start();
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

    /// <summary>The ticked maps, in display order (spec 3.3). One list for the whole app: a tile on any page
    /// aims at these. Read from the Maps page's unfiltered cards, not its visible ones, so a search does not
    /// silently shrink what a write is about to touch (plan decision A-D1).</summary>
    public IReadOnlyList<MapEntry> SelectedMaps => Maps.TickedMaps;

    public int SelectedMapCount => SelectedMaps.Count;

    /// <summary>Whether Brawlhalla is running, as of the last poll. The top bar's game line shows it.</summary>
    [ObservableProperty]
    public partial bool GameRunning { get; set; }

    [ObservableProperty]
    public partial PageViewModel? CurrentPage { get; set; }

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
        nameof(RefreshCommand),
        nameof(LaunchGameCommand),
        nameof(ImportCommand),
        nameof(UndoCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>Spec 7.8: the configured game folder is not there. The page header says so and every write into
    /// the game is off until a rescan finds it again. Recomputed around each scan, never guessed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanWrite))]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    public partial bool GameFolderMissing { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; }

    /// <summary>What the last write did, shown in the page header beside Undo. Empty when there is nothing to show.</summary>
    [ObservableProperty]
    public partial string DoneText { get; set; }

    /// <summary>Whether the line in <see cref="DoneText" /> describes a game write, which is the only kind of write
    /// there is a snapshot to put back. False for a library-only line, and the header hides Undo beside it rather
    /// than offering to undo something else.</summary>
    [ObservableProperty]
    public partial bool DoneUndoable { get; set; }

    [ObservableProperty]
    public partial bool CanUndo { get; set; }

    /// <summary>Folder name of the map most recently opened on Maps, or null before any. Maps sets it; part B's
    /// panel reads it back.</summary>
    [ObservableProperty]
    public partial string? LastOpenedMap { get; set; }

    public bool IsNotBusy => !IsBusy;

    /// <summary>IsNotBusy's sibling for anything that writes into the game folder: a write also needs the folder
    /// to be there (spec 7.8).</summary>
    public bool CanWrite => !IsBusy && !GameFolderMissing;

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
    private void NavigatePacks() => CurrentPage = Packs;

    [RelayCommand]
    private void NavigateSettings() => CurrentPage = SettingsPage;

    /// <summary>Opens the pack detail page on one pack (spec 7.5).</summary>
    public void NavigateToPack(Pack pack)
    {
        PackDetail.Pack = pack;
        CurrentPage = PackDetail;
    }

    /// <summary>SelectedMaps is computed, so the pages bound to it are told by hand when it changes. Public:
    /// the Maps page raises it when a card is ticked.</summary>
    public void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedMaps));
        OnPropertyChanged(nameof(SelectedMapCount));
    }

    /// <summary>Unticks every map. The selection bar offers it as "Clear".</summary>
    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var card in Maps.AllCards)
        {
            card.IsSelected = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task RefreshAsync() => RescanAsync();

    [RelayCommand]
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
        await RunWriteCoreAsync(
            "Undoing",
            undoPaths: null,
            (_, ct) => Task.Run(() => { result = Services.Undo.Restore(session, gamePath, libraryPath); }, ct),
            UndoDoneText,
            undoable: false,
            clearTicks: false);

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
            SelectedMapCount,
            snapshot.Catalog.Maps.Count);
        var window = new AddPicturesWindow { DataContext = vm, Owner = Application.Current.MainWindow, ShowActivated = !App.Quiet };
        if (window.ShowDialog() != true)
        {
            return;
        }

        var sources = vm.Files.Select(f => f.FullPath).ToList();
        var packName = vm.EffectivePackName;

        // A library write: no undo snapshot and no game-running policy, so RunBusyAsync rather than a game write.
        PictureImportResult? result = null;
        var ok = await RunBusyAsync(
            $"Importing into {packName}",
            (progress, ct) => Task.Run(
                () => { result = PictureImporter.Import(sources, Services.LibraryPath, packName, vm.Fit, progress, ct); },
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
            await ApplyPicturesAsync(maps, packName, result.Written, vm.Then == AddPicturesThen.Ticked);
            return;
        }

        if (ok)
        {
            SetLibraryDone(result?.Copied == 1
                ? $"Imported 1 picture into {packName}"
                : $"Imported {result?.Copied ?? 0} pictures into {packName}");
        }

        await RescanAsync();
    }

    /// <summary>The maps the chosen radio names. A map with no background slots is left out: without level data
    /// there is nothing to write into (spec 3.6).</summary>
    private IReadOnlyList<MapEntry> ThenMaps(AddPicturesThen then, AddPicturesTarget target, ScanSnapshot snapshot)
    {
        IEnumerable<MapEntry> maps = then switch
        {
            AddPicturesThen.Map => target.Map is null ? [] : [target.Map],
            AddPicturesThen.Ticked => SelectedMaps,
            AddPicturesThen.All => snapshot.Catalog.Maps,
            _ => [],
        };

        return maps.Where(m => m.BackgroundSlots.Count > 0).ToList();
    }

    /// <summary>Spec 6.8's optional half, as one game write: the imported pictures go to the maps in order,
    /// starting again from the first picture when there are more maps than pictures, and a picture past the last
    /// map is imported only.</summary>
    private async Task ApplyPicturesAsync(
        IReadOnlyList<MapEntry> maps, string packName, IReadOnlyList<string> written, bool clearTicks)
    {
        // Spec 6.3: the maps are named before more than one of them is written. The import has already run, so
        // declining still rescans: the library has changed even though nothing was applied. Nothing reached the
        // game folder, so the ticks stay as they are.
        if (maps.Count > 1
            && !Dialogs.Confirm(
                "Apply pictures",
                $"Apply these pictures to these {maps.Count} maps?\n\n{string.Join(", ", maps.Select(m => m.DisplayName))}"))
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
            $"{Count(used, "picture")} applied to {Count(maps.Count, "map")}",
            clearTicks,
            packName);

        Dialogs.ShowFailures("Some pictures could not be applied", failures);
    }

    /// <summary>Spec 3.2 and 4: one picture into the background slots of every map given, as one game write.
    /// More than one map confirms with the count first (spec 3.3) and clears the ticks on success.
    /// displayName is the name the user chose the picture by, which is the pack on a panel tile and the picture's
    /// own name in the custom library: spec 2.2 words the done line "flowermap applied to Brawlhaven", never the
    /// file name inside the pack, which the user never picked. Null for a caller with no such name, and then the
    /// file name is the best there is. Whichever it is, the confirm, the busy title and the done line all use the
    /// one name, so the user reads the same word from the first prompt to the last line.</summary>
    public async Task ApplyPictureAsync(
        string sourcePath,
        IReadOnlyList<MapEntry> maps,
        bool clearTicks,
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
                "Apply picture",
                $"Apply {name} to these {targets.Count} maps?\n\n{string.Join(", ", targets.Select(m => m.DisplayName))}"))
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
                    foreach (var map in targets)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress.Report(map.DisplayName);
                        failures.AddRange(
                            BackgroundApplier.Apply(sourcePath, gamePath, map.BackgroundSlots, null, ct).Failures);
                    }
                },
                ct),
            targets.Count == 1
                ? $"{name} applied to {targets[0].DisplayName}"
                : $"{name} applied to {targets.Count} maps",
            clearTicks,
            packName);

        Dialogs.ShowFailures("Some backgrounds could not be applied", failures);
    }

    /// <summary>Spec 3.2: one pack's platform art onto every map given, each map getting its own set from the
    /// same pack. A map the pack has nothing for is left out rather than cleared.</summary>
    public async Task ApplySetAsync(Pack pack, IReadOnlyList<MapEntry> maps, bool clearTicks)
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
                $"Apply {pack.Name} to these {targets.Count} maps?\n\n{string.Join(", ", targets.Select(m => m.DisplayName))}"))
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
                    foreach (var map in targets)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress.Report(map.DisplayName);
                        failures.AddRange(PlatformSetApplier.Apply(pack, map.FolderName, gamePath, null, ct).Failures);
                    }
                },
                ct),
            targets.Count == 1
                ? $"{pack.Name} applied to {targets[0].DisplayName}"
                : $"{pack.Name} applied to {targets.Count} maps",
            clearTicks,
            pack.Name);

        Dialogs.ShowFailures("Some files could not be applied", failures);
    }

    /// <summary>"1 map" or "3 maps": the done lines count things and every one of them can be one.</summary>
    public static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    /// <summary>Spec 7.2: the editor fits a picture and saves it into a pack, which is a library write of its own.
    /// The "Apply to game now" it offers is a game write, so the pack file it left is copied into the slot here,
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
                clearTicks: false,
                Path.GetFileNameWithoutExtension(saved.PackFile),
                saved.PackName);
            return;
        }

        var gamePath = Services.GamePath;
        var source = saved.PackFile;
        var slot = saved.Slot;
        var failures = new List<FileFailure>();
        await RunGameWriteAsync(
            $"Applying {Path.GetFileName(source)}",
            BackgroundApplier.TargetPaths([slot]),
            (_, ct) => Task.Run(
                () => failures.AddRange(BackgroundApplier.Apply(source, gamePath, [slot], null, ct).Failures),
                ct),
            $"{Path.GetFileName(source)} applied to {saved.MapNames}",
            packName: saved.PackName);

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

    /// <summary>Spec 6: the editor recolours a map's own pieces into a pack, which is a library write of its own.
    /// The "Apply to game now" it offers is a game write, so the set it left is applied here, with the boundary,
    /// the snapshot and the undo every other write gets. The rescan comes first either way, because the pack the
    /// apply needs is one the last scan may never have seen. <paramref name="onlyFile" /> is the one file the
    /// editor opens ticked, for the panel row that asked for it (ruling 7). Spec 9: the editor opens on a set of
    /// maps, which is one map for every way in but the ticked selection, and one Save writes all of them.</summary>
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
                SetLibraryDone(WorkingCopyDone(vm.WorkingCopies));
            }

            return;
        }

        if (vm.Saved is not { } saved)
        {
            return;
        }

        await RescanAsync();
        if (!saved.ApplyToGame)
        {
            // Saved into the pack and no further, so nothing in the game folder moved and there is nothing to undo.
            return;
        }

        if (Snapshot?.Packs.FirstOrDefault(p => p.Name.Equals(saved.PackName, StringComparison.OrdinalIgnoreCase))
            is { } target)
        {
            await ApplySetAsync(target, saved.Maps, clearTicks: false);
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
            SetLibraryDone(jobs.Count == 1 ? $"Imported {jobs[0].PackName}" : $"Imported {jobs.Count} packs");
        }

        await RescanAsync();
    }

    /// <summary>Scans, then refreshes every page, not just the current one, so switching pages never shows stale data.</summary>
    public async Task RescanAsync()
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

        // A file may be a different picture now, so the rows pages' decodes are forgotten before they rebuild.
        Services.RowThumbnails.Clear();

        // Maps rebuilds its cards first, because SelectedMaps reads them and a page's Refresh may ask for it.
        foreach (var page in _pages)
        {
            page.Refresh(snapshot);
        }

        CanUndo = Services.Undo.Latest is not null;
    }

    /// <summary>Spec 3.5: the game data has been read, so the catalog the last scan built from the cache, or from
    /// nothing, is out of date. A read that lands mid-scan is remembered rather than started on top of it, and
    /// RunBusyAsync runs the one rescan it asked for when the boundary clears.</summary>
    private void OnLevelDataChanged()
    {
        if (IsBusy)
        {
            _rescanPending = true;
            return;
        }

        _ = RescanAsync();
    }

    /// <summary>Stops the game poll and drops the level-data subscription. Called once, when the window closes, so
    /// neither keeps waking a dispatcher that is on its way out.</summary>
    public void Shutdown()
    {
        _gameTimer.Stop();
        Services.LevelData.Changed -= OnLevelDataChanged;
    }

    /// <summary>Copies the game folder into the Default pack (spec 6.1), asking before replacing one that already
    /// exists. Shared by the Packs and Settings pages so the confirm text and the busy boundary are the same
    /// from both. A library-only write: no undo snapshot and no game-running policy.</summary>
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
                + "To be sure the capture is vanilla, verify the game files through Steam first."))
        {
            return;
        }

        var gamePath = Services.GamePath;
        ApplyResult? result = null;
        var ok = await RunBusyAsync(
            "Capturing defaults",
            (progress, ct) => Task.Run(() => { result = DefaultPack.Capture(gamePath, library, progress, ct); }, ct));
        if (result is not null)
        {
            Dialogs.ShowFailures("Some files could not be captured", result.Failures);
        }

        if (ok)
        {
            SetLibraryDone("Captured defaults");
        }

        await RescanAsync();
    }

    public Pack? FindPack(string name) =>
        Snapshot?.Packs.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Runs one long operation with the busy flag, progress text, and Cancel. False when cancelled or
    /// failed. A progress message is normally a fragment, so it is shown as "&lt;label&gt;: &lt;message&gt;";
    /// <paramref name="prefixProgress"/> false is for an operation whose messages already read as the whole line.</summary>
    public async Task<bool> RunBusyAsync(
        string label,
        Func<IProgress<string>, CancellationToken, Task> work,
        bool prefixProgress = true)
    {
        if (IsBusy)
        {
            return false;
        }

        _cts = new CancellationTokenSource();
        IsBusy = true;
        ProgressText = label;
        var progress = new Progress<string>(
            message => ProgressText = prefixProgress ? $"{label}: {message}" : message);
        try
        {
            await work(progress, _cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException && !GameFolderExists())
        {
            // The game folder went away under the operation. Spec 7.8 calls that a state the header reports, not a
            // failure worth a dialog: every write stays off until a scan finds the folder again.
            GameFolderMissing = true;
            return false;
        }
        catch (Exception ex)
        {
            Dialogs.Error("Something went wrong", ex.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
            ProgressText = "";
            _cts.Dispose();
            _cts = null;

            // Last, after the token is cleared: a rescan started any earlier would take _cts for itself and have it
            // disposed out from under it by the lines above. Cleared before the start, so the rescan's own turn
            // through this boundary cannot start a second one.
            if (_rescanPending)
            {
                _rescanPending = false;
                _ = RescanAsync();
            }
        }
    }

    /// <summary>The header line a library-only operation leaves: the same line a game write leaves, without the
    /// Undo button, because nothing in the game folder moved and the snapshot Undo holds is an older write's.</summary>
    public void SetLibraryDone(string doneText)
    {
        DoneText = doneText;
        DoneUndoable = false;
    }

    /// <summary>Spec 2.2: what a write reports about when it will show. One string, appended by the wrapper, so
    /// two pages cannot word it differently. Read at completion, not at the start: a game that was launched while
    /// the write ran gets the sentence that is true when the line appears.</summary>
    public static string DoneSentence(bool gameRunning) =>
        gameRunning ? "Shows on the next match load." : "Shows when Brawlhalla starts.";

    /// <summary>The line an undo leaves. Spec 11 names no string for it, so this is the plan's (A-D7).</summary>
    public const string UndoDoneText = "Last change undone.";

    /// <summary>The whole done line: the page's fragment, then the shared sentence, with exactly one period
    /// between them however the fragment was punctuated. The wrapper settles the period for the same reason it
    /// settles the sentence: a page cannot get it wrong if a page does not decide it. An empty fragment leaves
    /// the sentence standing alone rather than a line that opens with a stop.</summary>
    private static string DoneLine(string doneText, bool gameRunning)
    {
        var fragment = doneText.TrimEnd('.');
        return fragment.Length == 0 ? DoneSentence(gameRunning) : $"{fragment}. {DoneSentence(gameRunning)}";
    }

    /// <summary>One write into the game folder (spec 8): the busy boundary, an undo snapshot of the paths it is
    /// about to touch, a done line the wrapper finishes with the period and the shared sentence, the ticks
    /// cleared when the write was aimed at them, and a rescan. False when the folder was missing, another
    /// operation held the boundary, or the write was cancelled or failed. <paramref name="packName"/> is the pack
    /// the write came out of, stamped as last applied when it succeeds; null for undo, reset and a picture that
    /// was only ever in the game folder. <paramref name="libraryUndoPaths"/> names library files the write also
    /// changes, relative to the library folder, so the undo puts them back with the game files (spec 7).</summary>
    public Task<bool> RunGameWriteAsync(
        string label,
        IReadOnlyList<string> undoPaths,
        Func<IProgress<string>, CancellationToken, Task> work,
        string doneText,
        bool clearTicks = false,
        string? packName = null,
        IReadOnlyList<string>? libraryUndoPaths = null) =>
        RunWriteCoreAsync(label, undoPaths, work, doneText, undoable: true, clearTicks, packName, libraryUndoPaths);

    /// <summary>The one path every game write takes. <paramref name="undoPaths"/> null means take no snapshot,
    /// which is Undo's case and only Undo's: the snapshot it is restoring is the only one there is, and Begin
    /// would replace it with an empty one.</summary>
    private async Task<bool> RunWriteCoreAsync(
        string label,
        IReadOnlyList<string>? undoPaths,
        Func<IProgress<string>, CancellationToken, Task> work,
        string doneText,
        bool undoable,
        bool clearTicks,
        string? packName = null,
        IReadOnlyList<string>? libraryUndoPaths = null)
    {
        if (GameFolderMissing || IsBusy)
        {
            // Nothing to write into, or one operation at a time (spec 7.1). Refused silently: the top bar already
            // carries the missing-folder notice, and a refused write must not clear the done line or rescan.
            return false;
        }

        var gamePath = Services.GamePath;
        var libraryPath = Services.LibraryPath;
        var ok = await RunBusyAsync(
            label,
            async (progress, ct) =>
            {
                await _launcher.RunWriteAsync(
                    label,
                    async () =>
                    {
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
                                    var session = Services.Undo.Begin();
                                    session.Capture(gamePath, undoPaths);
                                    if (libraryUndoPaths is { Count: > 0 })
                                    {
                                        session.CaptureLibrary(libraryPath, libraryUndoPaths);
                                    }
                                },
                                ct);
                        }

                        await work(progress, ct);
                    });
            });

        // Begin has already replaced the previous snapshot, so a write that was cancelled or failed has to clear
        // the done line too; leaving it would describe something Undo no longer restores.
        DoneText = ok ? DoneLine(doneText, GameRunning) : "";
        DoneUndoable = ok && undoable;

        // A restore that fully succeeds discards its snapshot, so what can be undone is always read back from the
        // store rather than remembered.
        CanUndo = Services.Undo.Latest is not null;
        if (ok && clearTicks)
        {
            // Spec 3.3 (S3): a write aimed at the ticked maps is finished with them. A cancelled or failed write
            // keeps them, which is why this is inside the ok branch.
            ClearSelection();
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

        await RescanAsync();
        return ok;
    }
}
