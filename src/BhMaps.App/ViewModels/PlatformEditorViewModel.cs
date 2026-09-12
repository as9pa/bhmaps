using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BhMaps.App.Services;
using BhMaps.Core.Imaging;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using BhMaps.Core.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>What a set tile's Edit hands the editor: the map whose own pieces are recoloured, the pack the
/// set came from, or null for the set the game is showing (spec 6), and the relative path of the one piece a
/// panel row asked for, or null for the whole set. Only that one piece opens ticked (spec 7).</summary>
public sealed record PlatformEditorRequest(MapEntry Map, Pack? Pack, string? OnlyFile = null);

/// <summary>What one Save left in the library: the pack it was saved into, which is the pack the shell stamps
/// when it applies it, the map folder inside that pack, and whether the user asked for it to go into the game as
/// well. The shell does that part, so the editor never writes into the game folder.</summary>
public sealed record PlatformSave(string PackName, string SetFolder, bool ApplyToGame);

/// <summary>Spec 6: one map's own platform pieces, faded and recoloured by one number each. Every slider change
/// writes the processed pieces into a temp set and composes the map from it, so the preview is the same render
/// the panel and the rows draw, over the background the game is showing for that map today.</summary>
public partial class PlatformEditorViewModel : ObservableObject
{
    public const string NoPackText = "In game";
    public const string NoFilesText = "This map has no platform art of its own.";
    public const string MixedText = "Mixed";
    public const string TickHintText = "Tick a file to edit it.";

    /// <summary>The widest a row's thumbnail is ever drawn, so a fitted picture is scaled once rather than per
    /// frame the list draws.</summary>
    private const double ThumbnailWidth = 240.0;

    /// <summary>Long enough that a dragged thumb is not one render per pixel, short enough that the preview
    /// still follows the thumb. Every piece is decoded and re-encoded per render, so this is slower work than
    /// the background editor's one bitmap (spec 6).</summary>
    private static readonly TimeSpan PreviewInterval = TimeSpan.FromMilliseconds(60);

    private readonly AppServices _services;
    private readonly IDialogs _dialogs;
    private readonly PlatformEditorRequest _request;
    private readonly string _tempRoot;
    private readonly string? _backgroundPath;
    private readonly Throttler _preview = new(PreviewInterval);
    private readonly List<(string Path, string PackName)> _workingCopies = [];

    private long _sequence;

    /// <summary>True while the shared sliders are writing the ticked rows, or the rows are writing the sliders,
    /// so one value change is one fan-out rather than a loop between the two (spec 4).</summary>
    private bool _syncing;

    public PlatformEditorViewModel(
        AppServices services,
        IDialogs dialogs,
        IReadOnlyList<string> packNames,
        PlatformEditorRequest request)
    {
        _services = services;
        _dialogs = dialogs;
        _request = request;
        _tempRoot = Path.Combine(Path.GetTempPath(), "BhMaps", "platform-editor", Guid.NewGuid().ToString("N"));
        Pieces = BuildPieces(services, request);
        foreach (var row in Pieces)
        {
            row.PropertyChanged += OnRowPropertyChanged;
        }

        _backgroundPath = CurrentBackground(services, request.Map);
        PackChoices = packNames.Concat([BackgroundEditorViewModel.NewPackChoice]).ToList();
        Error = "";
        ApplyNow = true;

        // The background editor's rule: the pack the user would mean is there, or it is a new pack already named.
        var existingDefault = packNames.FirstOrDefault(
            p => p.Equals(BackgroundEditorViewModel.DefaultPackName, StringComparison.OrdinalIgnoreCase));
        TargetPack = existingDefault ?? BackgroundEditorViewModel.NewPackChoice;
        NewPackName = existingDefault is null ? BackgroundEditorViewModel.DefaultPackName : "";

        Opacity = 100;
        Hue = 0;
        SchedulePreview();
    }

    public event Action<bool>? CloseRequested;

    public IReadOnlyList<string> PackChoices { get; }

    /// <summary>One row per piece of the set, in the order the file list draws them (spec 3).</summary>
    public IReadOnlyList<PlatformPieceViewModel> Pieces { get; }

    /// <summary>The files the editor is holding open for another program to edit, with the pack each one sits in.
    /// Empty until the working-copy commands land.</summary>
    public IReadOnlyList<(string Path, string PackName)> WorkingCopies => _workingCopies;

