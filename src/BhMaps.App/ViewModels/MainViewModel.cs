using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
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

/// <summary>The shell (decision D13): the busy boundary, navigation between the six pages, the scan that feeds
/// them, and the undo of the last game write.</summary>
public partial class MainViewModel : ObservableObject
{
    private const int MaxSuggestions = 8;

    private static readonly TimeSpan GamePollInterval = TimeSpan.FromSeconds(3);

    private readonly GameLauncher _launcher;
    private readonly IReadOnlyList<PageViewModel> _pages;

    /// <summary>The view the sidebar list binds through, so the search filters without a second collection.</summary>
    private readonly ICollectionView _mapListView;

    private readonly DispatcherTimer _gameTimer;
    private CancellationTokenSource? _cts;

    public MainViewModel(AppServices services, IDialogs dialogs)
    {
        Services = services;
        Dialogs = dialogs;
        ProgressText = "";
        DoneText = "";
        MapList = [];
        Suggestions = [];
        _mapListView = CollectionViewSource.GetDefaultView(MapList);
        _mapListView.Filter = MatchesSearch;

        // After the list and its view, because setting it runs the change hook that filters them.
        SearchText = "";
        _launcher = new GameLauncher(services, dialogs);
        Home = new HomeViewModel(this);
        Backgrounds = new BackgroundsViewModel(this);
        Platforms = new PlatformsViewModel(this);
        Packs = new PacksViewModel(this);
        PackDetail = new PackDetailViewModel(this);
        SettingsPage = new SettingsPageViewModel(this);
        _pages = [Home, Backgrounds, Platforms, Packs, PackDetail, SettingsPage];
        CurrentPage = Home;

        // Nothing tells the app when Brawlhalla starts or stops, so the sidebar's game block asks every 3 seconds.
        GameRunning = GameProcess.IsRunning();
        _gameTimer = new DispatcherTimer { Interval = GamePollInterval };
        _gameTimer.Tick += (_, _) => GameRunning = GameProcess.IsRunning();
        _gameTimer.Start();
    }

    public AppServices Services { get; }

    public IDialogs Dialogs { get; }

    /// <summary>Result of the last successful scan. Null until the first scan completes.</summary>
    public ScanSnapshot? Snapshot { get; private set; }

    public HomeViewModel Home { get; }

    public BackgroundsViewModel Backgrounds { get; }

    public PlatformsViewModel Platforms { get; }

    public PacksViewModel Packs { get; }

    public PackDetailViewModel PackDetail { get; }

    public SettingsPageViewModel SettingsPage { get; }

    /// <summary>Every map the scan found, in display-name order. The sidebar lists this collection's default view,
    /// filtered live by <see cref="SearchText"/>, so there is no second copy to keep in step.</summary>
    public ObservableCollection<MapListItemViewModel> MapList { get; }

    /// <summary>The search box's autocomplete list: at most eight display names, empty when the box is empty.</summary>
    public ObservableCollection<string> Suggestions { get; }

    /// <summary>The ticked maps, in list order. A page's multi-map action reads this and its count.</summary>
    public IReadOnlyList<MapListItemViewModel> SelectedMaps => MapList.Where(m => m.IsSelected).ToList();

    public int SelectedMapCount => SelectedMaps.Count;

    [ObservableProperty]
    public partial string SearchText { get; set; }

    /// <summary>Whether Brawlhalla is running, as of the last poll. The sidebar's game block shows it.</summary>
    [ObservableProperty]
    public partial bool GameRunning { get; set; }

    [ObservableProperty]
    public partial PageViewModel? CurrentPage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(
        nameof(RefreshCommand),
        nameof(LaunchGameCommand),
        nameof(ImportCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; }

    /// <summary>What the last write did, shown in the page header beside Undo. Empty when there is nothing to show.</summary>
    [ObservableProperty]
    public partial string DoneText { get; set; }

    [ObservableProperty]
    public partial bool CanUndo { get; set; }

    /// <summary>Folder name of the map most recently opened on Home, or null before any. Home sets it; the
    /// Platforms page falls back to it when the sidebar has no checked map (spec 7.4).</summary>
    [ObservableProperty]
    public partial string? LastOpenedMap { get; set; }

    public bool IsNotBusy => !IsBusy;

    private bool CanAct() => !IsBusy;

    [RelayCommand]
    private void NavigateHome() => CurrentPage = Home;

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

    /// <summary>Enter on a suggestion, or a click on one: the box takes the whole name, and on Home the map that
    /// name belongs to opens. On any other page choosing only narrows the list.</summary>
    [RelayCommand]
    private void ChooseSuggestion(string? displayName)
    {
        if (string.IsNullOrEmpty(displayName))
        {
            return;
        }

        SearchText = displayName;
        var map = MapList.FirstOrDefault(m => m.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase));
        if (map is not null && CurrentPage == Home)
        {
            Home.OpenMap(map.FolderName);
        }
    }

