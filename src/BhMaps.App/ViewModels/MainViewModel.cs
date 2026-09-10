using System.Windows;
using BhMaps.App.Services;
using BhMaps.App.ViewModels.Pages;
using BhMaps.App.Views;
using BhMaps.Core.Game;
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
    private readonly GameLauncher _launcher;
    private readonly IReadOnlyList<PageViewModel> _pages;
    private CancellationTokenSource? _cts;

    public MainViewModel(AppServices services, IDialogs dialogs)
    {
        Services = services;
        Dialogs = dialogs;
        ProgressText = "";
        DoneText = "";
        _launcher = new GameLauncher(services, dialogs);
        Home = new HomeViewModel(this);
        Backgrounds = new BackgroundsViewModel(this);
        Platforms = new PlatformsViewModel(this);
        Packs = new PacksViewModel(this);
        PackDetail = new PackDetailViewModel(this);
        SettingsPage = new SettingsPageViewModel(this);
        _pages = [Home, Backgrounds, Platforms, Packs, PackDetail, SettingsPage];
        CurrentPage = Home;
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

    [ObservableProperty]
    public partial PageViewModel? CurrentPage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(
        nameof(RefreshCommand),
        nameof(ResetAllCommand),
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

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task RefreshAsync() => RescanAsync();

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void LaunchGame() => GameProcess.Launch();

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ResetAllAsync()
    {
        if (Snapshot is null)
        {
            return;
        }

        var count = Snapshot.Tree.Folders.Count;
        if (!Dialogs.Confirm("Reset all", $"Delete the map art in all {count} folders? Brawlhalla regenerates the defaults on its next launch."))
        {
            return;
        }

        if (!ConfirmIfGameRunning())
        {
            return;
        }

        ResetResult? result = null;
        await RunBusyAsync(
            "Resetting",
            (progress, ct) => Task.Run(() => { result = GameResetter.ResetAll(Services.GamePath, progress, ct); }, ct));
        if (result is not null)
        {
            Dialogs.ShowFailures("Some files could not be deleted", result.Failures);
        }

        await RescanAsync();
    }

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
        foreach (var page in _pages)
        {
            page.Refresh(snapshot);
        }

        CanUndo = Services.Undo.Latest is not null;
    }

    /// <summary>Spec 6: warn when Brawlhalla is running. True means go ahead.</summary>
    protected bool ConfirmIfGameRunning() =>
        !GameProcess.IsRunning()
        || Dialogs.Confirm(
            "Brawlhalla is running",
            "Brawlhalla is running. Changes will not show until it restarts, and some files may be locked. Continue?");

    protected Pack? FindPack(string name) =>
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
                // files are the ones being snapshotted. Only the write itself takes the busy boundary.
                Services.Undo.Begin().Capture(gamePath, undoPaths);

                // Begin has already replaced the previous snapshot, so a write that was cancelled or failed has to
                // clear the done line too; leaving it would describe something Undo no longer restores.
                DoneText = await RunBusyAsync(label, work) ? doneText : "";
            });

        // A restore that fully succeeds discards its snapshot, so what can be undone is always read back from the
        // store rather than remembered.
        CanUndo = Services.Undo.Latest is not null;
        await RescanAsync();
    }
}
