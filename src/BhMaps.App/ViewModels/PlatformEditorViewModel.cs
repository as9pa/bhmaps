using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BhMaps.App.Services;
using BhMaps.Core.Hashing;
using BhMaps.Core.Imaging;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Packs;
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

    /// <summary>Spec 3.2: Ticked only with nothing ticked ghosts the whole map, so the preview says what to do
    /// about it, in the same words TickHintText uses for the sliders.</summary>
    public const string IsolateHintText = "Tick a file to see it on its own.";

    /// <summary>The widest a row's thumbnail is ever drawn, so a fitted picture is scaled once rather than per
    /// frame the list draws.</summary>
    private const double ThumbnailWidth = 240.0;

    /// <summary>Long enough that a dragged thumb is not one render per pixel, short enough that the preview
    /// still follows the thumb. Every piece is decoded and re-encoded per render, so this is slower work than
    /// the background editor's one bitmap (spec 6).</summary>
    private static readonly TimeSpan PreviewInterval = TimeSpan.FromMilliseconds(60);

    /// <summary>The quiet period after a save in another program: a program that writes its file in several
    /// goes is one render, not one per write.</summary>
    private static readonly TimeSpan ChangedDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>How long the preview waits out a file another program is part way through writing before it
    /// gives up and says so: twelve tries at 250 ms is three seconds of a locked or half-written file.</summary>
    private const int MaxReadRetries = 12;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);

    private readonly AppServices _services;
    private readonly IDialogs _dialogs;
    private readonly PlatformEditorRequest _request;
    private readonly string _tempRoot;
    private readonly string? _backgroundPath;
    private readonly Throttler _preview = new(PreviewInterval);
    private readonly List<(string Path, string PackName)> _workingCopies = [];
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Debouncer _changed = new(ChangedDelay);

    private long _sequence;

    /// <summary>How many times running the preview has found a watched file unreadable since the last render
    /// that worked (spec 6).</summary>
    private int _retries;

    /// <summary>True while the shared sliders are writing the ticked rows, or the rows are writing the sliders,
    /// so one value change is one fan-out rather than a loop between the two (spec 4).</summary>
    private bool _syncing;

    /// <summary>True while the ctor is putting the remembered mode on, so opening the editor never writes the
    /// setting back and never schedules a second render (spec 3.5).</summary>
    private bool _restoringMode;

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

        // Spec 3.4: a panel row's Edit names one file, and that editor opens on it. Spec 3.5: every other way in
        // opens in the mode the last chip click left behind.
        _restoringMode = true;
        IsolatePreview = request.OnlyFile is not null || services.Settings.PlatformPreviewIsolate;
        _restoringMode = false;

        // The background editor's rule: the pack the user would mean is there, or it is a new pack already named.
        var existingDefault = packNames.FirstOrDefault(
            p => p.Equals(BackgroundEditorViewModel.DefaultPackName, StringComparison.OrdinalIgnoreCase));
        TargetPack = existingDefault ?? BackgroundEditorViewModel.NewPackChoice;
        NewPackName = existingDefault is null ? BackgroundEditorViewModel.DefaultPackName : "";

        Opacity = 100;
        Hue = 0;

        // The rows of a pack's set are read from a folder the user can change from outside the app, so it is
        // watched from the moment the editor opens rather than only once a working copy is written (ruling 9).
        if (request.Pack is { } sourcePack)
        {
            foreach (var row in Pieces.Where(p => IsUnder(p.OriginalPath, sourcePack.FullPath)))
            {
                if (Path.GetDirectoryName(row.OriginalPath) is { Length: > 0 } setFolder)
                {
                    WatchFolder(setFolder);
                }
            }
        }

        SchedulePreview();
    }

    public event Action<bool>? CloseRequested;

    /// <summary>The packs Save and "Edit in another app" can write into, "New pack..." last. Editing outside
    /// makes the new pack for real, so the list gains it at that point.</summary>
    public IReadOnlyList<string> PackChoices { get; private set; }

    /// <summary>One row per piece of the set, in the order the file list draws them (spec 3).</summary>
    public IReadOnlyList<PlatformPieceViewModel> Pieces { get; }

    /// <summary>The files the editor is holding open for another program to edit, with the pack each one sits in.
    /// They stay in the library whichever button closes the window, which is what the shell's Cancel line says
    /// (ruling 8).</summary>
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

    /// <summary>Spec 3.1: true is Ticked only, false is All pieces. Changing it schedules a render and writes the
    /// setting; nothing else is written anywhere.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAllPieces))]
    [NotifyPropertyChangedFor(nameof(IsolateHint))]
    public partial bool IsolatePreview { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewPack), nameof(PackNameError), nameof(CanSave), nameof(CanEditOutside))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(EditOutsideCommand))]
    public partial string TargetPack { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackNameError), nameof(CanSave), nameof(CanEditOutside))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(EditOutsideCommand))]
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

    /// <summary>The All pieces chip's side of the pair, so each chip binds IsSelected two way and the list's own
    /// single selection keeps exactly one of them on.</summary>
    public bool ShowAllPieces
    {
        get => !IsolatePreview;
        set => IsolatePreview = !value;
    }

    /// <summary>Spec 3.2: the scrim line under a fully ghosted preview, and null when there is nothing to say, so
    /// the window hides it with the same NullToVis converter the empty text already uses.</summary>
    public string? IsolateHint => IsolatePreview && Pieces.Count > 0 && TickedCount == 0 ? IsolateHintText : null;

    /// <summary>The values only move the ticked rows, so there is nothing to adjust while none is ticked.</summary>
    public bool CanAdjust => HasTicked;

    public bool CanAll => TickedCount < Pieces.Count;

    public bool CanNone => TickedCount > 0;

    /// <summary>"Mixed" is the honest reading when the ticked rows do not agree; the slider still shows one of
    /// them, and moving it puts them all on that value (spec 4). Blank while nothing is ticked, because the
    /// sliders are off and the Image line is already asking for a tick.</summary>
    public string OpacityText => !HasTicked ? "" : IsMixed(p => p.Opacity) ? MixedText : $"{Opacity}%";

    /// <summary>The sign is part of the reading: "+140" is a turn one way and "-30" the other, and "0" is neither.
    /// Blank while nothing is ticked, as the opacity reading is.</summary>
    public string HueText =>
        !HasTicked ? "" : IsMixed(p => p.Hue) ? MixedText : Hue > 0 ? $"+{Hue}" : Hue.ToString();

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
    /// render still holds open is not worth a dialog: the folder is under %TEMP% and Windows clears it. The
    /// working copies themselves stay where they are: they are the pack's files now (ruling 8).</summary>
    public void Cleanup()
    {
        foreach (var watcher in _watchers.Values)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
        _changed.Cancel();
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

    partial void OnIsolatePreviewChanged(bool value)
    {
        if (_restoringMode)
        {
            return;
        }

        // Spec 3.1: the chip re-renders through the existing 60 ms throttle. Spec 3.5: and is remembered.
        SchedulePreview();
        _services.UpdateSettings(_services.Settings with { PlatformPreviewIsolate = value });
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

    /// <summary>Spec 3.3: clicking a row's thumbnail or name ticks that row and unticks the rest. Each write goes
    /// through the row's own property, so the sliders, the Image line and the header follow exactly as they do
    /// for All and None.</summary>
    [RelayCommand]
    private void Solo(PlatformPieceViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        foreach (var other in Pieces)
        {
            other.IsTicked = ReferenceEquals(other, row);
        }
    }

    /// <summary>Spec 3.3: Ctrl+click adds a row to the ticks and clears nothing. Ticking a ticked row is a no-op
    /// rather than a toggle: the checkbox is where unticking lives.</summary>
    [RelayCommand]
    private void AddTick(PlatformPieceViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        row.IsTicked = true;
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
            row.SetReplacement(fitted[row], name, path);
            row.Thumbnail = ThumbnailOf(fitted[row]);
        }

        OnImageChanged();
    }

    /// <summary>Back to the pieces' own art. A ticked row holding a working copy is asked about first, because
    /// the reset writes over a file in the pack that another program may still have open (spec 6).</summary>
    [RelayCommand(CanExecute = nameof(CanResetImage))]
    private async Task ResetImageAsync()
    {
        var failed = false;
        var copies = Pieces.Where(p => p.IsTicked && p.Art == PieceArt.WorkingCopy).ToList();
        if (copies.Count > 0)
        {
            // One pack is a place the user can picture; copies spread over two is only "the library".
            var where = copies.Select(p => p.WorkingCopyPack).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
                ? copies[0].WorkingCopyPack
                : "the library";
            if (!_dialogs.Confirm(
                "Put the original art back?",
                copies.Count == 1
                    ? $"{copies[0].FileName} in {where} was changed in another app. Reset replaces it with the piece's original art."
                    : $"{copies.Count} files in {where} were changed in another app. Reset replaces them with the pieces' original art."))
            {
                // The question was about all of the ticked rows, so a no leaves every one of them alone.
                return;
            }

            try
            {
                // The file stays the editor's and stays in _workingCopies: what changes is what is in it.
                await Task.Run(() =>
                {
                    foreach (var row in copies)
                    {
                        File.Copy(row.OriginalPath, row.WorkingCopyPath!, overwrite: true);
                    }
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ImageError = ex.Message;
                failed = true;
            }
        }

        foreach (var row in Pieces.Where(p => p.IsTicked && p.Art != PieceArt.Original).ToList())
        {
            row.ResetArt();
            await RefreshThumbnailAsync(row);
        }

        if (!failed)
        {
            ImageError = "";
        }

        OnImageChanged();
    }

    /// <summary>Spec 6: every ticked piece is written into the pack as a real file and handed to a program of the
    /// user's choosing. The copy is what the row reads from afterwards, its folder is watched so a save in that
    /// program comes back into the preview, and the file stays in the pack whichever button closes the window
    /// (ruling 8).</summary>
    [RelayCommand(CanExecute = nameof(CanEditOutside))]
    private async Task EditOutsideAsync()
    {
        if (EnsurePackFolder(out var pack, out var packError) is not { } folder)
        {
            ImageError = packError;
            return;
        }

        var failed = false;
        var opened = new List<PlatformPieceViewModel>();
        foreach (var row in Pieces.Where(p => p.IsTicked).ToList())
        {
            var copy = Path.Combine(folder, row.FileName);
            // A copy already there at the piece's own values is the file to hand over as it stands; anything
            // else is written out first, because what the user edits has to be what the editor is showing.
            if (!File.Exists(copy) || row.Art == PieceArt.Replacement || !row.IsDefault)
            {
                try
                {
                    await Task.Run(() => row.WriteResult(copy));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
                {
                    ImageError = ex.Message;
                    failed = true;
                    continue;
                }
            }

            row.SetWorkingCopy(copy, pack);
            opened.Add(row);
            if (!_workingCopies.Any(w => string.Equals(w.Path, copy, StringComparison.OrdinalIgnoreCase)))
            {
                _workingCopies.Add((copy, pack));
            }

            WatchFolder(folder);
            if (EditorLauncher.OpenWith(copy) is { } reason)
            {
                ImageError = reason;
                failed = true;
            }
        }

        if (!failed)
        {
            ImageError = "";
        }

        foreach (var row in opened)
        {
            await RefreshThumbnailAsync(row);
        }

        // The rows that were handed over went back to 100 and 0, because their values are in the file now.
        _syncing = true;
        if (Pieces.FirstOrDefault(p => p.IsTicked) is { } first)
        {
            Opacity = first.Opacity;
            Hue = first.Hue;
        }

        _syncing = false;
        OnPropertyChanged(nameof(OpacityText));
        OnPropertyChanged(nameof(HueText));
        OnImageChanged();
    }

    /// <summary>The pack folder this map's working copies go in, made if it is not there, or null with
    /// <paramref name="error"/> set. Save builds the same path, so the two write into the one place.</summary>
    private string? EnsurePackFolder(out string packName, out string error)
    {
        packName = EffectivePackName;
        error = "";
        var packRoot = Path.Combine(PackScanner.PacksRoot(_services.LibraryPath), packName);
        // TryCreate refuses a name another pack already holds, which is not a failure here: a "New pack..." the
        // user already edited into once is the pack this one goes in too.
        if (IsNewPack && !Directory.Exists(packRoot) && !PackCreator.TryCreate(_services.LibraryPath, packName, out error))
        {
            return null;
        }

        var folder = Path.Combine(packRoot, _request.Map.FolderName);
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return null;
        }

        if (IsNewPack)
        {
            // The pack exists now, so the row reads as the pack it is and Save writes there rather than
            // making a second one.
            PackChoices = PackChoices
                .Where(c => c != BackgroundEditorViewModel.NewPackChoice)
                .Append(packName)
                .Concat([BackgroundEditorViewModel.NewPackChoice])
                .ToList();
            OnPropertyChanged(nameof(PackChoices));
            TargetPack = packName;
        }

        return folder;
    }

    /// <summary>Ruling 9: every library set folder the rows read from is followed from the moment it becomes a
    /// source, and only those. The game folder is never watched, because the editor never writes into it, and
    /// the temp set is the preview's own output.</summary>
    private void WatchFolder(string folder)
    {
        var full = Path.GetFullPath(folder);
        if (_watchers.ContainsKey(full) || IsUnder(full, _services.GamePath) || IsUnder(full, _tempRoot))
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(full, "*.png")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            watcher.Changed += OnFolderChanged;
            watcher.Created += OnFolderChanged;
            watcher.Deleted += OnFolderChanged;
            watcher.Renamed += OnFolderChanged;
            _watchers[full] = watcher;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // The copy is in the pack either way; all that is lost is the preview following it on its own.
        }
    }

    /// <summary>The watcher's thread is not the UI's, and one save is several writes, so the change is handed
    /// over and then waited out.</summary>
    private void OnFolderChanged(object sender, FileSystemEventArgs e) =>
        Application.Current?.Dispatcher.InvokeAsync(() => _changed.Run(async ct => await ReloadChangedAsync(ct)));

    /// <summary>What a save in another program changes: the rows reading from a watched folder, and the map.
    /// A working copy that program deleted, or renamed to another name, is gone from under its row, so the row
    /// goes back to the piece's own art rather than keeping the picture it last read (ruling 9).</summary>
    private async Task ReloadChangedAsync(CancellationToken ct)
    {
        var watched = new List<(PlatformPieceViewModel Row, string FilePath)>();
        foreach (var row in Pieces)
        {
            if (row.WorkingCopyPath is { } filePath && IsWatched(filePath))
            {
                watched.Add((row, filePath));
            }
        }

        // One hop for the whole set, because the watcher fires on every write another program makes.
        var removed = await Task.Run(() => watched.Where(w => !File.Exists(w.FilePath)).ToList(), ct);
        if (removed.Count > 0)
        {
            // Read before the reset, which is what drops the path the name comes from.
            var names = removed.Select(w => Path.GetFileName(w.FilePath)).ToList();
            foreach (var (row, _) in removed)
            {
                ct.ThrowIfCancellationRequested();
                row.ResetArt();
                await RefreshThumbnailAsync(row, ct);
            }

            ImageError = names.Count == 1
                ? $"{names[0]} was removed from the pack; showing the piece's own art."
                : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]} were removed from the pack;"
                    + " showing the piece's own art.";
            OnImageChanged();
        }

        foreach (var row in Pieces.Where(p => p.Replacement is null && IsWatched(p.SourcePath)))
        {
            ct.ThrowIfCancellationRequested();
            await RefreshThumbnailAsync(row, ct);
        }

        SchedulePreview();
    }

    private bool IsWatched(string filePath) =>
        Path.GetDirectoryName(Path.GetFullPath(filePath)) is { } folder && _watchers.ContainsKey(folder);

    /// <summary>Whether <paramref name="path"/> sits inside <paramref name="root"/>, the folder itself counting
    /// as inside it.</summary>
    private static bool IsUnder(string path, string root)
    {
        var full = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root);
        return full.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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
        EditOutsideCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsolateHint));

        // In All pieces a tick only moves the sliders, as in 2.3. In Ticked only it changes the picture, so the
        // render is asked for; the 60 ms throttle collapses the run of ticks All, None and Solo produce.
        if (IsolatePreview)
        {
            SchedulePreview();
        }
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

                var record = PlatformEditRecord.Load(packRoot);
                record.SetMap(_request.Map.FolderName, DateTimeOffset.Now, EntriesFor(rows, packRoot));
                record.Save(packRoot);
            });
            Saved = new PlatformSave(EffectivePackName, destination, ApplyNow);
            CloseRequested?.Invoke(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            _dialogs.Error("Could not save the platforms", ex.Message);
        }
    }

    /// <summary>Builds the record entry set for the rows just written into packRoot (hash from the written file).
    /// A row whose file is not there was not written, so the record says nothing about it.</summary>
    internal static Dictionary<string, PlatformPieceEntry> EntriesFor(IReadOnlyList<PlatformPieceViewModel> rows, string packRoot)
    {
        var entries = new Dictionary<string, PlatformPieceEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var written = Path.Combine(packRoot, row.RelativePath);
            if (!File.Exists(written))
            {
                continue;
            }

            var entry = new PlatformPieceEntry { Art = row.ArtKind, Hash = FileHasher.Hash(written) };
            if (entry.Art != PlatformArt.WorkingCopy)
            {
                entry.Opacity = row.Opacity;
                entry.Hue = row.Hue;
                if (entry.Art == PlatformArt.EachPiece)
                {
                    entry.Picture = row.ReplacementPath;
                }
            }

            entries[row.RelativePath] = entry;
        }

        return entries;
    }

    /// <summary>Spec 6: 60 ms between renders, newest values win. The stamp is what makes the second half of that
    /// true, because a render already on the queue still finishes after a newer one has been asked for.</summary>
    private void SchedulePreview()
    {
        var stamp = ++_sequence;
        _preview.Run(ct => RenderAsync(stamp, ct));
    }

    /// <summary>Spec 3.2. Not isolating, a map with no pieces, and every piece ticked all render exactly as 2.3
    /// did: null focus, null viewport, pixel for pixel. Isolating with nothing ticked passes an empty set, which
    /// ghosts everything, over the full camera. Otherwise the ticked paths are the focus and the frame leans in;
    /// a frame that comes back null, which is focused pieces with no size, falls back to the full camera too.</summary>
    private (IReadOnlySet<string>? Focus, CameraBounds? Viewport) FocusFor(LevelDesc level)
    {
        if (!IsolatePreview || Pieces.Count == 0)
        {
            return (null, null);
        }

        var ticked = Pieces.Where(p => p.IsTicked).ToList();
        if (ticked.Count == Pieces.Count)
        {
            return (null, null);
        }

        var focus = new HashSet<string>(ticked.Select(p => p.RelativePath), StringComparer.OrdinalIgnoreCase);
        if (focus.Count == 0)
        {
            return (focus, null);
        }

        var aspect = (double)MapCompositor.PanelWidth / MapCompositor.PanelHeight;
        return (focus, PlatformBounds.For(level, PlatformBounds.Pad, aspect, focus));
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
        var (focus, viewport) = FocusFor(level);
        var reading = "";
        try
        {
            await Task.Run(
                () =>
                {
                    foreach (var row in rows)
                    {
                        ct.ThrowIfCancellationRequested();
                        reading = row.FileName;
                        row.CopyOrWriteResult(Path.Combine(root, row.RelativePath));
                    }
                },
                ct);

            var sources = new AssetSources(root, backgroundOverride: background);
            var bitmap = await _services.Renderer.RunAsync(
                () => MapCompositor.Render(
                    level, MapCompositor.PanelWidth, MapCompositor.PanelHeight, sources, viewport, focus),
                ct);
            if (ct.IsCancellationRequested || stamp != _sequence)
            {
                return;
            }

            Preview = bitmap;
            Error = "";
            _retries = 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            // A file another program is part way through saving is locked or half written, and the watcher has
            // already asked for this render because of that save. Waiting is the answer, not an error line.
            if (ex is IOException && _watchers.Count > 0 && _retries < MaxReadRetries)
            {
                _retries++;
                await Task.Delay(RetryDelay, ct);
                SchedulePreview();
                return;
            }

            if (ex is IOException && _watchers.Count > 0)
            {
                ImageError = $"Could not read {reading}: {ex.Message}";
                return;
            }

            Error = "Could not read the platform art: " + ex.Message;
        }
    }
}
