using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using BhMaps.App.Services;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Packs;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BhMaps.App.ViewModels;

/// <summary>One pack the import can read from, with the counts line the pack chooser shows beside a pack.</summary>
public sealed record PackSourceRow(Pack Pack, string Label);

/// <summary>One tickable line of the import window: a map of the source pack, or one of its loose Backgrounds
/// pictures. A row the target already has starts unticked and stays disabled until Replace goes on.</summary>
public sealed partial class PackImportRowViewModel : ObservableObject
{
    public PackImportRowViewModel(
        MapEntry? map, string? looseFile, string name, string detail, bool alreadyHere, string picturePath)
    {
        Map = map;
        LooseFile = looseFile;
        Name = name;
        Detail = detail;
        AlreadyHere = alreadyHere;
        PicturePath = picturePath;
    }

    public MapEntry? Map { get; }

    /// <summary>Pack-relative, for a Pictures row. Null on a map row.</summary>
    public string? LooseFile { get; }

    public string Name { get; }

    /// <summary>"{k} files" or the file name, and "already here" for a row the target holds.</summary>
    public string Detail { get; }

    public bool AlreadyHere { get; }

    /// <summary>The picture the thumbnail is decoded from, or "" when the row has none.</summary>
    public string PicturePath { get; }

    [ObservableProperty]
    public partial bool IsTicked { get; set; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; set; }
}

/// <summary>Spec 2.6 5.2: one pack's maps and pictures chosen for another pack. It writes nothing: it hands the
/// shell a <see cref="PackImportPlan" /> and the shell runs it through the library write boundary.</summary>
public sealed partial class ImportFromPackViewModel : ObservableObject
{
    private readonly MainViewModel _shell;
    private readonly Pack _target;
    private CancellationTokenSource _thumbnails = new();
    private string _progress = "";

    public ImportFromPackViewModel(MainViewModel shell, Pack target, IReadOnlyList<Pack> sources)
    {
        _shell = shell;
        _target = target;
        Title = $"Import into {target.Name}";
        Subtitle = "Maps and pictures are copied into this pack. Nothing is written to the game.";
        ReplaceText = $"Replace maps {target.Name} already has";
        Sources = [.. sources.Select(SourceRow)];
        Rows = [];
        LooseRows = [];

        // Order matters: the Source setter runs OnSourceChanged during construction and BuildRows reads Rows,
        // LooseRows, AllMaps and Replace, so every one of them is in place before Source is set.
        AllMaps = true;
        Source = Sources.FirstOrDefault();
    }

    public string Title { get; }

    public string Subtitle { get; }

    /// <summary>The checkbox's label, which names the pack being written into.</summary>
    public string ReplaceText { get; }

    public ObservableCollection<PackSourceRow> Sources { get; }

