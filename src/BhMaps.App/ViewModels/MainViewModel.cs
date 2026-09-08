using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using BhMaps.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private CancellationTokenSource? _cts;

    public MainViewModel(AppServices services)
    {
        _services = services;
        ProgressText = "";
        StatusText = services.GamePath;
    }

    public ObservableCollection<PackItemViewModel> Packs { get; } = new();

    public ObservableCollection<FolderCardViewModel> Folders { get; } = new();

    [ObservableProperty]
    public partial PackItemViewModel? SelectedPack { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    public bool IsNotBusy => !IsBusy;

    private bool CanAct() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task RefreshAsync() => RescanAsync();

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

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

        Populate(snapshot);
    }

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
            MessageBox.Show(ex.Message, "Something went wrong", MessageBoxButton.OK, MessageBoxImage.Error);
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
            var card = new FolderCardViewModel(folder, snapshot.Status.ForFolder(folder.Name));
            Folders.Add(card);
            _ = card.LoadThumbnailAsync(_services.Thumbnails, tile);
        }

        StatusText = $"{_services.GamePath}   |   {snapshot.Tree.Folders.Count} folders, {snapshot.Packs.Count} packs";
    }
}