    /// <summary>What Save wrote into the library, or null while nothing has been saved.</summary>
    public PlatformSave? Saved { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpacityText))]
    public partial int Opacity { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HueText))]
    public partial int Hue { get; set; }

    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewPack), nameof(PackNameError), nameof(CanSave), nameof(CanEditOutside))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string TargetPack { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackNameError), nameof(CanSave), nameof(CanEditOutside))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string NewPackName { get; set; }

    [ObservableProperty]
    public partial bool ApplyNow { get; set; }

    [ObservableProperty]
    public partial string Error { get; set; }

    /// <summary>Why the picture the user picked could not be read, or empty (spec 5).</summary>
    [ObservableProperty]
    public partial string ImageError { get; set; } = "";

    public string Title => $"Edit platforms, {_request.Map.DisplayName}";

    /// <summary>The set Edit was pressed on: a pack's, or the one the game is showing (spec 6).</summary>
    public string SourceText =>
        $"{_request.Pack?.Name ?? NoPackText}, {MainViewModel.Count(Pieces.Count, "file")}";

    /// <summary>False for a map with no pieces of its own: there is nothing for a slider to move (spec 14).</summary>
    public bool CanEdit => Pieces.Count > 0;

    public string FilesHeader => $"Files, {TickedCount} of {Pieces.Count}";

    public int TickedCount => Pieces.Count(p => p.IsTicked);

    public bool HasTicked => TickedCount > 0;

    /// <summary>The values only move the ticked rows, so there is nothing to adjust while none is ticked.</summary>
    public bool CanAdjust => HasTicked;

    public bool CanAll => TickedCount < Pieces.Count;

    public bool CanNone => TickedCount > 0;

    /// <summary>"Mixed" is the honest reading when the ticked rows do not agree; the slider still shows one of
    /// them, and moving it puts them all on that value (spec 4).</summary>
    public string OpacityText => IsMixed(p => p.Opacity) ? MixedText : $"{Opacity}%";

    /// <summary>The sign is part of the reading: "+140" is a turn one way and "-30" the other, and "0" is neither.</summary>
    public string HueText =>
        IsMixed(p => p.Hue) ? MixedText : Hue > 0 ? $"+{Hue}" : Hue.ToString();

    /// <summary>The Image value line: one reading for the ticked rows when they agree, "Mixed" when they do not,
    /// and the hint while nothing is ticked (spec 4).</summary>
    public string ImageText
    {
        get
        {
            if (!HasTicked)
            {
                return TickHintText;
            }

            var ticked = Pieces.Where(p => p.IsTicked).ToList();
            if (ticked.All(p => p.Art == PieceArt.Replacement)
                && ticked.Select(p => p.ReplacementName).Distinct(StringComparer.Ordinal).Count() == 1)
            {
                // One picture fitted to pieces of different shapes has no one size to read out.
                return ticked.All(p => p.Width == ticked[0].Width && p.Height == ticked[0].Height)
                    ? ticked[0].ImageText
                    : $"{ticked[0].ReplacementName}, fitted to each piece";
            }

            var first = ticked[0].ImageText;
            return ticked.All(p => p.ImageText == first) ? first : MixedText;
        }
    }

    /// <summary>The Image line is a hint rather than a value while nothing is ticked, and the window draws it
    /// differently.</summary>
    public bool ImageHintVisible => !HasTicked;

    public bool CanUseImage => HasTicked;

    public bool CanResetImage => Pieces.Any(p => p.IsTicked && p.Art != PieceArt.Original);

    /// <summary>Editing outside writes a working copy into the pack, so it needs a pack name that is good enough
    /// to save into.</summary>
    public bool CanEditOutside => HasTicked && PackNameError.Length == 0;

    /// <summary>What the preview says instead of a picture, which is only ever the map having no art of its own.</summary>
    public string EmptyText => CanEdit ? "" : NoFilesText;

    public bool IsNewPack => TargetPack == BackgroundEditorViewModel.NewPackChoice;

    public string EffectivePackName => IsNewPack ? NewPackName.Trim() : TargetPack;

    public string PackNameError =>
        IsNewPack && !PackNameValidator.IsValid(EffectivePackName, out var error) ? error : "";

    public bool CanSave => CanEdit && PackNameError.Length == 0;

    /// <summary>Drops the temp set the previews were drawn from, whichever button closed the window. A file the
    /// render still holds open is not worth a dialog: the folder is under %TEMP% and Windows clears it.</summary>
    public void Cleanup()
    {
        _preview.Cancel();
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The temp set outlives the window rather than the window outliving the user's patience.
        }
    }

    /// <summary>The render a drag ends with: the throttle can only have dropped values that are stale by now, so
    /// the newest ones are drawn with nothing queued behind them.</summary>
    public void RenderFinal()
    {
        _preview.Cancel();
        SchedulePreview();
    }

    /// <summary>What Task 4's watcher calls when a working copy changed on disk: the same render the sliders
    /// ask for.</summary>
    public void RenderNow() => SchedulePreview();

    /// <summary>The rows the window drew nothing for yet, one at a time so a set of forty does not decode forty
    /// files at once.</summary>
    public async Task LoadThumbnailsAsync(CancellationToken ct)
    {
        foreach (var row in Pieces)
        {
            ct.ThrowIfCancellationRequested();
            await RefreshThumbnailAsync(row, ct);
        }
    }

    /// <summary>One row's thumbnail: the fitted picture itself for a replacement, the shared cache for a file.</summary>
    public async Task RefreshThumbnailAsync(PlatformPieceViewModel row, CancellationToken ct = default)
    {
        if (row.Replacement is { } fitted)
        {
            row.Thumbnail = ThumbnailOf(fitted);
            return;
        }

        var path = row.SourcePath;
        try
        {
            var mtimeTicks = await Task.Run(() => File.GetLastWriteTimeUtc(path).Ticks, ct);
            row.Thumbnail = await _services.Thumbnails.GetAsync(path, mtimeTicks, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            row.Thumbnail = null;
        }
    }

    /// <summary>The sliders move every ticked row, and a row moved on its own moves nothing else (spec 4).</summary>
    partial void OnOpacityChanged(int value)
    {
        if (_syncing)
        {
            return;
        }

        FanOut(row => row.Opacity = value);
        SchedulePreview();
    }

    partial void OnHueChanged(int value)
    {
        if (_syncing)
        {
            return;
        }

        FanOut(row => row.Hue = value);
        SchedulePreview();
    }

    /// <summary>Where each piece is read from: the pack's copy when the pack has one that draws something, the
    /// game's otherwise, which is the rule every composite already resolves by (spec 6).</summary>
    private static IReadOnlyList<PlatformPieceViewModel> BuildPieces(AppServices services, PlatformEditorRequest request)
    {
        var sources = new AssetSources(services.GamePath, request.Pack?.FullPath);
        var pieces = new List<PlatformPieceViewModel>();
        foreach (var relativePath in request.Map.PlatformFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (sources.ResolveAsset(relativePath) is { } source)
            {
                pieces.Add(new PlatformPieceViewModel(
                    relativePath,
                    source,
                    ticked: request.OnlyFile is null
                        || string.Equals(relativePath, request.OnlyFile, StringComparison.OrdinalIgnoreCase)));
            }
        }

        return pieces;
    }

    /// <summary>A fitted picture drawn at list size, frozen; a piece no wider than a thumbnail is its own.</summary>
    private static ImageSource ThumbnailOf(BitmapSource fitted)
    {
        if (fitted.PixelWidth <= ThumbnailWidth)
        {
            return fitted;
        }

        var scale = ThumbnailWidth / fitted.PixelWidth;
        var scaled = new TransformedBitmap(fitted, new ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }

    /// <summary>The background the game is showing for the map today, so the preview sits over what the user sees
    /// rather than over the tile colour. Null for a map whose slot the game folder has nothing for.</summary>
    private static string? CurrentBackground(AppServices services, MapEntry map)
    {
        if (map.BackgroundSlots.Count == 0)
        {
            return null;
        }

        var path = Path.Combine(services.GamePath, AssetPath.Background(map.BackgroundSlots[0]));
        return File.Exists(path) ? path : null;
    }

    /// <summary>Setting the shared value fans out on its own, but only when it moved: a Mixed set whose first row
    /// already sits at the default would keep the rest of the set where it is (spec 4).</summary>
    [RelayCommand]
    private void ResetOpacity()
    {
        Opacity = PlatformPieceViewModel.DefaultOpacity;
        FanOut(row => row.Opacity = PlatformPieceViewModel.DefaultOpacity);
        SchedulePreview();
    }

    [RelayCommand]
    private void ResetHue()
    {
        Hue = PlatformPieceViewModel.DefaultHue;
        FanOut(row => row.Hue = PlatformPieceViewModel.DefaultHue);
        SchedulePreview();
    }

    [RelayCommand(CanExecute = nameof(CanAll))]
    private void All()
    {
        foreach (var row in Pieces)
        {
            row.IsTicked = true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanNone))]
    private void None()
    {
        foreach (var row in Pieces)
        {
            row.IsTicked = false;
        }
    }

    /// <summary>Spec 5: one picture, fitted to each ticked piece's own shape and held in memory until Save. The
    /// fit is the slow part, so it happens off the UI thread and no row changes until all of them have one.</summary>
    [RelayCommand(CanExecute = nameof(CanUseImage))]
    private async Task ReplaceAsync()
    {
        var ticked = Pieces.Where(p => p.IsTicked).ToList();
        var title = ticked.Count == 1
            ? $"Replace {ticked[0].FileName}"
            : $"Replace {MainViewModel.Count(ticked.Count, "file")}";
        if (_dialogs.PickImageFile(title) is not { } path)
        {
            return;
        }

        Dictionary<PlatformPieceViewModel, BitmapSource> fitted;
        try
        {
            fitted = await Task.Run(() =>
            {
                var source = BackgroundFitter.LoadSource(path);
                var results = new Dictionary<PlatformPieceViewModel, BitmapSource>();
                foreach (var row in ticked)
                {
                    var piece = BackgroundFitter.LoadSource(row.SourcePath);
                    results[row] = PieceFitter.Fit(source, piece);
                }

                return results;
            });
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            ImageError = $"Could not read {Path.GetFileName(path)}. Use a PNG, JPG, BMP, GIF or WebP.";
            return;
        }

        ImageError = "";
        var name = Path.GetFileName(path);
        foreach (var row in ticked)
        {
            row.SetReplacement(fitted[row], name);
            row.Thumbnail = ThumbnailOf(fitted[row]);
        }

        OnImageChanged();
    }

    /// <summary>Back to the pieces' own art. Task 4 asks the user first when a ticked row holds a working copy,
    /// because that gives up a file another program may still have open.</summary>
    [RelayCommand(CanExecute = nameof(CanResetImage))]
    private async Task ResetImageAsync()
    {
        foreach (var row in Pieces.Where(p => p.IsTicked && p.Art != PieceArt.Original).ToList())
        {
            row.ResetArt();
            await RefreshThumbnailAsync(row);
        }

        ImageError = "";
        OnImageChanged();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlatformPieceViewModel.IsTicked):
                OnTickedChanged();
                break;
            case nameof(PlatformPieceViewModel.Opacity):
            case nameof(PlatformPieceViewModel.Hue):
                if (!_syncing)
                {
                    OnRowValuesChanged();
                }

                break;
        }
    }

    /// <summary>What ticking changes is what the sliders and the Image line are reading, not the map: the shared
    /// values come from the first ticked row, and nothing is rendered again (spec 4).</summary>
    private void OnTickedChanged()
    {
        _syncing = true;
        if (Pieces.FirstOrDefault(p => p.IsTicked) is { } first)
        {
            Opacity = first.Opacity;
            Hue = first.Hue;
        }

        _syncing = false;
        OnPropertyChanged(nameof(FilesHeader));
        OnPropertyChanged(nameof(TickedCount));
        OnPropertyChanged(nameof(HasTicked));
        OnPropertyChanged(nameof(CanAdjust));
        OnPropertyChanged(nameof(OpacityText));
        OnPropertyChanged(nameof(HueText));
        OnPropertyChanged(nameof(ImageText));
        OnPropertyChanged(nameof(ImageHintVisible));
        OnPropertyChanged(nameof(CanUseImage));
        OnPropertyChanged(nameof(CanResetImage));
        OnPropertyChanged(nameof(CanEditOutside));
        AllCommand.NotifyCanExecuteChanged();
        NoneCommand.NotifyCanExecuteChanged();
        ReplaceCommand.NotifyCanExecuteChanged();
        ResetImageCommand.NotifyCanExecuteChanged();
    }

    /// <summary>A row moved on its own: the shared readings may have gone Mixed, and the map did change.</summary>
    private void OnRowValuesChanged()
    {
        OnPropertyChanged(nameof(OpacityText));
        OnPropertyChanged(nameof(HueText));
        SchedulePreview();
    }

    /// <summary>What a Replace or a Reset image changed: the value line, the Reset button, and the map.</summary>
    private void OnImageChanged()
    {
        OnPropertyChanged(nameof(ImageText));
        OnPropertyChanged(nameof(CanResetImage));
        ResetImageCommand.NotifyCanExecuteChanged();
        SchedulePreview();
    }

    private void FanOut(Action<PlatformPieceViewModel> set)
    {
        _syncing = true;
        foreach (var row in Pieces.Where(p => p.IsTicked))
        {
            set(row);
        }

        _syncing = false;
    }

    private bool IsMixed(Func<PlatformPieceViewModel, int> value) =>
        Pieces.Where(p => p.IsTicked).Select(value).Distinct().Count() > 1;

    /// <summary>Saves the recoloured set into the pack and nothing else; the "Apply to game now" box is the
    /// shell's business, because a game write needs the boundary, the snapshot and the undo (spec 8).</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var packRoot = Path.Combine(PackScanner.PacksRoot(_services.LibraryPath), EffectivePackName);
        var destination = Path.Combine(packRoot, _request.Map.FolderName);
        if (Directory.Exists(destination)
            && Directory.EnumerateFiles(destination).Any(f => !IsWorkingCopy(f))
            && !_dialogs.Confirm(
                "Replace platforms?",
                $"{EffectivePackName} already has platforms for {_request.Map.DisplayName}. Replace them?"))
        {
            return;
        }

        // Every row, ticked or not: what Save leaves behind is the whole set, not the part being worked on.
        var rows = Pieces;
        try
        {
            await Task.Run(() =>
            {
                foreach (var row in rows)
                {
                    row.CopyOrWriteResult(Path.Combine(packRoot, row.RelativePath));
                }
            });
            Saved = new PlatformSave(EffectivePackName, destination, ApplyNow);
            CloseRequested?.Invoke(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            _dialogs.Error("Could not save the platforms", ex.Message);
        }
    }

    /// <summary>Spec 6: 60 ms between renders, newest values win. The stamp is what makes the second half of that
    /// true, because a render already on the queue still finishes after a newer one has been asked for.</summary>
    private void SchedulePreview()
    {
        var stamp = ++_sequence;
        _preview.Run(ct => RenderAsync(stamp, ct));
    }

    /// <summary>Whether a file in the destination is one this editor put there for another program to edit, which
    /// is not the pack's own work and so is not what the Replace question is about.</summary>
    private bool IsWorkingCopy(string fullPath) =>
        _workingCopies.Any(w => string.Equals(
            Path.GetFullPath(w.Path), Path.GetFullPath(fullPath), StringComparison.OrdinalIgnoreCase));

    /// <summary>The pieces are processed into the temp set and the map is composed from it, so the preview is the
    /// real composite rather than a drawing of one. The temp set is the map art root of the render: a piece faded
    /// to nothing has to draw nothing, and a pack file that is fully transparent falls back to the game's.</summary>
    private async Task RenderAsync(long stamp, CancellationToken ct)
    {
        var rows = Pieces;
        var root = _tempRoot;
        var level = _request.Map.BaseLevel;
        var background = _backgroundPath;
        try
        {
            await Task.Run(
                () =>
                {
                    foreach (var row in rows)
                    {
                        ct.ThrowIfCancellationRequested();
                        row.CopyOrWriteResult(Path.Combine(root, row.RelativePath));
                    }
                },
                ct);

            var sources = new AssetSources(root, backgroundOverride: background);
            var bitmap = await _services.Renderer.RunAsync(
                () => MapCompositor.Render(level, MapCompositor.PanelWidth, MapCompositor.PanelHeight, sources), ct);
            if (ct.IsCancellationRequested || stamp != _sequence)
            {
                return;
            }

            Preview = bitmap;
            Error = "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
        {
            if (!ct.IsCancellationRequested)
            {
                Error = "Could not read the platform art: " + ex.Message;
            }
        }
    }
}