    /// <summary>Unticks every map. The pages' multi-map bars offer it as "Clear".</summary>
    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var map in MapList)
        {
            map.IsSelected = false;
        }
    }

    /// <summary>Every keystroke re-filters the list and rebuilds the suggestions.</summary>
    partial void OnSearchTextChanged(string value)
    {
        _mapListView.Refresh();
        Suggestions.Clear();
        foreach (var name in Suggest(value))
        {
            Suggestions.Add(name);
        }
    }

    /// <summary>Names that start with what was typed first, then names that merely contain it, capped at eight.</summary>
    private IEnumerable<string> Suggest(string search)
    {
        if (search.Length == 0)
        {
            return [];
        }

        var names = MapList.Select(m => m.DisplayName).ToList();
        return names
            .Where(n => n.StartsWith(search, StringComparison.OrdinalIgnoreCase))
            .Concat(names.Where(n => n.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxSuggestions);
    }

    private bool MatchesSearch(object item) =>
        item is MapListItemViewModel map
        && (SearchText.Length == 0 || map.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

    /// <summary>Rebuilds the sidebar list from a scan, keeping the maps that were ticked ticked, matched by folder
    /// name because a game update can rename a map.</summary>
    private void PopulateMapList(ScanSnapshot snapshot)
    {
        var ticked = MapList
            .Where(m => m.IsSelected)
            .Select(m => m.FolderName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var old in MapList)
        {
            old.PropertyChanged -= OnMapItemChanged;
        }

        MapList.Clear();
        foreach (var map in snapshot.Catalog.Maps)
        {
            snapshot.MapStatuses.TryGetValue(map.FolderName, out var status);

            // Ticked before subscribing, so restoring the selection is not mistaken for the user changing it.
            var item = new MapListItemViewModel(map, status) { IsSelected = ticked.Contains(map.FolderName) };
            item.PropertyChanged += OnMapItemChanged;
            MapList.Add(item);
        }

        NotifySelectionChanged();
    }

    private void OnMapItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapListItemViewModel.IsSelected))
        {
            NotifySelectionChanged();
        }
    }

    /// <summary>SelectedMaps is computed, so the pages bound to it are told by hand when it changes.</summary>
    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedMaps));
        OnPropertyChanged(nameof(SelectedMapCount));
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

        var vm = new ImportViewModel(Snapshot.Tree, Dialogs);
        var window = new ImportWindow { DataContext = vm, Owner = Application.Current.MainWindow };
        if (window.ShowDialog() != true || vm.Plan is null)
        {
            return;
        }

        await RunImportAsync(vm.Plan, vm.PackName.Trim());
    }

    /// <summary>Spec 6.4: puts back the files the last game write was about to overwrite or delete.</summary>
    [RelayCommand]
    private async Task UndoAsync()
    {
        if (Services.Undo.Latest is not { } session)
        {
            return;
        }

        var gamePath = Services.GamePath;
        ApplyResult? result = null;
        await RunBusyAsync(
            "Undoing",
            (_, _) => Task.Run(() => { result = Services.Undo.Restore(session, gamePath); }));
        if (result is not null)
        {
            Dialogs.ShowFailures("Some files could not be restored", result.Failures);
        }

        DoneText = "";
        CanUndo = Services.Undo.Latest is not null;
        await RescanAsync();
    }

    /// <summary>Opens the Add pictures window (spec 6.8), then imports what it collected and, when it asked for
    /// it, applies those pictures to the maps. <paramref name="mapFolder"/> is the one map the pictures should
    /// also go to, from the Home panel; null means the sidebar's ticked maps decide.</summary>
    public async Task OpenAddPicturesAsync(string? mapFolder)
    {
        if (Snapshot is not { } snapshot)
        {
            return;
        }

        var maps = AddPicturesTargets(snapshot, mapFolder);
        var vm = new AddPicturesViewModel(Dialogs, snapshot.Packs.Select(p => p.Name).ToList(), maps.Count);
        var window = new AddPicturesWindow { DataContext = vm, Owner = Application.Current.MainWindow };
        if (window.ShowDialog() != true)
        {
            return;
        }

        var sources = vm.Files.Select(f => f.FullPath).ToList();
        var packName = vm.EffectivePackName;
        var apply = vm.ApplyToMaps && maps.Count > 0;

        // Spec 6.3: the maps are named before more than one of them is written. The confirm comes before the
        // import rather than between the two writes, so that saying no here imports nothing either.
        if (apply
            && maps.Count > 1
            && !Dialogs.Confirm(
                "Apply pictures",
                $"Apply these pictures to these {maps.Count} maps?\n\n{string.Join(", ", maps.Select(m => m.DisplayName))}"))
        {
            return;
        }

        // A library write: no undo snapshot and no game-running policy, so RunBusyAsync rather than a game write.
        ApplyResult? result = null;
        var ok = await RunBusyAsync(
            $"Importing into {packName}",
            (progress, ct) => Task.Run(
                () => { result = PictureImporter.Import(sources, Services.LibraryPath, packName, vm.Fit, progress, ct); },
                ct));
        if (result is not null)
        {
            Dialogs.ShowFailures("Some pictures could not be imported", result.Failures);
        }

        if (ok && apply)
        {
            // The apply rescans on its way out, and its done line is the one that ends up in the header.
            await ApplyPicturesAsync(maps, packName, sources);
            return;
        }

        if (ok)
        {
            DoneText = result?.Copied == 1
                ? $"Imported 1 picture into {packName}"
                : $"Imported {result?.Copied ?? 0} pictures into {packName}";
        }

        await RescanAsync();
    }

    /// <summary>The maps the Add pictures dialog would write to: the one map the Home panel named, or the
    /// sidebar's ticked maps. A map with no background slots is left out, because without level data there is
    /// nothing to write into (spec 3.6).</summary>
    private IReadOnlyList<MapEntry> AddPicturesTargets(ScanSnapshot snapshot, string? mapFolder)
    {
        IEnumerable<string> folders = mapFolder is null ? SelectedMaps.Select(m => m.FolderName) : [mapFolder];
        return folders
            .Select(snapshot.Catalog.ByFolder)
            .OfType<MapEntry>()
            .Where(m => m.BackgroundSlots.Count > 0)
            .ToList();
    }

    /// <summary>Spec 6.8's optional half, as one game write: the imported pictures go to the maps in order,
    /// starting again from the first picture when there are more maps than pictures, and a picture past the last
    /// map is imported only.</summary>
    private async Task ApplyPicturesAsync(IReadOnlyList<MapEntry> maps, string packName, IReadOnlyList<string> sources)
    {
        // The importer's target name for each source, which is the file the import just wrote into the pack. A
        // second source mapping to the same name took the silent " (2)" suffix, so it is the first source's
        // picture that both of those maps get; the importer does not report the names it chose.
        var backgrounds = Path.Combine(
            PackScanner.PacksRoot(Services.LibraryPath), packName, PictureImporter.BackgroundsFolder);
        var pictures = sources.Select(s => Path.Combine(backgrounds, PictureImporter.TargetFileName(s))).ToList();

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
    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

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
        if (window.ShowDialog() == true)
        {
            await RescanAsync();
        }
    }

    /// <summary>Spec 6: merge prompt when the pack exists, then Execute as a long operation, then rescan.</summary>
    protected async Task RunImportAsync(ImportPlan plan, string packName)
    {
        var packDir = Path.Combine(PackScanner.PacksRoot(Services.LibraryPath), packName);
        if (Directory.Exists(packDir)
            && !Dialogs.Confirm(
                "Pack already exists",
                $"A pack named '{packName}' already exists. Merge into it? Files with the same name are overwritten; other files stay."))
        {
            return;
        }

        ApplyResult? result = null;
        await RunBusyAsync(
            $"Importing into {packName}",
            (progress, ct) => Task.Run(() => { result = ImportRouter.Execute(plan, packName, Services.LibraryPath, progress, ct); }, ct));
        if (result is not null)
        {
            Dialogs.ShowFailures("Some files could not be imported", result.Failures);
        }

        await RescanAsync();
    }

    /// <summary>Scans, then refreshes every page, not just the current one, so switching pages never shows stale data.</summary>
    public async Task RescanAsync()
    {
        ScanSnapshot? snapshot = null;
        var ok = await RunBusyAsync(
            "Scanning",
            (progress, ct) => Task.Run(() => { snapshot = Services.Scan(progress, ct); }, ct));
        if (!ok || snapshot is null)
        {
            return;
        }

        Snapshot = snapshot;

        // Before the pages, because a page's Refresh may read the sidebar's selection.
        PopulateMapList(snapshot);
        foreach (var page in _pages)
        {
            page.Refresh(snapshot);
        }

        CanUndo = Services.Undo.Latest is not null;
    }

    /// <summary>Stops the game poll. Called once, when the window closes, so the timer does not keep ticking on
    /// a dispatcher that is on its way out.</summary>
    public void Shutdown() => _gameTimer.Stop();

    /// <summary>Copies the game folder into the Default pack (spec 6.1), asking before replacing one that already
    /// exists. Shared by the Packs and Settings pages so the confirm text and the busy boundary are the same
    /// from both. A library-only write: no undo snapshot and no game-running policy.</summary>
    public async Task CaptureDefaultsAsync()
    {
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
        await RunBusyAsync(
            "Capturing defaults",
            (progress, ct) => Task.Run(() => { result = DefaultPack.Capture(gamePath, library, progress, ct); }, ct));
        if (result is not null)
        {
            Dialogs.ShowFailures("Some files could not be captured", result.Failures);
        }

        await RescanAsync();
    }

    /// <summary>Spec 6: warn when Brawlhalla is running. True means go ahead.</summary>
    public bool ConfirmIfGameRunning() =>
        !GameProcess.IsRunning()
        || Dialogs.Confirm(
            "Brawlhalla is running",
            "Brawlhalla is running. Changes will not show until it restarts, and some files may be locked. Continue?");

    public Pack? FindPack(string name) =>
        Snapshot?.Packs.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Runs one long operation with the busy flag, progress text, and Cancel. False when cancelled or failed.</summary>
    public async Task<bool> RunBusyAsync(string label, Func<IProgress<string>, CancellationToken, Task> work)
    {
        if (IsBusy)
        {
            return false;
        }

        _cts = new CancellationTokenSource();
        IsBusy = true;
        ProgressText = label;
        var progress = new Progress<string>(message => ProgressText = $"{label}: {message}");
        try
        {
            await work(progress, _cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
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
        }
    }

    /// <summary>One write into the game folder: the while-the-game-runs policy around it, an undo snapshot of the
    /// paths it is about to touch, the busy boundary, a done line, and a rescan.</summary>
    public async Task RunGameWriteAsync(
        string label,
        IReadOnlyList<string> undoPaths,
        Func<IProgress<string>, CancellationToken, Task> work,
        string doneText)
    {
        var gamePath = Services.GamePath;
        await _launcher.RunWriteAsync(
            label,
            async () =>
            {
                // Inside the launcher's callback, so with whileRunning=restart the game is already closed and its
                // files are the ones being snapshotted. The capture is the first step of the busy operation, not a
                // step before it: it is file copying, so it belongs off the UI thread, behind a progress line, and
                // inside the boundary that turns an IO failure into the same dialog any other write failure gets.
                var ok = await RunBusyAsync(
                    label,
                    async (progress, ct) =>
                    {
                        progress.Report("Saving undo");
                        await Task.Run(() => Services.Undo.Begin().Capture(gamePath, undoPaths), ct);
                        await work(progress, ct);
                    });

                // Begin has already replaced the previous snapshot, so a write that was cancelled or failed has to
                // clear the done line too; leaving it would describe something Undo no longer restores.
                DoneText = ok ? doneText : "";
            });

        // A restore that fully succeeds discards its snapshot, so what can be undone is always read back from the
        // store rather than remembered.
        CanUndo = Services.Undo.Latest is not null;
        await RescanAsync();
    }
}
