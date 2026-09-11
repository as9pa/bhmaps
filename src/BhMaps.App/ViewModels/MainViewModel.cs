using System.Windows;
using System.Windows.Threading;
using BhMaps.App.Services;
using BhMaps.App.ViewModels.Pages;
using BhMaps.App.Views;
using BhMaps.Core.Game;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
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
        Packs = new PacksViewModel(this);
        PackDetail = new PackDetailViewModel(this);
        SettingsPage = new SettingsPageViewModel(this);
        _pages = [Maps, Backgrounds, Packs, PackDetail, SettingsPage];
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
        ApplyResult? result = null;
        await RunWriteCoreAsync(
            "Undoing",
            undoPaths: null,
            (_, ct) => Task.Run(() => { result = Services.Undo.Restore(session, gamePath); }, ct),
            UndoDoneText,
            undoable: false,
            clearTicks: false);

        if (result is not null)
        {
            Dialogs.ShowFailures("Some files could not be restored", result.Failures);
        }
    }

    /// <summary>Opens the Add pictures window (spec 6.8), then imports what it collected and, when it asked for
    /// it, applies those pictures to the maps. <paramref name="mapFolder"/> is the one map the pictures should
    /// also go to, from the Maps panel; null means the ticked maps decide.</summary>
    public async Task OpenAddPicturesAsync(string? mapFolder)
    {
        if (Snapshot is not { } snapshot)
        {
            return;
        }

        var maps = AddPicturesTargets(snapshot, mapFolder);
        var vm = new AddPicturesViewModel(
            Dialogs, snapshot.Packs.Select(p => p.Name).ToList(), maps.Count, mapFolder is not null);
        var window = new AddPicturesWindow { DataContext = vm, Owner = Application.Current.MainWindow };
        if (window.ShowDialog() != true)
        {
            return;
        }

        var sources = vm.Files.Select(f => f.FullPath).ToList();
        var packName = vm.EffectivePackName;
        var apply = vm.ApplyToMaps && maps.Count > 0;

        // Spec 6.3: the maps are named before more than one of them is written. Declining leaves the import to
        // run on its own, so the work of choosing the pictures is not thrown away with the apply.
        if (apply
            && maps.Count > 1
            && !Dialogs.Confirm(
                "Apply pictures",
                $"Apply these pictures to these {maps.Count} maps?\n\n{string.Join(", ", maps.Select(m => m.DisplayName))}"))
        {
            apply = false;
        }

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

        // Nothing landing in the pack leaves nothing to apply, however the checkbox was left.
        if (ok && apply && result is { Written.Count: > 0 })
        {
            // The apply rescans on its way out, and its done line is the one that ends up in the header.
            await ApplyPicturesAsync(maps, packName, result.Written);
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

    /// <summary>The maps the Add pictures dialog would write to: the one map the Maps panel named, or the ticked
    /// maps. A map with no background slots is left out, because without level data there is nothing to write
    /// into (spec 3.6).</summary>
    private IReadOnlyList<MapEntry> AddPicturesTargets(ScanSnapshot snapshot, string? mapFolder) =>
        (mapFolder is null
            ? SelectedMaps
            : [.. new[] { snapshot.Catalog.ByFolder(mapFolder) }.OfType<MapEntry>()])
        .Where(m => m.BackgroundSlots.Count > 0)
        .ToList();

    /// <summary>Spec 6.8's optional half, as one game write: the imported pictures go to the maps in order,
    /// starting again from the first picture when there are more maps than pictures, and a picture past the last
    /// map is imported only.</summary>
    private async Task ApplyPicturesAsync(IReadOnlyList<MapEntry> maps, string packName, IReadOnlyList<string> written)
    {
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
            $"{Count(used, "picture")} applied to {Count(maps.Count, "map")}");

        Dialogs.ShowFailures("Some pictures could not be applied", failures);
    }

    /// <summary>"1 map" or "3 maps": the done lines count things and every one of them can be one.</summary>
    public static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    /// <summary>Spec 5.3: the editor fits a picture and saves it into a pack. That is a library write and its own
    /// business; the "Apply to game now" it offers is a game write, so the pack file it left is copied into the
    /// slot here, with the boundary, the snapshot and the running-game policy every other one gets.</summary>
    public async Task OpenBackgroundEditorAsync(string? initialSlot)
    {
        if (Snapshot is null)
        {
            return;
        }

        var slots = Snapshot.Tree.FindFolder("Backgrounds")?.Files.Select(f => f.Name).ToList() ?? new List<string>();
        var packNames = Snapshot.Packs.Select(p => p.Name).ToList();
        var vm = new BackgroundEditorViewModel(Services, Dialogs, slots, packNames, initialSlot);
        var window = new BackgroundEditorWindow { DataContext = vm, Owner = Application.Current.MainWindow };
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

        var gamePath = Services.GamePath;
        var source = saved.PackFile;
        var slot = saved.Slot;
        var failures = new List<FileFailure>();
        await RunGameWriteAsync(
            $"Applying {slot}",
            BackgroundApplier.TargetPaths([slot]),
            (_, ct) => Task.Run(
                () => failures.AddRange(BackgroundApplier.Apply(source, gamePath, [slot], null, ct).Failures),
                ct),
            $"Background applied to {slot}");

        Dialogs.ShowFailures("Some backgrounds could not be applied", failures);
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
    /// operation held the boundary, or the write was cancelled or failed.</summary>
    public Task<bool> RunGameWriteAsync(
        string label,
        IReadOnlyList<string> undoPaths,
        Func<IProgress<string>, CancellationToken, Task> work,
        string doneText,
        bool clearTicks = false) =>
        RunWriteCoreAsync(label, undoPaths, work, doneText, undoable: true, clearTicks);

    /// <summary>The one path every game write takes. <paramref name="undoPaths"/> null means take no snapshot,
    /// which is Undo's case and only Undo's: the snapshot it is restoring is the only one there is, and Begin
    /// would replace it with an empty one.</summary>
    private async Task<bool> RunWriteCoreAsync(
        string label,
        IReadOnlyList<string>? undoPaths,
        Func<IProgress<string>, CancellationToken, Task> work,
        string doneText,
        bool undoable,
        bool clearTicks)
    {
        if (GameFolderMissing || IsBusy)
        {
            // Nothing to write into, or one operation at a time (spec 7.1). Refused silently: the top bar already
            // carries the missing-folder notice, and a refused write must not clear the done line or rescan.
            return false;
        }

        var gamePath = Services.GamePath;
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
                            await Task.Run(() => Services.Undo.Begin().Capture(gamePath, undoPaths), ct);
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

        await RescanAsync();
        return ok;
    }
}
