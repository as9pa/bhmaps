using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Media;
using BhMaps.App.Services;
using BhMaps.Core.Imaging;
using BhMaps.Core.Operations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>One picked picture. The list shows the name; the import needs the path.</summary>
public sealed record PictureFileViewModel(string FullPath)
{
    public string FileName { get; } = Path.GetFileName(FullPath);
}

/// <summary>Spec 6.8: any number of dropped or picked pictures, one fit mode for the batch, a target pack, and
/// optionally an apply to the maps the caller named. The dialog only collects the answers; the shell runs the
/// import and the apply, because both belong to its busy boundary.</summary>
public partial class AddPicturesViewModel : ObservableObject
{
    public const int PreviewWidth = 320;
    public const int PreviewHeight = 180;
    public const string NewPackChoice = BackgroundEditorViewModel.NewPackChoice;

    /// <summary>What the file picker offers, so dropping and picking agree on what an image is. Wider than
    /// ImageFiles.IsImage, which answers a different question: what counts as map art already inside a pack.</summary>
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".webp"];

    private readonly IDialogs _dialogs;
    private readonly Debouncer _preview = new();

    public AddPicturesViewModel(IDialogs dialogs, IReadOnlyList<string> packNames, int targetMapCount)
    {
        _dialogs = dialogs;
        Files = [];
        Files.CollectionChanged += OnFilesChanged;
        PackChoices = packNames.Concat([NewPackChoice]).ToList();
        TargetMapCount = targetMapCount;
        Fit = PictureFit.Fill;
        Error = "";

        // The same default the background editor picks, and the same reason: a pack of one's own rather than
        // Default, which is the baseline Reset puts back (spec 6.1).
        var existingDefault = packNames.FirstOrDefault(p =>
            p.Equals(BackgroundEditorViewModel.DefaultPackName, StringComparison.OrdinalIgnoreCase));
        TargetPack = existingDefault ?? NewPackChoice;
        NewPackName = existingDefault is null ? BackgroundEditorViewModel.DefaultPackName : "";
    }

    public event Action<bool>? CloseRequested;

    /// <summary>Every pack in the library plus the "New pack..." entry that reveals the name field.</summary>
    public IReadOnlyList<string> PackChoices { get; }

    /// <summary>The pictures to import, in the order they were dropped or picked.</summary>
    public ObservableCollection<PictureFileViewModel> Files { get; }

    /// <summary>How many maps the "Also apply to" checkbox would write to. Zero disables it.</summary>
    public int TargetMapCount { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FitStretch), nameof(FitCenter), nameof(FitFill), nameof(FitFit))]
    public partial PictureFit Fit { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewPack), nameof(PackNameError), nameof(CanAdd))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string TargetPack { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackNameError), nameof(CanAdd))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string NewPackName { get; set; }

    [ObservableProperty]
    public partial bool ApplyToMaps { get; set; }

    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }

    [ObservableProperty]
    public partial string Error { get; set; }

    /// <summary>The checkbox's line, with the count of maps it would write to (spec 6.8).</summary>
    public string ApplyToMapsLabel => TargetMapCount switch
    {
        0 => "Also apply to the selected maps",
        1 => "Also apply to the selected maps (1 map)",
        _ => $"Also apply to the selected maps ({TargetMapCount} maps)",
    };

    public bool CanApplyToMaps => TargetMapCount > 0;

    public bool IsNewPack => TargetPack == NewPackChoice;

    public string EffectivePackName => IsNewPack ? NewPackName.Trim() : TargetPack;

    /// <summary>Only a new name can be wrong: a name in the list is a folder that already exists.</summary>
    public string PackNameError =>
        IsNewPack && !PackNameValidator.IsValid(EffectivePackName, out var error) ? error : "";

    public bool CanAdd => Files.Count > 0 && PackNameError.Length == 0;

    public bool FitStretch
    {
        get => Fit == PictureFit.Stretch;
        set
        {
            if (value)
            {
                Fit = PictureFit.Stretch;
            }
        }
    }

    public bool FitCenter
    {
        get => Fit == PictureFit.Center;
        set
        {
            if (value)
            {
                Fit = PictureFit.Center;
            }
        }
    }

    public bool FitFill
    {
        get => Fit == PictureFit.Fill;
        set
        {
            if (value)
            {
                Fit = PictureFit.Fill;
            }
        }
    }

    public bool FitFit
    {
        get => Fit == PictureFit.Fit;
        set
        {
            if (value)
            {
                Fit = PictureFit.Fit;
            }
        }
    }

    /// <summary>A drop on the window. Folders and anything that is not an image are ignored rather than refused:
    /// dropping a folder of pictures with a readme in it should still add the pictures.</summary>
    public void AcceptDroppedFiles(IEnumerable<string> paths) =>
        AddFiles(paths.Where(p => IsImage(p) && File.Exists(p)));

    partial void OnFitChanged(PictureFit value) => SchedulePreview();

    [RelayCommand]
    private void PickFiles()
    {
        if (_dialogs.PickImageFiles("Choose pictures") is { } picked)
        {
            AddFiles(picked);
        }
    }

    [RelayCommand]
    private void Remove(PictureFileViewModel? file)
    {
        if (file is not null)
        {
            Files.Remove(file);
        }
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add() => CloseRequested?.Invoke(true);

    private static bool IsImage(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>The preview is the real 2048x1151 fit shrunk to the box, not a fit computed at box size: Center
    /// leaves a small picture at 1:1 (decision D9), so how much of the canvas it covers depends on the canvas.</summary>
    private static ImageSource RenderPreview(string path, FitOptions options)
    {
        var fitted = BackgroundFitter.Render(path, options, BackgroundFitter.OutputWidth, BackgroundFitter.OutputHeight);
        return BackgroundFitter.Render(fitted, new FitOptions(FitMode.Stretch), PreviewWidth, PreviewHeight);
    }

    /// <summary>A file already in the list is not added twice: the second copy would import again and take the
    /// importer's silent " (2)" suffix for a picture the user only meant once.</summary>
    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!Files.Any(f => f.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                Files.Add(new PictureFileViewModel(path));
            }
        }
    }

    private void OnFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanAdd));
        AddCommand.NotifyCanExecuteChanged();

        // The preview is of the first picture, so any change to the list can change it.
        SchedulePreview();
    }

    /// <summary>Spec 5.3: debounce 150 ms, render off the UI thread, latest request wins. An empty list clears
    /// the preview at once rather than after the quiet period, because there is nothing to wait for.</summary>
    private void SchedulePreview()
    {
        var path = Files.FirstOrDefault()?.FullPath;
        var options = PictureImporter.ToFitOptions(Fit);
        if (path is null || !File.Exists(path))
        {
            _preview.Cancel();
            Preview = null;
            Error = "";
            return;
        }

        _ = _preview.RunAsync(async ct =>
        {
            try
            {
                var bitmap = await Task.Run(() => RenderPreview(path, options), ct);
                if (!ct.IsCancellationRequested)
                {
                    Preview = bitmap;
                    Error = "";
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
            {
                if (!ct.IsCancellationRequested)
                {
                    Preview = null;
                    Error = "Could not read the image: " + ex.Message;
                }
            }
        });
    }
}