    [ObservableProperty]
    public partial PackSourceRow? Source { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChooseMaps))]
    public partial bool AllMaps { get; set; }

    [ObservableProperty]
    public partial bool Replace { get; set; }

    /// <summary>The tick list is up only when the second radio is, and the markup has no inverting converter.</summary>
    public bool ChooseMaps => !AllMaps;

    /// <summary>The source's maps, in catalog order.</summary>
    public ObservableCollection<PackImportRowViewModel> Rows { get; }

    /// <summary>The source's loose Backgrounds pictures, which belong to no map (spec 5.2).</summary>
    public ObservableCollection<PackImportRowViewModel> LooseRows { get; }

    public bool HasLooseRows => LooseRows.Count > 0;

    public string AllMapsText => $"All {MainViewModel.Count(Rows.Count, "map")}";

    /// <summary>Spec 5.2: "Import 3 maps", "Import 1 map", "Nothing to import" when none is ticked, and the
    /// running count while it imports.</summary>
    public string PrimaryText =>
        _progress.Length > 0 ? _progress
        : Importable == 0 ? "Nothing to import"
        : $"Import {MainViewModel.Count(Importable, "map")}";

    public bool CanImport => Importable > 0 && _progress.Length == 0;

    private IEnumerable<PackImportRowViewModel> AllRows => Rows.Concat(LooseRows);

    private int Importable => AllMaps
        ? AllRows.Count(r => !r.AlreadyHere || Replace)
        : AllRows.Count(r => r.IsTicked && (!r.AlreadyHere || Replace));

    /// <summary>The plan the window's primary button hands back, in row order.</summary>
    public PackImportPlan? BuildPlan()
    {
        if (Source is not { } source)
        {
            return null;
        }

        var chosen = AllRows.Where(r => (AllMaps || r.IsTicked) && (!r.AlreadyHere || Replace)).ToList();
        return new PackImportPlan(
            source.Pack,
            _target,
            [.. chosen.Where(r => r.Map is not null).Select(r => r.Map!)],
            [.. chosen.Where(r => r.LooseFile is not null).Select(r => r.LooseFile!)],
            Replace);
    }

    /// <summary>The line the primary button shows while the import runs, in place of its count.</summary>
    public void SetProgress(string text)
    {
        _progress = text;
        OnPropertyChanged(nameof(PrimaryText));
        OnPropertyChanged(nameof(CanImport));
    }

    /// <summary>The window is closing, so the decodes it started stop where they are.</summary>
    public void Cleanup() => _thumbnails.Cancel();

    /// <summary>Spec 2.6 5.2: the source's maps and its loose Backgrounds files, ticked as the dialog's rules
    /// say. Rows the target already has start unticked, and stay disabled until Replace goes on.</summary>
    private void BuildRows()
    {
        _thumbnails.Cancel();
        _thumbnails = new CancellationTokenSource();
        Rows.Clear();
        LooseRows.Clear();
        if (Source is not { } source || _shell.Snapshot is not { } snapshot)
        {
            Rebuilt();
            return;
        }

        var catalog = snapshot.Catalog;
        foreach (var map in catalog.Maps.Where(m => source.Pack.FindFolder(m.FolderName) is { Files.Count: > 0 }))
        {
            var files = PackCopier.MapFiles(source.Pack, map, catalog);
            var here = _target.FindFolder(map.FolderName) is { Files.Count: > 0 };
            Add(Rows, new PackImportRowViewModel(
                map,
                null,
                map.DisplayName,
                here ? "already here" : $"{files.Count} files",
                here,
                Picture(source.Pack, files))
            {
                IsTicked = !here,
                IsEnabled = !here || Replace,
            });
        }

        foreach (var file in source.Pack.FindFolder(PackCopier.BackgroundsFolder)?.Files ?? Array.Empty<GameFile>())
        {
            if (!Path.GetExtension(file.Name).Equals(PackCopier.BackgroundExtension, StringComparison.OrdinalIgnoreCase)
                || PackCopier.MapForSlot(catalog, file.Name) is not null)
            {
                continue;
            }

            var relative = Path.Combine(PackCopier.BackgroundsFolder, file.Name);
            var here = File.Exists(Path.Combine(_target.FullPath, relative));
            Add(LooseRows, new PackImportRowViewModel(
                null, relative, file.Name, here ? "already here" : file.Name, here, file.FullPath)
            {
                IsTicked = !here,
                IsEnabled = !here || Replace,
            });
        }

        Rebuilt();
        _ = LoadThumbnailsAsync(_shell.Services, _thumbnails.Token);
    }

    /// <summary>A row is the only thing that knows it was ticked, and the primary button's count is built from
    /// every row, so the count is raised again whenever one of them changes.</summary>
    private void Add(ObservableCollection<PackImportRowViewModel> rows, PackImportRowViewModel row)
    {
        row.PropertyChanged += OnRowChanged;
        rows.Add(row);
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PackImportRowViewModel.IsTicked))
        {
            OnPropertyChanged(nameof(PrimaryText));
            OnPropertyChanged(nameof(CanImport));
        }
    }

    /// <summary>Every property the two lists feed, raised in one place after a rebuild.</summary>
    private void Rebuilt()
    {
        OnPropertyChanged(nameof(AllMapsText));
        OnPropertyChanged(nameof(HasLooseRows));
        OnPropertyChanged(nameof(PrimaryText));
        OnPropertyChanged(nameof(CanImport));
    }

    /// <summary>A map row's thumbnail is the picture the source pack ships for it, when it ships one.</summary>
    private static string Picture(Pack source, IReadOnlyList<string> files)
    {
        var background = files.FirstOrDefault(
            f => Path.GetExtension(f).Equals(PackCopier.BackgroundExtension, StringComparison.OrdinalIgnoreCase));
        return background is null ? "" : Path.Combine(source.FullPath, background);
    }

    /// <summary>Every decode is off the UI thread and through the shared cache, as the chooser's rows are.</summary>
    private async Task LoadThumbnailsAsync(AppServices services, CancellationToken ct)
    {
        foreach (var row in AllRows.Where(r => r.PicturePath.Length > 0).ToList())
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var mtime = await Task.Run(() => File.GetLastWriteTimeUtc(row.PicturePath).Ticks, ct);
                if (await services.Thumbnails.GetAsync(row.PicturePath, mtime, ct) is { } image)
                {
                    row.Thumbnail = image;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A row whose file vanished still imports; a blank thumbnail is not worth a dialog.
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>The counts line the pack chooser shows, built the same way (spec 4.3).</summary>
    private PackSourceRow SourceRow(Pack pack)
    {
        var maps = _shell.Snapshot is { } snapshot
            ? snapshot.Catalog.Maps.Count(m => pack.FindFolder(m.FolderName) is { Files.Count: > 0 })
            : 0;
        var backgrounds = pack.FindFolder(PackCopier.BackgroundsFolder)?.Files.Count ?? 0;
        return new PackSourceRow(
            pack,
            $"{pack.Name}  {MainViewModel.Count(maps, "map")}, {MainViewModel.Count(backgrounds, "background")}");
    }

    partial void OnSourceChanged(PackSourceRow? value) => BuildRows();

    partial void OnReplaceChanged(bool value)
    {
        foreach (var row in AllRows)
        {
            row.IsEnabled = !row.AlreadyHere || value;
        }

        OnPropertyChanged(nameof(PrimaryText));
        OnPropertyChanged(nameof(CanImport));
    }

    partial void OnAllMapsChanged(bool value)
    {
        OnPropertyChanged(nameof(PrimaryText));
        OnPropertyChanged(nameof(CanImport));
    }
}
