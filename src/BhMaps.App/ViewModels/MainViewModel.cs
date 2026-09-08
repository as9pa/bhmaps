using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using BhMaps.App.Services;
using BhMaps.App.Views;
using BhMaps.Core.Game;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IDialogs _dialogs;
    private CancellationTokenSource? _cts;

    public MainViewModel(AppServices services, IDialogs dialogs)
    {
        _services = services;
        _dialogs = dialogs;
        ProgressText = "";
        StatusText = services.GamePath;
    }

    public ObservableCollection<PackItemViewModel> Packs { get; } = new();

    public ObservableCollection<FolderCardViewModel> Folders { get; } = new();

    /// <summary>Result of the last successful scan. Null until the first scan completes.</summary>
    protected ScanSnapshot? Snapshot { get; private set; }

    /// <summary>Non-null while the folder detail panel replaces the grid (spec 5.2).</summary>
    [ObservableProperty]
    public partial FolderDetailViewModel? Detail { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyAllCommand), nameof(OpenPackFolderCommand))]
    public partial PackItemViewModel? SelectedPack { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(
        nameof(RefreshCommand),
        nameof(ResetAllCommand),
        nameof(LaunchGameCommand),
        nameof(ApplyAllCommand),
        nameof(OpenPackFolderCommand),
        nameof(ImportCommand),
        nameof(SaveCurrentCommand),
        nameof(NewBackgroundCommand),
        nameof(OpenSettingsCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    public bool IsNotBusy => !IsBusy;

    private bool CanAct() => !IsBusy;

    private bool HasSelectedPack() => !IsBusy && SelectedPack is not null;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task RefreshAsync() => RescanAsync();

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void LaunchGame() => GameProcess.Launch();

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task OpenSettingsAsync()
    {
        var window = new SettingsWindow
        {
            DataContext = new SettingsViewModel(_services, _dialogs, null),
            Owner = Application.Current.MainWindow,
        };
        if (window.ShowDialog() != true)
        {
            return;
        }

        StatusText = _services.GamePath;
        await RescanAsync();
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ResetAllAsync()
    {
        if (Snapshot is null)
        {
            return;
        }

        var count = Snapshot.Tree.Folders.Count;
        if (!_dialogs.Confirm("Reset all", $"Delete the map art in all {count} folders? Brawlhalla regenerates the defaults on its next launch."))
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
            (progress, ct) => Task.Run(() => { result = GameResetter.ResetAll(_services.GamePath, progress, ct); }, ct));
        if (result is not null)
        {
            _dialogs.ShowFailures("Some files could not be deleted", result.Failures);
        }

        await RescanAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedPack))]
    private async Task ApplyAllAsync()
    {
        var pack = SelectedPack?.Pack;
        if (pack is null || !ConfirmIfGameRunning())
        {
            return;
        }

        ApplyResult? result = null;
        await RunBusyAsync(
            $"Applying {pack.Name}",
            (progress, ct) => Task.Run(() => { result = PackApplier.ApplyPack(pack, _services.GamePath, progress, ct); }, ct));
        if (result is not null)
        {
            _dialogs.ShowFailures("Some files could not be copied", result.Failures);
        }

        await RescanAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedPack))]
    private void OpenPackFolder()
    {
        var pack = SelectedPack?.Pack;
        if (pack is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{pack.FullPath}\"") { UseShellExecute = true })?.Dispose();
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ImportAsync()
    {
        if (Snapshot is null)
        {
            return;
        }

        var vm = new ImportViewModel(Snapshot.Tree, _dialogs);
        var window = new ImportWindow { DataContext = vm, Owner = Application.Current.MainWindow };
        if (window.ShowDialog() != true || vm.Plan is null)
        {
            return;
        }

        await RunImportAsync(vm.Plan, vm.PackName.Trim());
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task SaveCurrentAsync()
    {
        if (Snapshot is null)
        {
            return;
        }

        var name = _dialogs.PromptText("Save current map art as a pack", "Pack name:", $"backup-{DateTime.Now:yyyy-MM-dd}");
        if (name is null)
        {
            return;
        }

        name = name.Trim();
        if (!PackNameValidator.IsValid(name, out var error))
        {
            _dialogs.Error("Invalid pack name", error);
            return;
        }

        var tree = Snapshot.Tree;
        var plan = await Task.Run(() => ImportRouter.Plan(tree.RootPath, tree));
        if (plan.IncludedCount == 0)
        {
            _dialogs.Info("Nothing to save", "The game folder has no image files to save as a pack.");
            return;
        }

        await RunImportAsync(plan, name);
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task NewBackgroundAsync() => OpenBackgroundEditorAsync(null);

    public async Task OpenBackgroundEditorAsync(string? initialSlot)
    {
        if (Snapshot is null)
        {
            return;
        }

        var slots = Snapshot.Tree.FindFolder("Backgrounds")?.Files.Select(f => f.Name).ToList() ?? new List<string>();
        var packNames = Snapshot.Packs.Select(p => p.Name).ToList();
        var vm = new BackgroundEditorViewModel(_services, _dialogs, slots, packNames, initialSlot);
        var window = new BackgroundEditorWindow { DataContext = vm, Owner = Application.Current.MainWindow };
        if (window.ShowDialog() == true)
        {
            await RescanAsync();
        }
    }

    /// <summary>Spec 6: merge prompt when the pack exists, then Execute as a long operation, rescan, and select the pack.</summary>
    protected async Task RunImportAsync(ImportPlan plan, string packName)
    {
        var packDir = Path.Combine(PackScanner.PacksRoot(_services.LibraryPath), packName);
        if (Directory.Exists(packDir)
            && !_dialogs.Confirm(
                "Pack already exists",
                $"A pack named '{packName}' already exists. Merge into it? Files with the same name are overwritten; other files stay."))
        {
            return;
        }

        ApplyResult? result = null;
        await RunBusyAsync(
            $"Importing into {packName}",
            (progress, ct) => Task.Run(() => { result = ImportRouter.Execute(plan, packName, _services.LibraryPath, progress, ct); }, ct));
        if (result is not null)
        {
            _dialogs.ShowFailures("Some files could not be imported", result.Failures);
        }

        await RescanAsync();
        SelectedPack = Packs.FirstOrDefault(p => p.Name.Equals(packName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task ApplyFolderFromPackAsync(string folderName, string packName)
    {
        var pack = FindPack(packName);
        if (pack is null || !ConfirmIfGameRunning())
        {
            return;
        }

        ApplyResult? result = null;
        await RunBusyAsync(
            $"Applying {folderName} from {packName}",
            (progress, ct) => Task.Run(() => { result = PackApplier.ApplyFolder(pack, folderName, _services.GamePath, progress, ct); }, ct));
        if (result is not null)
        {
            _dialogs.ShowFailures("Some files could not be copied", result.Failures);
        }

        await RescanAsync();
    }

    public async Task ResetFolderAsync(string folderName)
    {
        if (!_dialogs.Confirm("Reset folder", $"Delete the map art in {folderName}? Brawlhalla regenerates the defaults on its next launch."))
        {
            return;
        }

        if (!ConfirmIfGameRunning())
        {
            return;
        }

        ResetResult? result = null;
        await RunBusyAsync(
            $"Resetting {folderName}",
            (_, _) => Task.Run(() => { result = GameResetter.ResetFolder(_services.GamePath, folderName); }));
        if (result is not null)
        {
            _dialogs.ShowFailures("Some files could not be deleted", result.Failures);
        }

        await RescanAsync();
    }

    public void OpenDetail(string folderName)
    {
        if (Snapshot is null)
        {
            return;
        }

        Detail = new FolderDetailViewModel(this, Snapshot, folderName, _services.Thumbnails);
    }

    public void CloseDetail() => Detail = null;

    public async Task ApplyFileFromPackAsync(string folderName, string fileName, string packName)
    {
        var pack = FindPack(packName);
        if (pack is null || !ConfirmIfGameRunning())
        {
            return;
        }

        ApplyResult? result = null;
        await RunBusyAsync(
            $"Applying {folderName}\\{fileName} from {packName}",
            (_, _) => Task.Run(() => { result = PackApplier.ApplyFile(pack, folderName, fileName, _services.GamePath); }));
        if (result is not null)
        {
            _dialogs.ShowFailures("The file could not be copied", result.Failures);
        }

        await RescanAsync();
    }

    public async Task ResetFileAsync(string folderName, string fileName)
    {
        if (!_dialogs.Confirm("Reset file", $"Delete {folderName}\\{fileName}? Brawlhalla regenerates the default on its next launch."))
        {
            return;
        }

        if (!ConfirmIfGameRunning())
        {
            return;
        }

        ResetResult? result = null;
        await RunBusyAsync(
            $"Resetting {folderName}\\{fileName}",
            (_, _) => Task.Run(() => { result = GameResetter.ResetFile(_services.GamePath, folderName, fileName); }));
        if (result is not null)
        {
            _dialogs.ShowFailures("The file could not be deleted", result.Failures);
        }

        await RescanAsync();
    }

    public async Task RescanAsync()
    {
        ScanSnapshot? snapshot = null;
        var ok = await RunBusyAsync(
            "Scanning",
            (progress, ct) => Task.Run(() => { snapshot = _services.Scan(progress, ct); }, ct));
        if (!ok || snapshot is null)
        {
            return;
        }

        Snapshot = snapshot;
        Populate(snapshot);
    }

    /// <summary>Spec 6: warn when Brawlhalla is running. True means go ahead.</summary>
    protected bool ConfirmIfGameRunning() =>
        !GameProcess.IsRunning()
        || _dialogs.Confirm(
            "Brawlhalla is running",
            "Brawlhalla is running. Changes will not show until it restarts, and some files may be locked. Continue?");

    protected Pack? FindPack(string name) =>
        Snapshot?.Packs.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Runs one long operation with the busy flag, progress text, and Cancel. False when cancelled or failed.</summary>
    protected async Task<bool> RunBusyAsync(string label, Func<IProgress<string>, CancellationToken, Task> work)
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
            _dialogs.Error("Something went wrong", ex.Message);
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

    private void Populate(ScanSnapshot snapshot)
    {
        var selectedName = SelectedPack?.Name;
        Packs.Clear();
        foreach (var pack in snapshot.Packs)
        {
            Packs.Add(new PackItemViewModel(pack));
        }

        SelectedPack = Packs.FirstOrDefault(p => p.Name == selectedName);

        Folders.Clear();
        var tile = Application.Current.TryFindResource("BackgroundsTile") as ImageSource;
        foreach (var folder in snapshot.Tree.Folders)
        {
            var card = new FolderCardViewModel(this, folder, snapshot.Status.ForFolder(folder.Name), snapshot.Packs);
            Folders.Add(card);
            _ = card.LoadThumbnailAsync(_services.Thumbnails, tile);
        }

        StatusText = $"{_services.GamePath}   |   {snapshot.Tree.Folders.Count} folders, {snapshot.Packs.Count} packs";

        if (Detail is { } open)
        {
            Detail = new FolderDetailViewModel(this, snapshot, open.FolderName, _services.Thumbnails);
        }
    }
}
