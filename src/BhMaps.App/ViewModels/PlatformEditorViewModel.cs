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
/// panel row asked for, or null for the whole set. Only that one piece opens ticked (spec 7). SourcePack is the
/// pack whose record the values are loaded from: the pack itself when there is one, the pack the map's files were
/// matched to otherwise, and null when no pack remembers this map (spec 5.1).</summary>
public sealed record PlatformEditorRequest(MapEntry Map, Pack? Pack, string? OnlyFile = null, Pack? SourcePack = null);

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

    /// <summary>Spec 5.2: the record's values are meant for the untouched art, and the game folder's file is the
    /// nearest thing to it when the Default pack has nothing.</summary>
    public const string NoOriginalNoteText = "The untouched art was not found, so the preview starts from the saved file.";

    /// <summary>Spec 5.2: the file the pack holds is not what the record's values were worked out from, so the
    /// preview is a fair warning rather than a promise.</summary>
    public const string ChangedOutsideNoteText = "The file changed since, so the preview may differ from the game.";

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

    /// <summary>The cuts the switch and the drag ask for, on the preview's own rate limit (spec 6.2).</summary>
    private readonly Throttler _recut = new(PreviewInterval);

    /// <summary>The rows whose note this editor's own fitting wrote, so a row already saying something about
    /// itself keeps that line and only a fit note is written over (spec 5.2).</summary>
    private readonly HashSet<PlatformPieceViewModel> _fitNoteRows = [];

    /// <summary>The record the editor opened with, or null when no pack remembers this map (spec 5.1).</summary>
    private readonly PlatformEditRecord? _record;

    /// <summary>The rows the record built, with the entry each one came from: what the background load fits,
    /// hashes and, for Start fresh, puts back (spec 5.2).</summary>
    private readonly List<(PlatformPieceViewModel Row, PlatformPieceEntry Entry)> _recordRows = [];

    /// <summary>The pictures and hashes the record asked for, running off the UI thread. The first preview waits
    /// on it, so the map is never drawn from half a record.</summary>
    private readonly Task _recordArt;

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

    /// <summary>True while a loaded record is putting its picture, its pan and its switch on, so the three of
    /// them are one cut at the end rather than one cut each.</summary>
    private bool _restoringFit;

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
        _record = request.SourcePack is { } recordPack ? PlatformEditRecord.Load(recordPack.FullPath) : null;
        Pieces = BuildPieces();
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

        // Spec 5.3: the line names the pack the values came from, and Save goes back to that pack when the list
        // still has it, because that is the set the user is carrying on with.
        ValuesFromText = "";
        if (request.SourcePack is { } valuesFrom && _record?.Map(request.Map.FolderName) is { } saved)
        {
            HasValuesFrom = true;
            ValuesFromText = $"Values from {valuesFrom.Name}, saved {saved.SavedAt.ToLocalTime():d MMM HH:mm}.";
            if (PackChoices.Any(p => p.Equals(valuesFrom.Name, StringComparison.OrdinalIgnoreCase)))
            {
                TargetPack = valuesFrom.Name;
            }
        }

        // The sliders open on the first ticked row, which is where a loaded record put its values, and on the
        // defaults when nothing was loaded, exactly as 2.4 opened. Syncing, so this never fans back out.
        _syncing = true;
        var firstTicked = Pieces.FirstOrDefault(p => p.IsTicked);
        Opacity = firstTicked?.Opacity ?? PlatformPieceViewModel.DefaultOpacity;
        Hue = firstTicked?.Hue ?? PlatformPieceViewModel.DefaultHue;
        _syncing = false;

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

        _recordArt = LoadRecordArtAsync();
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

    /// <summary>Spec 6.2: true lays one picture across every platform and cuts each piece out of it, false fits
    /// the same picture to each piece on its own, as 2.4 did. Changing it cuts the ticked rows again.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPan))]
    public partial bool FitAcross { get; set; } = true;

    /// <summary>Where the laid picture sits inside the platform box, 0..1 (spec 6.2). The drag on the preview is
    /// the only thing that moves it.</summary>
    [ObservableProperty]
    public partial double PanX { get; set; } = 0.5;

    [ObservableProperty]
    public partial double PanY { get; set; } = 0.5;

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

    /// <summary>"Values from Default, saved 12 Sep 22:01." (spec 5.3).</summary>
    [ObservableProperty]
    public partial string ValuesFromText { get; set; }

    /// <summary>Whether the Values from line and its Start fresh link are shown (spec 5.3).</summary>
    [ObservableProperty]
    public partial bool HasValuesFrom { get; set; }

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

    /// <summary>The picture Replace loaded, frozen and kept so the switch and the drag cut it again without
    /// going back to the disk (spec 6.2). Null until a picture is picked or a record names one.</summary>
    public BitmapSource? LoadedPicture { get; private set; }

    /// <summary>The full path the loaded picture came from, which is what the record writes down.</summary>
    public string? LoadedPicturePath { get; private set; }

    /// <summary>Whether the fit switch can be used: there is a picture to lay (spec 6.2).</summary>
    public bool CanUseFit => LoadedPicture is not null;

    /// <summary>Whether dragging the preview moves anything: a laid picture, not one fitted piece by piece.</summary>
    public bool CanPan => CanUseFit && FitAcross;

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
        _recut.Cancel();
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

    /// <summary>The switch and the pan both mean the same thing: cut the ticked rows out of the loaded picture
    /// again. Nothing is loaded until a Replace or a record, and the record puts all three on at once.</summary>
    partial void OnFitAcrossChanged(bool value) => ScheduleRecut();

    partial void OnPanXChanged(double value) => ScheduleRecut();

    partial void OnPanYChanged(double value) => ScheduleRecut();

    private void ScheduleRecut()
    {
        if (_restoringFit || !CanUseFit)
        {
            return;
        }

        // Spec 6.2: a drag asks for a cut per mouse move, and the cut is the slow part, so the same 60 ms
        // throttle the preview uses collapses the run and only the newest pan survives.
        _recut.Run(_ => RecutAsync());
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

    /// <summary>Where each piece is read from: the file the record's values were worked out from when a pack
    /// remembers this map (spec 5.2), and otherwise the pack's copy when the pack has one that draws something
    /// and the game's when it does not, which is the rule every composite already resolves by (spec 6).</summary>
    private IReadOnlyList<PlatformPieceViewModel> BuildPieces()
    {
        var pieces = new List<PlatformPieceViewModel>();
        foreach (var relativePath in _request.Map.PlatformFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var ticked = _request.OnlyFile is null
                || string.Equals(relativePath, _request.OnlyFile, StringComparison.OrdinalIgnoreCase);
            var entry = _record?.Entry(_request.Map.FolderName, relativePath);

            // A working copy is a file another program owns, so its row opens on that file the way 2.4 opened it.
            if (entry is null || entry.Art == PlatformArt.WorkingCopy)
            {
                if (ResolveAsset(relativePath) is { } source)
                {
                    pieces.Add(new PlatformPieceViewModel(relativePath, source, ticked));
                }

                continue;
            }

            if (BuildRecordPiece(relativePath, entry, ticked) is { } row)
            {
                pieces.Add(row);
            }
        }

        return pieces;
    }

    /// <summary>Spec 5.2: the record's values were worked out from the untouched art, so the row starts from the
    /// Default pack's copy of the piece, or the game's when the library has no Default pack. Null when neither
    /// is there, which is the unresolved file 2.4 leaves out of the list.</summary>
    private PlatformPieceViewModel? BuildRecordPiece(string relativePath, PlatformPieceEntry entry, bool ticked)
    {
        var note = "";
        var original = Path.Combine(PackScanner.PacksRoot(_services.LibraryPath), DefaultPack.Name, relativePath);
        if (!File.Exists(original))
        {
            original = Path.Combine(_services.GamePath, relativePath);
            note = NoOriginalNoteText;
        }

        if (!File.Exists(original))
        {
            return null;
        }

        // Part B replaces this branch: one picture cut across every piece is spanned, not repeated.
        var picture = entry.Art is PlatformArt.EachPiece or PlatformArt.Across ? entry.Picture : null;
        if (picture is { Length: > 0 } && !File.Exists(picture))
        {
            // The picture is gone, so there is nothing to fit: the row shows what the pack holds, as saved.
            var saved = _request.SourcePack is { } pack ? Path.Combine(pack.FullPath, relativePath) : original;
            return new PlatformPieceViewModel(relativePath, File.Exists(saved) ? saved : original, ticked)
            {
                LoadedFromRecord = true,
                Note = $"{Path.GetFileName(picture)}, missing. Showing the saved file.",
            };
        }

        var row = new PlatformPieceViewModel(relativePath, original, ticked)
        {
            LoadedFromRecord = true,
            Opacity = entry.Opacity ?? PlatformPieceViewModel.DefaultOpacity,
            Hue = entry.Hue ?? PlatformPieceViewModel.DefaultHue,
            Note = note,
        };
        _recordRows.Add((row, entry));
        return row;
    }

    /// <summary>The 2.4 resolution of one piece: the pack the editor was opened with, then the game.</summary>
    private string? ResolveAsset(string relativePath) =>
        new AssetSources(_services.GamePath, _request.Pack?.FullPath).ResolveAsset(relativePath);

    /// <summary>Spec 5.2: the pictures the record names are fitted to their pieces and the files the pack holds
    /// are hashed against what the record was written for, both off the UI thread. The rows only change here,
    /// when all of that is done, and the preview waits on this task before it draws.</summary>
    private async Task LoadRecordArtAsync()
    {
        if (_recordRows.Count == 0)
        {
            return;
        }

        var packRoot = _request.SourcePack?.FullPath;
        var rows = _recordRows.Select(r => (r.Row, r.Entry, HasNote: r.Row.Note.Length > 0)).ToList();
        var reading = "";
        List<(PlatformPieceViewModel Row, BitmapSource? Fitted, string Picture, string Note)> loaded;
        try
        {
            loaded = await Task.Run(() =>
            {
                var results = new List<(PlatformPieceViewModel, BitmapSource?, string, string)>();
                foreach (var (row, entry, hasNote) in rows)
                {
                    // An Across row is not fitted here: its picture is laid over the whole stage once, which the
                    // cut below does for all of them together (spec 6.2).
                    var picture = entry.Art == PlatformArt.EachPiece ? entry.Picture : null;
                    BitmapSource? fitted = null;
                    if (picture is { Length: > 0 } && File.Exists(picture))
                    {
                        reading = Path.GetFileName(picture);
                        fitted = PieceFitter.Fit(
                            BackgroundFitter.LoadSource(picture), BackgroundFitter.LoadSource(row.OriginalPath));
                    }

                    results.Add((row, fitted, picture ?? "", ChangedOutsideNote(row, entry, packRoot, hasNote)));
                }

                return results;
            });
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            ImageError = $"Could not read {reading}. The pieces are showing their own art.";
            return;
        }

        foreach (var (row, fitted, picture, note) in loaded)
        {
            if (fitted is not null)
            {
                row.SetReplacement(fitted, Path.GetFileName(picture), picture);
                row.Thumbnail = ThumbnailOf(fitted);
            }

            if (note.Length > 0)
            {
                row.Note = note;
            }
        }

        await LoadRecordSpanAsync();
        OnImageChanged();
    }

    /// <summary>Spec 5.2 and 6.2: the record's Across rows are one picture laid across the platforms with the pan
    /// it was saved with, so the picture, the pan and the switch go on together and one cut covers all of them.
    /// The rows the record saved as EachPiece keep the fit they were loaded with.</summary>
    private async Task LoadRecordSpanAsync()
    {
        var spanning = _recordRows
            .Where(r => r.Entry.Art == PlatformArt.Across
                && r.Entry.Picture is { Length: > 0 } picture
                && File.Exists(picture))
            .ToList();
        if (spanning.Count == 0)
        {
            return;
        }

        var entry = spanning[0].Entry;
        _restoringFit = true;
        FitAcross = true;
        PanX = Math.Clamp(entry.PanX ?? 0.5, 0, 1);
        PanY = Math.Clamp(entry.PanY ?? 0.5, 0, 1);
        _restoringFit = false;

        if (await LoadPictureAsync(entry.Picture!))
        {
            await RecutAsync(spanning.Select(r => r.Row).ToList());
        }
    }

    /// <summary>Spec 5.2: the pack's file is not the file the record's values were worked out from, so the row
    /// says the preview may differ. Empty when it matches, when there is nothing to compare, or when the row is
    /// already saying something else about itself.</summary>
    private static string ChangedOutsideNote(
        PlatformPieceViewModel row, PlatformPieceEntry entry, string? packRoot, bool hasNote)
    {
        if (hasNote || packRoot is null)
        {
            return "";
        }

        var packFile = Path.Combine(packRoot, row.RelativePath);
        return File.Exists(packFile)
            && !FileHasher.Hash(packFile).Equals(entry.Hash, StringComparison.OrdinalIgnoreCase)
                ? ChangedOutsideNoteText
                : "";
    }

    /// <summary>Spec 5.4: the record's values go, and every row is the file and the numbers the editor would have
    /// opened on with nothing remembered. The rows themselves stay, so the list's bindings hold.</summary>
    [RelayCommand]
    private void StartFresh()
    {
        _syncing = true;
        foreach (var row in Pieces)
        {
            if (row.LoadedFromRecord && ResolveAsset(row.RelativePath) is { } source)
            {
                row.ResetOriginal(source);
            }

            row.ResetArt();
            row.Opacity = PlatformPieceViewModel.DefaultOpacity;
            row.Hue = PlatformPieceViewModel.DefaultHue;
            row.Note = "";
        }

        Opacity = PlatformPieceViewModel.DefaultOpacity;
        Hue = PlatformPieceViewModel.DefaultHue;
        _syncing = false;
        HasValuesFrom = false;
        ClearPicture();
        ImageError = "";
        OnImageChanged();
        OnPropertyChanged(nameof(OpacityText));
        OnPropertyChanged(nameof(HueText));
        SchedulePreview();
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

        if (!await LoadPictureAsync(path))
        {
            return;
        }

        await RecutAsync(ticked);
    }

    /// <summary>The picked picture, decoded once off the UI thread and kept for every later cut. False when it
    /// could not be read, with the line already on the panel.</summary>
    private async Task<bool> LoadPictureAsync(string path)
    {
        try
        {
            LoadedPicture = await Task.Run(() => BackgroundFitter.LoadSource(path));
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            ImageError = $"Could not read {Path.GetFileName(path)}. Use a PNG, JPG, BMP, GIF or WebP.";
            return false;
        }

        LoadedPicturePath = path;
        OnPropertyChanged(nameof(CanUseFit));
        OnPropertyChanged(nameof(CanPan));
        return true;
    }

    /// <summary>The picture stops being what any row is made of: the switch has nothing to lay, and no row has a
    /// fit note left that a later cut could write over.</summary>
    private void ClearPicture()
    {
        LoadedPicture = null;
        LoadedPicturePath = null;
        _fitNoteRows.Clear();
        OnPropertyChanged(nameof(CanUseFit));
        OnPropertyChanged(nameof(CanPan));
    }

    /// <summary>Spec 6.2: every row named is cut from the loaded picture again with the switch and the pan as
    /// they are now. Across, the picture is laid over the platforms' own box once and each row takes the part
    /// under its largest placement; a row the stage never draws is fitted on its own instead, and says so. The
    /// cutting is the slow part, so it happens off the UI thread and no row changes until all of them have one.</summary>
    private async Task RecutAsync(IReadOnlyList<PlatformPieceViewModel>? only = null)
    {
        if (LoadedPicture is not { } picture || LoadedPicturePath is not { } path)
        {
            return;
        }

        var rows = only ?? Pieces.Where(p => p.IsTicked).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var level = _request.Map.BaseLevel;
        var box = SpanFitter.Box(level);
        var across = FitAcross;
        var pan = new FitOptions(PanX: PanX, PanY: PanY);
        List<(PlatformPieceViewModel Row, BitmapSource Fitted, bool Across, string Note)> cut;
        try
        {
            cut = await Task.Run(() =>
            {
                var results = new List<(PlatformPieceViewModel, BitmapSource, bool, string)>();
                foreach (var row in rows)
                {
                    var piece = BackgroundFitter.LoadSource(row.SourcePath);
                    if (!across || box is not { } stage)
                    {
                        results.Add((row, PieceFitter.Fit(picture, piece), false, ""));
                        continue;
                    }

                    var placements = SpanFitter.Placements(level, row.RelativePath, piece.PixelWidth, piece.PixelHeight);
                    if (placements.Count == 0)
                    {
                        results.Add((
                            row,
                            PieceFitter.Fit(picture, piece),
                            false,
                            $"{row.FileName} not on this stage, fitted on its own."));
                        continue;
                    }

                    // The same file drawn twice is one asset with two placements, and the picture can only be cut
                    // for one of them, so the largest is the one the user is looking at (spec 6.1).
                    var note = placements.Count > 1
                        ? $"{row.FileName} drawn {placements.Count} times, cut from the largest."
                        : "";
                    results.Add((row, SpanFitter.Cut(picture, stage, pan, SpanFitter.Largest(placements)!, piece), true, note));
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
        foreach (var (row, fitted, rowAcross, note) in cut)
        {
            row.SetReplacement(fitted, name, path, rowAcross);
            row.Thumbnail = ThumbnailOf(fitted);
            SetFitNote(row, note);
        }

        OnImageChanged();
    }

    /// <summary>The line a cut leaves on a row. A row already saying its picture is gone, its untouched art is
    /// missing or its file changed outside keeps that line: only a fit note is written over (spec 5.2).</summary>
    private void SetFitNote(PlatformPieceViewModel row, string note)
    {
        if (row.Note.Length > 0 && !_fitNoteRows.Contains(row))
        {
            return;
        }

        row.Note = note;
        if (note.Length > 0)
        {
            _fitNoteRows.Add(row);
        }
        else
        {
            _fitNoteRows.Remove(row);
        }
    }

    /// <summary>Spec 6.2: dragging the preview moves the laid picture. The delta arrives in the stage's own
    /// 1280 by 720 pixels, and a cover fit only has room to move where it hangs over the box, so the delta is
    /// turned into pan units by that overflow and clamped. A picture with no overflow one way does not move
    /// that way, and the cut that follows is throttled.</summary>
    public void DragPan(double dxStagePixels, double dyStagePixels)
    {
        var level = _request.Map.BaseLevel;
        if (LoadedPicture is not { } picture || !FitAcross || SpanFitter.Box(level) is not { } box)
        {
            return;
        }

        var (_, viewport) = FocusFor(level);
        if ((viewport ?? level.Camera) is not { W: > 0, H: > 0 } camera)
        {
            return;
        }

        // The preview draws the camera's part of the level into the panel, so a stage pixel is that many level
        // units, and the box the picture is laid in is measured in level units.
        var dest = BackgroundFitter.DestinationRect(
            picture.PixelWidth,
            picture.PixelHeight,
            new FitOptions(PanX: PanX, PanY: PanY),
            Math.Max(1, (int)Math.Round(box.Width)),
            Math.Max(1, (int)Math.Round(box.Height)));
        var scaleX = MapCompositor.PanelWidth / camera.W;
        var scaleY = MapCompositor.PanelHeight / camera.H;
        var overflowX = dest.Width - box.Width;
        var overflowY = dest.Height - box.Height;
        if (overflowX > 0)
        {
            PanX = Math.Clamp(PanX - (dxStagePixels / scaleX / overflowX), 0, 1);
        }

        if (overflowY > 0)
        {
            PanY = Math.Clamp(PanY - (dyStagePixels / scaleY / overflowY), 0, 1);
        }
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
            SetFitNote(row, "");
            await RefreshThumbnailAsync(row);
        }

        // Nothing is made of the picture any more, so the switch has nothing left to lay (spec 6.2).
        if (Pieces.All(p => p.Art != PieceArt.Replacement))
        {
            ClearPicture();
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
        var (panX, panY) = (PanX, PanY);
        try
        {
            await Task.Run(() =>
            {
                foreach (var row in rows)
                {
                    row.CopyOrWriteResult(Path.Combine(packRoot, row.RelativePath));
                }

                var record = PlatformEditRecord.Load(packRoot);
                record.SetMap(_request.Map.FolderName, DateTimeOffset.Now, EntriesFor(rows, packRoot, panX, panY));
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
    /// A row whose file is not there was not written, so the record says nothing about it. The pan belongs to the
    /// editor rather than to a row, so an Across row writes down the one the picture was laid with (spec 6.2).</summary>
    internal static Dictionary<string, PlatformPieceEntry> EntriesFor(
        IReadOnlyList<PlatformPieceViewModel> rows, string packRoot, double panX, double panY)
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
                else if (entry.Art == PlatformArt.Across)
                {
                    entry.Picture = row.ReplacementPath;
                    entry.PanX = panX;
                    entry.PanY = panY;
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
            // The record's pictures are what some of the rows are made of, so the first render waits for them.
            await _recordArt;
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
