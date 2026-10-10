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

/// <summary>What a set tile's Edit hands the editor: the maps whose own pieces are recoloured, the pack the
/// set came from, or null for the set the game is showing (spec 6), and the relative path of the one piece a
/// panel row asked for, or null for the whole set. Only that one piece opens ticked (spec 7). SourcePack is the
/// pack whose record the values are loaded from: the pack itself when there is one, the pack the map's files were
/// matched to otherwise, and null when no pack remembers this map (spec 5.1). Catalog, when given, says which
/// other layout of a folder also draws a piece (3.2).</summary>
public sealed record PlatformEditorRequest(
    IReadOnlyList<MapEntry> Maps, Pack? Pack, string? OnlyFile = null, Pack? SourcePack = null,
    MapCatalog? Catalog = null)
{
    /// <summary>The first map of the set, which is the whole set for every way in that opens on one map.</summary>
    public MapEntry Map => Maps[0];
}

/// <summary>What one Save left in the library: the pack it was saved into, which is the pack the shell stamps
/// when it applies it, the folder of the first map it wrote inside that pack, whether the user asked for it to go
/// into the game as well, and the maps it actually wrote, which is every map of the set unless Cancel stopped the
/// run part way (spec 9). The shell does the game part, so the editor never writes into the game folder.</summary>
public sealed record PlatformSave(
    string PackName, string SetFolder, bool ApplyToGame, IReadOnlyList<MapEntry> Maps);

/// <summary>Spec 9: one map's own side of the editor. The rows, the background the preview sits over and the
/// record the values came from belong to the map; the picture, the pan, the values and the target pack belong to
/// the editor and are shared by every map in the set. The three lines that Start fresh and the record write are
/// settable, because they are what the strip reads back when it comes to this map again.</summary>
internal sealed record MapSet(MapEntry Map, PlatformEditRecord? Record, Pack? SourcePack, string? BackgroundPath)
{
    /// <summary>One row per piece of this map, in the order the file list draws them (spec 3).</summary>
    public IReadOnlyList<PlatformPieceViewModel> Rows { get; set; } = [];

    /// <summary>"Values from Default, saved 12 Sep 22:01." for this map, or empty (spec 5.3).</summary>
    public string ValuesFromText { get; set; } = "";

    public bool HasValuesFrom { get; set; }

    /// <summary>The rows this map's record built, with the entry each one came from: what the record art fits,
    /// hashes and, for Start fresh, puts back (spec 5.2).</summary>
    public List<(PlatformPieceViewModel Row, PlatformPieceEntry Entry)> RecordRows { get; } = [];
}

/// <summary>Spec 6: one map's own platform pieces, faded and recoloured by one number each. Every slider change
/// writes the processed pieces into a temp set and composes the map from it, so the preview is the same render
/// the panel and the rows draw, over the background the game is showing for that map today.</summary>
public partial class PlatformEditorViewModel : ObservableObject
{
    public const string NoPackText = "In game";
    public const string NoFilesText = "This map has no platform art of its own.";
    public const string MixedText = "Mixed";
    public const string TickHintText = "Select a file to edit it.";

    /// <summary>Spec 3.2: Selected only with nothing selected ghosts the whole map, so the preview says what to do
    /// about it, in the same words TickHintText uses for the sliders.</summary>
    public const string IsolateHintText = "Select a file to see it on its own.";

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
    private readonly Throttler _preview = new(PreviewInterval);
    private readonly List<(string Path, string PackName)> _workingCopies = [];
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Debouncer _changed = new(ChangedDelay);

    /// <summary>The cuts the switch and the drag ask for, on the preview's own rate limit (spec 6.2).</summary>
    private readonly Throttler _recut = new(PreviewInterval);

    /// <summary>The rows whose note this editor's own fitting wrote, so a row already saying something about
    /// itself keeps that line and only a fit note is written over (spec 5.2).</summary>
    private readonly HashSet<PlatformPieceViewModel> _fitNoteRows = [];

    /// <summary>Spec 9: every map the editor was opened on, in the order the strip walks them.</summary>
    private readonly IReadOnlyList<MapSet> _sets;

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

    /// <summary>The run Save is in the middle of, so Cancel can stop it between maps, or null (spec 9).</summary>
    private CancellationTokenSource? _saveCts;

    public PlatformEditorViewModel(
        AppServices services,
        IDialogs dialogs,
        IReadOnlyList<Pack> packs,
        IReadOnlyDictionary<string, MapStatus> statuses,
        PlatformEditorRequest request)
    {
        _services = services;
        _dialogs = dialogs;
        _request = request;
        _tempRoot = Path.Combine(Path.GetTempPath(), "BhMaps", "platform-editor", Guid.NewGuid().ToString("N"));
        _sets = request.Maps.Select(map => BuildSet(map, packs, statuses)).ToList();
        foreach (var row in AllRows())
        {
            row.PropertyChanged += OnRowPropertyChanged;
        }

        // A discovered pack (outside packs\) is read-only, so it is never a save target.
        PackChoices = packs.Where(p => !p.IsDiscovered).Select(p => p.Name).ToList();
        Error = "";

        // Spec 3.4: a panel row's Edit names one file, and that editor opens on it. Spec 3.5: every other way in
        // opens in the mode the last chip click left behind.
        _restoringMode = true;
        IsolatePreview = request.OnlyFile is not null || services.Settings.PlatformPreviewIsolate;
        _restoringMode = false;

        // 3.6 E2: custom platforms go into a pack of their own, so the default is a fresh Custom Pack N. Opened on
        // a pack's own tile, the edit is of that pack and goes back into it; the Default pack is the one
        // exception, because it is the game's own art and Reset reads from it.
        PackName = PackNames.NextFree(TakenPackNames());
        if (request.Pack is { } ownPack
            && !ownPack.Name.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase)
            && PackChoices.Any(p => p.Equals(ownPack.Name, StringComparison.OrdinalIgnoreCase)))
        {
            PackName = ownPack.Name;
        }

        // Spec 5.3: the line names the pack the values came from, and Save goes back to that pack when the list
        // still has it, because that is the set the user is carrying on with. With many maps that is the first
        // map that remembers one, because one Save writes all of them into the one pack (spec 9).
        ValuesFromText = Current.ValuesFromText;
        HasValuesFrom = Current.HasValuesFrom;
        if (_sets.FirstOrDefault(s => s.HasValuesFrom)?.SourcePack is { } valuesFrom
            && PackChoices.Any(p => p.Equals(valuesFrom.Name, StringComparison.OrdinalIgnoreCase)))
        {
            PackName = valuesFrom.Name;
        }

        // The sliders open on the first ticked row, which is where a loaded record put its values, and on the
        // defaults when nothing was loaded, exactly as 2.4 opened. Syncing, so this never fans back out.
        _syncing = true;
        var firstTicked = TickedRows().FirstOrDefault();
        Opacity = firstTicked?.Opacity ?? PlatformPieceViewModel.DefaultOpacity;
        Hue = firstTicked?.Hue ?? PlatformPieceViewModel.DefaultHue;
        Saturation = firstTicked?.Saturation ?? PlatformPieceViewModel.DefaultTone;
        Contrast = firstTicked?.Contrast ?? PlatformPieceViewModel.DefaultTone;
        Darken = firstTicked?.Darken ?? PlatformPieceViewModel.DefaultTone;
        Blur = firstTicked?.Blur ?? PlatformPieceViewModel.DefaultTone;
        _syncing = false;

        // The rows of a pack's set are read from a folder the user can change from outside the app, so it is
        // watched from the moment the editor opens rather than only once a working copy is written (ruling 9).
        if (request.Pack is { } sourcePack)
        {
            foreach (var row in AllRows().Where(p => IsUnder(p.OriginalPath, sourcePack.FullPath)))
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

    /// <summary>One row per piece of the map the strip is on, in the order the file list draws them (spec 3).
    /// Switching maps raises this, so the whole list is read again from the map that is on now (spec 9).</summary>
    public IReadOnlyList<PlatformPieceViewModel> Pieces => Current.Rows;

    /// <summary>The files the editor is holding open for another program to edit, with the pack each one sits in.
    /// They stay in the library whichever button closes the window, which is what the shell's Cancel line says
    /// (ruling 8).</summary>
    public IReadOnlyList<(string Path, string PackName)> WorkingCopies => _workingCopies;

    /// <summary>What Save wrote into the library, or null while nothing has been saved.</summary>
    public PlatformSave? Saved { get; private set; }

    /// <summary>Save wrote faded pieces without the seam fix because there was no level data (3.2 O1).</summary>
    public bool SeamFixSkipped { get; private set; }

    /// <summary>Spec 9: which map of the set the strip is on. Everything one map owns follows it.</summary>
    [ObservableProperty]
    public partial int CurrentIndex { get; set; }

    /// <summary>Spec 9: "Saving 3 of 59 maps into Default" while one Save runs, and empty otherwise.</summary>
    [ObservableProperty]
    public partial string SaveProgressText { get; set; } = "";

    /// <summary>Spec 9: true while the save loop is running, which is what shows the progress line and its
    /// Cancel and holds the window's own two buttons.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(CanAddAsNew), nameof(CanCloseWindow))]
    [NotifyCanExecuteChangedFor(nameof(SaveAndApplyCommand), nameof(SaveOnlyCommand), nameof(AddAsNewCommand), nameof(CancelSaveCommand), nameof(PreviousMapCommand), nameof(NextMapCommand))]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpacityText))]
    public partial int Opacity { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HueText))]
    public partial int Hue { get; set; }

    /// <summary>The background editor's tone controls, per piece and fanned out the way Opacity and Hue are:
    /// Saturation and Contrast -100..100, Darken and Blur 0..100.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaturationText))]
    public partial int Saturation { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContrastText))]
    public partial int Contrast { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DarkenText))]
    public partial int Darken { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BlurText))]
    public partial int Blur { get; set; }

    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }

    /// <summary>Spec 6.2: true lays one picture across every platform and cuts each piece out of it, false fits
    /// the same picture to each piece on its own, as 2.4 did. Changing it cuts the ticked rows again. It can be
    /// chosen before any picture is loaded, and the next Replace uses it (3.2 F2).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPan), nameof(CanPanX), nameof(CanPanY), nameof(ImageText))]
    public partial bool FitAcross { get; set; } = true;

    /// <summary>3.0 E: how the picture fills the piece's box, or the whole stage when it is laid across. The one
    /// fit set every window offers. Changing it cuts the ticked rows again.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(CanPan), nameof(CanPanX), nameof(CanPanY), nameof(FitFill), nameof(FitFit), nameof(FitCenter), nameof(FitStretch), nameof(ImageText))]
    public partial PictureFit Fit { get; set; } = PictureFit.Fill;

    /// <summary>Where the picture sits inside the platform box, or inside each piece's own box, 0..1 (spec 6.2).
    /// The sliders, the drag and the wheel on the preview all move it (3.10).</summary>
    [ObservableProperty]
    public partial double PanX { get; set; } = 0.5;

    [ObservableProperty]
    public partial double PanY { get; set; } = 0.5;

    /// <summary>3.10: 1..4 over the Fill's cover fit, shown as 100%..400%. Fill only, as the pan is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomText), nameof(CanPanX), nameof(CanPanY))]
    public partial double Zoom { get; set; } = PanZoom.MinZoom;

    public string ZoomText => $"{Math.Round(Zoom * 100)}%";

    /// <summary>Spec 3.1: true is Selected only, false is All pieces. Changing it schedules a render and writes the
    /// setting; nothing else is written anywhere.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAllPieces))]
    [NotifyPropertyChangedFor(nameof(IsolateHint))]
    public partial bool IsolatePreview { get; set; }

    /// <summary>3.6 E3: the editable box's text. An existing pack's name saves into it; anything else is a new
    /// pack.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewPack), nameof(PackNameError), nameof(PackNameHint), nameof(CanSave), nameof(CanEditOutside))]
    [NotifyCanExecuteChangedFor(nameof(SaveAndApplyCommand), nameof(SaveOnlyCommand), nameof(EditOutsideCommand))]
    public partial string PackName { get; set; }

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

    public string Title => _sets.Count == 1
        ? $"Edit platforms, {CurrentMap.DisplayName}"
        : $"Edit platforms, {MainViewModel.Count(_sets.Count, "map")}";

    /// <summary>The map the strip is on: the one the preview draws, the file list lists and Start fresh acts on.</summary>
    public MapEntry CurrentMap => Current.Map;

    public string CurrentMapName => Current.Map.DisplayName;

    /// <summary>"2 of 3 maps" (spec 9).</summary>
    public string MapStripText => $"{CurrentIndex + 1} of {MainViewModel.Count(_sets.Count, "map")}";

    /// <summary>Whether the strip is drawn at all: one map is every other way into the editor (spec 9).</summary>
    public bool HasManyMaps => _sets.Count > 1;

    /// <summary>3.2: the "also in" mark of each row whose piece another layout of the folder also draws, keyed by
    /// the row. A row with no entry draws no mark.</summary>
    public Dictionary<PlatformPieceViewModel, string> AlsoInMarks { get; } = [];

    public bool CanPreviousMap => !IsSaving && CurrentIndex > 0;

    public bool CanNextMap => !IsSaving && CurrentIndex < _sets.Count - 1;

    /// <summary>The window's own Cancel closes it and drops the run, which a save part way through a set of maps
    /// must not do: the Cancel that stops that one is the progress line's (spec 9).</summary>
    public bool CanCloseWindow => !IsSaving;

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

    /// <summary>The tone readings, blank or Mixed the way the opacity reading is, in the background editor's own
    /// format.</summary>
    public string SaturationText => !HasTicked ? "" : IsMixed(p => p.Saturation) ? MixedText : Saturation.ToString();

    public string ContrastText => !HasTicked ? "" : IsMixed(p => p.Contrast) ? MixedText : Contrast.ToString();

    public string DarkenText => !HasTicked ? "" : IsMixed(p => p.Darken) ? MixedText : $"{Darken}%";

    public string BlurText => !HasTicked ? "" : IsMixed(p => p.Blur) ? MixedText : Blur.ToString();

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
                // 3.2: the rows are cut from the loaded picture the way the Across or Each pair and the fit row
                // say, so the line reads those out rather than a size.
                if (CanUseFit)
                {
                    var layout = FitAcross ? "across the platforms" : "on each piece";
                    return $"{ticked[0].ReplacementName}, {layout}, {Fit}";
                }

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

    /// <summary>Whether the Fill, Fit, Center and Stretch row can be used: there is a picture to lay (spec 6.2).
    /// The Across or Each pair is always open (3.2 F2).</summary>
    public bool CanUseFit => LoadedPicture is not null;

    /// <summary>Whether the pan and zoom do anything: a loaded picture in Fill, which is the one fit with anything
    /// hanging over the box to move (3.0 E). Laid across the platforms or on each piece alike (3.10).</summary>
    public bool CanPan => CanUseFit && Fit == PictureFit.Fill;

    /// <summary>Whether the picture hangs over its box along X: always once it is zoomed in, and at 100% only
    /// when the box is a different shape from the picture.</summary>
    public bool CanPanX => CanPan && (Zoom > PanZoom.MinZoom || PanOverflow().X > 0);

    public bool CanPanY => CanPan && (Zoom > PanZoom.MinZoom || PanOverflow().Y > 0);

    public bool FitFill
    {
        get => Fit == PictureFit.Fill;
        set { if (value) { Fit = PictureFit.Fill; } }
    }

    public bool FitFit
    {
        get => Fit == PictureFit.Fit;
        set { if (value) { Fit = PictureFit.Fit; } }
    }

    public bool FitCenter
    {
        get => Fit == PictureFit.Center;
        set { if (value) { Fit = PictureFit.Center; } }
    }

    public bool FitStretch
    {
        get => Fit == PictureFit.Stretch;
        set { if (value) { Fit = PictureFit.Stretch; } }
    }

    public bool CanResetImage => TickedRows().Any(p => p.Art != PieceArt.Original || !p.IsDefault);

    /// <summary>Editing outside writes a working copy into the pack, so it needs a pack name that is good enough
    /// to save into.</summary>
    public bool CanEditOutside => HasTicked && PackNameError.Length == 0;

    /// <summary>What the preview says instead of a picture, which is only ever the map having no art of its own.</summary>
    public string EmptyText => CanEdit ? "" : NoFilesText;

    public bool IsNewPack =>
        !PackChoices.Any(p => p.Equals(EffectivePackName, StringComparison.OrdinalIgnoreCase));

    public string EffectivePackName => PackName.Trim();

    public string PackNameError =>
        IsNewPack && !PackNameValidator.IsValid(EffectivePackName, out var error) ? error : "";

    /// <summary>Under the box while the name is a new pack that can be made; the error line takes its place
    /// otherwise. Null hides the line.</summary>
    public string? PackNameHint =>
        IsNewPack && PackNameError.Length == 0 ? BackgroundEditorViewModel.NewPackHint : null;

    /// <summary>Spec 9: one map with no art of its own does not stop the rest of the set being written, so what
    /// Save needs is a map in the set that has something to write.</summary>
    public bool CanSave => CanAddAsNew && PackNameError.Length == 0;

    /// <summary>3.6 E1: the Save rule without the pack name, because Add as new asks for its own.</summary>
    public bool CanAddAsNew => _sets.Any(s => s.Rows.Count > 0) && !IsSaving;

    /// <summary>The map the strip is on: the one the file list lists and the preview draws (spec 9).</summary>
    private MapSet Current => _sets[CurrentIndex];

    /// <summary>Every row of every map: what the ctor, the watcher and the thumbnails walk (spec 9).</summary>
    private List<PlatformPieceViewModel> AllRows() => _sets.SelectMany(s => s.Rows).ToList();

    /// <summary>Every ticked row of every map, which is what a value, a picture and a reset act on: the sliders
    /// and Replace belong to the editor rather than to the map the strip is on (spec 9).</summary>
    private List<PlatformPieceViewModel> TickedRows() => AllRows().Where(p => p.IsTicked).ToList();

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
        foreach (var row in AllRows())
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

    [RelayCommand(CanExecute = nameof(CanPreviousMap))]
    private void PreviousMap() => CurrentIndex--;

    [RelayCommand(CanExecute = nameof(CanNextMap))]
    private void NextMap() => CurrentIndex++;

    /// <summary>Spec 9: Cancel stops the save run between maps, so the map being written is written whole. The
    /// maps already written stay in the pack, and the window closes on them.</summary>
    [RelayCommand(CanExecute = nameof(IsSaving))]
    private void CancelSave() => _saveCts?.Cancel();

    /// <summary>Spec 9: the strip moved to another map, so everything that map owns is read again: its rows, its
    /// Values from line, its ticks and its preview. Nothing the editor owns moves, because the picture, the pan,
    /// the values and the target pack are the set's.</summary>
    partial void OnCurrentIndexChanged(int value)
    {
        ValuesFromText = Current.ValuesFromText;
        HasValuesFrom = Current.HasValuesFrom;
        OnPropertyChanged(nameof(Pieces));
        OnPropertyChanged(nameof(CurrentMap));
        OnPropertyChanged(nameof(CurrentMapName));
        OnPropertyChanged(nameof(MapStripText));
        OnPropertyChanged(nameof(SourceText));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(FilesHeader));
        PreviousMapCommand.NotifyCanExecuteChanged();
        NextMapCommand.NotifyCanExecuteChanged();

        // The sliders and the Image line read the ticks, and this map's are not the last one's.
        OnTickedChanged();
        SchedulePreview();
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

    partial void OnSaturationChanged(int value) => FanOutTone(row => row.Saturation = value);

    partial void OnContrastChanged(int value) => FanOutTone(row => row.Contrast = value);

    partial void OnDarkenChanged(int value) => FanOutTone(row => row.Darken = value);

    partial void OnBlurChanged(int value) => FanOutTone(row => row.Blur = value);

    /// <summary>The tone sliders move every ticked row, as Opacity and Hue do, and may make Reset worth pressing.</summary>
    private void FanOutTone(Action<PlatformPieceViewModel> set)
    {
        if (_syncing)
        {
            return;
        }

        FanOut(set);
        OnPropertyChanged(nameof(CanResetImage));
        ResetImageCommand.NotifyCanExecuteChanged();
        SchedulePreview();
    }

    /// <summary>The switch and the pan both mean the same thing: cut the ticked rows out of the loaded picture
    /// again. Nothing is loaded until a Replace or a record, and the record puts all three on at once.</summary>
    partial void OnFitAcrossChanged(bool value) => ScheduleRecut();

    partial void OnFitChanged(PictureFit value) => ScheduleRecut();

    partial void OnPanXChanged(double value) => ScheduleRecut();

    partial void OnPanYChanged(double value) => ScheduleRecut();

    partial void OnZoomChanged(double value) => ScheduleRecut();

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
    private IReadOnlyList<PlatformPieceViewModel> BuildPieces(MapSet set)
    {
        var pieces = new List<PlatformPieceViewModel>();
        foreach (var relativePath in set.Map.LayoutFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var ticked = _request.OnlyFile is null
                || string.Equals(relativePath, _request.OnlyFile, StringComparison.OrdinalIgnoreCase);
            var entry = set.Record?.Entry(set.Map.FolderName, relativePath);

            // A working copy is a file another program owns, so its row opens on that file the way 2.4 opened it.
            if (entry is null || entry.Art == PlatformArt.WorkingCopy)
            {
                if (ResolveAsset(relativePath) is { } source)
                {
                    pieces.Add(new PlatformPieceViewModel(relativePath, source, ticked));
                }

                continue;
            }

            if (BuildRecordPiece(set, relativePath, entry, ticked) is { } row)
            {
                pieces.Add(row);
            }
        }

        // 3.0 E: a row is called "Piece 1", "Piece 2" and so on, which is the order it is listed in.
        for (var i = 0; i < pieces.Count; i++)
        {
            pieces[i].Number = i + 1;
        }

        // 3.2: a piece another layout of the folder also draws carries a quiet mark saying which.
        foreach (var piece in pieces)
        {
            var mark = _request.Catalog?.AlsoInText(set.Map, piece.RelativePath) ?? "";
            if (mark.Length > 0)
            {
                AlsoInMarks[piece] = mark;
            }
        }

        return pieces;
    }

    /// <summary>Spec 5.2: the record's values were worked out from the untouched art, so the row starts from the
    /// Default pack's copy of the piece, or the game's when the library has no Default pack. Null when neither
    /// is there, which is the unresolved file 2.4 leaves out of the list.</summary>
    private PlatformPieceViewModel? BuildRecordPiece(
        MapSet set, string relativePath, PlatformPieceEntry entry, bool ticked)
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
            var saved = set.SourcePack is { } pack ? Path.Combine(pack.FullPath, relativePath) : original;
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
            Saturation = entry.Saturation ?? PlatformPieceViewModel.DefaultTone,
            Contrast = entry.Contrast ?? PlatformPieceViewModel.DefaultTone,
            Darken = entry.Darken ?? PlatformPieceViewModel.DefaultTone,
            Blur = entry.Blur ?? PlatformPieceViewModel.DefaultTone,
            Note = note,
        };
        set.RecordRows.Add((row, entry));
        return row;
    }

    /// <summary>Spec 9: one map's side of the editor, built the way Part A built the only map there was. The pack
    /// the values come from is the one the editor was opened with, and otherwise the pack this map's own files
    /// were matched to, which is per map because the maps of a set need not come from one pack (spec 5.1).</summary>
    private MapSet BuildSet(
        MapEntry map, IReadOnlyList<Pack> packs, IReadOnlyDictionary<string, MapStatus> statuses)
    {
        var sourcePack = _request.Pack
            ?? _request.SourcePack
            ?? SourcePackFinder.ForPlatforms(map, statuses.GetValueOrDefault(map.FolderName), packs);
        var record = sourcePack is { } pack ? PlatformEditRecord.Load(pack.FullPath) : null;
        var set = new MapSet(map, record, sourcePack, CurrentBackground(_services, map));
        set.Rows = BuildPieces(set);
        if (sourcePack is { } from && record?.Map(map.FolderName) is { } saved)
        {
            set.HasValuesFrom = true;
            set.ValuesFromText = $"Values from {from.Name}, saved {saved.SavedAt.ToLocalTime():d MMM HH:mm}.";
        }

        return set;
    }

    /// <summary>The 2.4 resolution of one piece: the pack the editor was opened with, then the game.</summary>
    private string? ResolveAsset(string relativePath) =>
        new AssetSources(_services.GamePath, _request.Pack?.FullPath).ResolveAsset(relativePath);

    /// <summary>Spec 5.2: the pictures the record names are fitted to their pieces and the files the pack holds
    /// are hashed against what the record was written for, both off the UI thread. The rows only change here,
    /// when all of that is done, and the preview waits on this task before it draws.</summary>
    private async Task LoadRecordArtAsync()
    {
        // The constructor stores this method's task in _recordArt, and the first preview awaits that field. A
        // pack with no record has nothing to load, so without this yield the body would run to OnImageChanged
        // synchronously, inside the call, and schedule a render before the field is assigned: the render then
        // awaited null and the app showed "Object reference not set to an instance of an object". Yielding once
        // returns to the constructor first, so the field is set before any continuation runs.
        await Task.Yield();
        foreach (var set in _sets)
        {
            await LoadRecordArtAsync(set);
        }

        await LoadRecordSpanAsync();
        OnImageChanged();
    }

    /// <summary>One map's record art, which is the whole of it for every way in that opens on one map.</summary>
    private async Task LoadRecordArtAsync(MapSet set)
    {
        if (set.RecordRows.Count == 0)
        {
            return;
        }

        var packRoot = set.SourcePack?.FullPath;
        var rows = set.RecordRows.Select(r => (r.Row, r.Entry, HasNote: r.Row.Note.Length > 0)).ToList();
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
                        // The saved fit mode, not always Fill: a Fit or Center piece reopens as saved (3.2 F1).
                        fitted = PieceFitter.Fit(
                            BackgroundFitter.LoadSource(picture),
                            BackgroundFitter.LoadSource(row.OriginalPath),
                            PictureFits.Options(
                                entry.Fit ?? PictureFit.Fill,
                                entry.PanX ?? 0.5,
                                entry.PanY ?? 0.5,
                                zoom: PanZoom.ClampZoom(entry.Zoom ?? PanZoom.MinZoom)));
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
    }

    /// <summary>Spec 5.2 and 6.2: the record's Across rows are one picture laid across the platforms with the pan
    /// it was saved with, so the picture, the pan and the switch go on together and one cut covers all of them.
    /// The rows the record saved as EachPiece keep the fit they were loaded with.</summary>
    private async Task LoadRecordSpanAsync()
    {
        var spanning = _sets
            .SelectMany(s => s.RecordRows)
            .Where(r => r.Entry.Art == PlatformArt.Across
                && r.Entry.Picture is { Length: > 0 } picture
                && File.Exists(picture))
            .ToList();
        if (spanning.Count == 0)
        {
            // The Across or Each pair is open with no picture loaded, so a record that saved its pictures piece
            // by piece shows that choice rather than the default (3.2 F2).
            if (_sets.SelectMany(s => s.RecordRows).FirstOrDefault(r => r.Entry.Art == PlatformArt.EachPiece
                && r.Entry.Picture is { Length: > 0 }).Entry is { } each)
            {
                // 3.10: the pan and zoom apply on each piece too, so the sliders open where the record left them.
                _restoringFit = true;
                FitAcross = false;
                Fit = each.Fit ?? PictureFit.Fill;
                PanX = Math.Clamp(each.PanX ?? 0.5, 0, 1);
                PanY = Math.Clamp(each.PanY ?? 0.5, 0, 1);
                Zoom = PanZoom.ClampZoom(each.Zoom ?? PanZoom.MinZoom);
                _restoringFit = false;
            }

            return;
        }

        // The picture and the pan belong to the editor, so the first map of the set that saved one is what the
        // switch and the drag carry on from (spec 9).
        var entry = spanning[0].Entry;
        _restoringFit = true;
        FitAcross = true;
        Fit = entry.Fit ?? PictureFit.Fill;
        PanX = Math.Clamp(entry.PanX ?? 0.5, 0, 1);
        PanY = Math.Clamp(entry.PanY ?? 0.5, 0, 1);
        Zoom = PanZoom.ClampZoom(entry.Zoom ?? PanZoom.MinZoom);
        _restoringFit = false;

        if (!await LoadPictureAsync(entry.Picture!))
        {
            return;
        }

        // A map that saved a different picture keeps the one it saved: what is cut again here is what the record
        // says, map by map, not the first map's picture laid over the rest of the set.
        foreach (var group in spanning.GroupBy(r => r.Entry.Picture!, StringComparer.OrdinalIgnoreCase))
        {
            var rows = group.Select(r => r.Row).ToList();
            if (group.Key.Equals(entry.Picture, StringComparison.OrdinalIgnoreCase))
            {
                await CutAsync(WorkFor(rows), LoadedPicture!, group.Key);
            }
            else if (await LoadSourceAsync(group.Key) is { } picture)
            {
                await CutAsync(WorkFor(rows), picture, group.Key);
            }
        }
    }

    /// <summary>A picture a record names, decoded off the UI thread, or null with the line already on the panel.
    /// The editor's own picture is loaded by LoadPictureAsync; this one is only ever cut with.</summary>
    private async Task<BitmapSource?> LoadSourceAsync(string path)
    {
        try
        {
            return await Task.Run(() => BackgroundFitter.LoadSource(path));
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            ImageError = $"Could not read {Path.GetFileName(path)}. The pieces are showing their own art.";
            return null;
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
            row.ResetValues();
            row.Note = "";
        }

        SyncValues(null);
        _syncing = false;

        // Spec 9: Start fresh is about the map the strip is on, so the line the strip reads back for it goes too.
        Current.HasValuesFrom = false;
        HasValuesFrom = false;
        ClearPicture();
        ImageError = "";
        OnImageChanged();
        RaiseValueTexts();
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

    [RelayCommand]
    private void ResetSaturation()
    {
        Saturation = PlatformPieceViewModel.DefaultTone;
        FanOutTone(row => row.Saturation = PlatformPieceViewModel.DefaultTone);
    }

    [RelayCommand]
    private void ResetContrast()
    {
        Contrast = PlatformPieceViewModel.DefaultTone;
        FanOutTone(row => row.Contrast = PlatformPieceViewModel.DefaultTone);
    }

    [RelayCommand]
    private void ResetDarken()
    {
        Darken = PlatformPieceViewModel.DefaultTone;
        FanOutTone(row => row.Darken = PlatformPieceViewModel.DefaultTone);
    }

    [RelayCommand]
    private void ResetBlur()
    {
        Blur = PlatformPieceViewModel.DefaultTone;
        FanOutTone(row => row.Blur = PlatformPieceViewModel.DefaultTone);
    }

    [RelayCommand]
    private void ResetPanX() => PanX = 0.5;

    [RelayCommand]
    private void ResetPanY() => PanY = 0.5;

    [RelayCommand]
    private void ResetZoom() => Zoom = PanZoom.MinZoom;

    /// <summary>3.10: a double-click on the preview puts the picture back in the middle and leaves the zoom.</summary>
    public void CentrePan()
    {
        if (!CanPan)
        {
            return;
        }

        PanX = 0.5;
        PanY = 0.5;
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
        var ticked = TickedRows();
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
        OnPropertyChanged(nameof(CanPanX));
        OnPropertyChanged(nameof(CanPanY));
        OnPropertyChanged(nameof(ImageText));
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
        OnPropertyChanged(nameof(CanPanX));
        OnPropertyChanged(nameof(CanPanY));
        OnPropertyChanged(nameof(ImageText));
    }

    /// <summary>Spec 6.2: every row named is cut from the loaded picture again with the switch and the pan as
    /// they are now. Across, the picture is laid over the platforms' own box once and each row takes the part
    /// under its largest placement; a row the stage never draws is fitted on its own instead, and says so. The
    /// cutting is the slow part, so it happens off the UI thread and no row changes until all of them have one.</summary>
    private Task RecutAsync(IReadOnlyList<PlatformPieceViewModel>? only = null) =>
        LoadedPicture is { } picture && LoadedPicturePath is { } path
            ? CutAsync(WorkFor(only), picture, path)
            : Task.CompletedTask;

    /// <summary>The rows each map of the set has to cut: the ones named, gathered map by map, or every ticked row
    /// of every map. A map with none of them is left out, because the cut is per map's own stage (spec 9).</summary>
    private List<(MapSet Set, List<PlatformPieceViewModel> Rows)> WorkFor(
        IReadOnlyList<PlatformPieceViewModel>? only)
    {
        var work = new List<(MapSet, List<PlatformPieceViewModel>)>();
        foreach (var set in _sets)
        {
            var rows = only is null
                ? set.Rows.Where(p => p.IsTicked).ToList()
                : set.Rows.Where(only.Contains).ToList();
            if (rows.Count > 0)
            {
                work.Add((set, rows));
            }
        }

        return work;
    }

    /// <summary>Spec 9: the cut itself, over each map's own box and each map's own placements, so one picture
    /// laid across a set of maps is laid across every one of their stages rather than the first one's.</summary>
    private async Task CutAsync(
        List<(MapSet Set, List<PlatformPieceViewModel> Rows)> work, BitmapSource picture, string path)
    {
        if (work.Count == 0)
        {
            return;
        }

        var across = FitAcross;
        var pan = PictureFits.Options(Fit, PanX, PanY, zoom: Zoom);

        // 3.10: on each piece the pan and zoom apply inside each piece's own box, as they do across the stage.
        var options = pan;
        List<(PlatformPieceViewModel Row, BitmapSource Fitted, bool Across, string Note)> cut;
        try
        {
            cut = await Task.Run(() =>
            {
                var results = new List<(PlatformPieceViewModel, BitmapSource, bool, string)>();
                foreach (var (set, rows) in work)
                {
                    var level = set.Map.BaseLevel;
                    var box = SpanFitter.Box(level);
                    foreach (var row in rows)
                    {
                        var piece = BackgroundFitter.LoadSource(row.SourcePath);

                        // Fit and Center show the piece's original art where the picture does not reach (3.2 F1).
                        var original = row.WorkingCopyPath is null ? piece : BackgroundFitter.LoadSource(row.OriginalPath);
                        if (!across || box is not { } stage)
                        {
                            results.Add((row, PieceFitter.Fit(picture, piece, options, original), false, ""));
                            continue;
                        }

                        var placements = SpanFitter.Placements(level, row.RelativePath, piece.PixelWidth, piece.PixelHeight);
                        if (placements.Count == 0)
                        {
                            results.Add((
                                row,
                                PieceFitter.Fit(picture, piece, options, original),
                                false,
                                $"{row.FileName} not on this stage, fitted on its own."));
                            continue;
                        }

                        // The same file drawn twice is one asset with two placements, and the picture can only be
                        // cut for one of them, so the largest is the one the user is looking at (spec 6.1).
                        var note = placements.Count > 1
                            ? $"{row.FileName} drawn {placements.Count} times, cut from the largest."
                            : "";
                        results.Add((row, SpanFitter.Cut(picture, stage, pan, SpanFitter.Largest(placements)!, piece, original), true, note));
                    }
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

    /// <summary>Spec 6.2: dragging the preview moves the picture. The delta arrives in the stage's own 1280 by 720
    /// pixels, is carried into the box the picture is fitted to, and <see cref="PanZoom.Drag"/> turns it into pan
    /// units by the overflow there. A picture with no overflow one way does not move that way, and the cut that
    /// follows is throttled. 3.10: on each piece the box is the first ticked piece's own.</summary>
    public void DragPan(double dxStagePixels, double dyStagePixels)
    {
        if (!CanPan || LoadedPicture is not { } picture || PanArea() is not { } area)
        {
            return;
        }

        var (panX, panY) = PanZoom.Drag(
            picture.PixelWidth,
            picture.PixelHeight,
            area.FitWidth,
            area.FitHeight,
            Zoom,
            PanX,
            PanY,
            dxStagePixels * area.FitPerStageX,
            dyStagePixels * area.FitPerStageY);
        PanX = panX;
        PanY = panY;
    }

    /// <summary>3.10: the wheel over the preview zooms 10% a notch about the stage point under the cursor, so the
    /// part of the picture under it stays there, and the pan follows. The zoom holds to 100%..400%.</summary>
    public void WheelZoom(double stageX, double stageY, double notches)
    {
        if (!CanPan || notches == 0 || LoadedPicture is not { } picture || PanArea() is not { } area)
        {
            return;
        }

        var (zoom, panX, panY) = PanZoom.ZoomAbout(
            picture.PixelWidth,
            picture.PixelHeight,
            area.FitWidth,
            area.FitHeight,
            Zoom,
            PanX,
            PanY,
            Zoom * Math.Pow(PanZoom.WheelStep, notches),
            (stageX - area.StageLeft) * area.FitPerStageX,
            (stageY - area.StageTop) * area.FitPerStageY);
        Zoom = zoom;
        PanX = panX;
        PanY = panY;
    }

    /// <summary>The box the picture is fitted to and how the preview maps onto it. Across, the platform box in
    /// level units. On each piece, the first ticked piece's own pixels, placed where the stage draws that piece
    /// largest; a piece the stage never draws is taken as drawn at one level unit a pixel at the camera's corner.
    /// The preview draws the camera's part of the level into the panel, so a stage pixel is that many level
    /// units. Null when there is no box or no camera.</summary>
    private (double FitWidth, double FitHeight, double StageLeft, double StageTop, double FitPerStageX, double FitPerStageY)? PanArea()
    {
        var level = CurrentMap.BaseLevel;
        var (_, viewport) = FocusFor(level);
        if ((viewport ?? level.Camera) is not { W: > 0, H: > 0 } camera)
        {
            return null;
        }

        Rect box;
        double fitWidth;
        double fitHeight;
        if (FitAcross)
        {
            if (SpanFitter.Box(level) is not { Width: > 0, Height: > 0 } stage)
            {
                return null;
            }

            box = stage;
            (fitWidth, fitHeight) = (stage.Width, stage.Height);
        }
        else
        {
            if (TickedRows().FirstOrDefault() is not { Width: > 0, Height: > 0 } row)
            {
                return null;
            }

            (fitWidth, fitHeight) = (row.Width, row.Height);
            box = SpanFitter.Largest(SpanFitter.Placements(level, row.RelativePath, row.Width, row.Height)) is { } placement
                && placement.Bounds is { Width: > 0, Height: > 0 } bounds
                ? bounds
                : new Rect(camera.X, camera.Y, row.Width, row.Height);
        }

        var unitsPerStageX = camera.W / MapCompositor.PanelWidth;
        var unitsPerStageY = camera.H / MapCompositor.PanelHeight;
        return (
            fitWidth,
            fitHeight,
            (box.X - camera.X) / unitsPerStageX,
            (box.Y - camera.Y) / unitsPerStageY,
            unitsPerStageX * fitWidth / box.Width,
            unitsPerStageY * fitHeight / box.Height);
    }

    /// <summary>How far the loaded picture hangs over its box at 100%, which is what the pan sliders can move along
    /// before any zoom.</summary>
    private (double X, double Y) PanOverflow() =>
        LoadedPicture is { } picture && PanArea() is { } area
            ? PanZoom.Overflow(picture.PixelWidth, picture.PixelHeight, area.FitWidth, area.FitHeight, PanZoom.MinZoom)
            : (0, 0);

    /// <summary>Back to the pieces' own art. A ticked row holding a working copy is asked about first, because
    /// the reset writes over a file in the pack that another program may still have open (spec 6).</summary>
    [RelayCommand(CanExecute = nameof(CanResetImage))]
    private async Task ResetImageAsync()
    {
        var failed = false;
        var copies = TickedRows().Where(p => p.Art == PieceArt.WorkingCopy).ToList();
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
                    : $"{copies.Count} files in {where} were changed in another app. Reset replaces them with the pieces' original art.",
                copies.Count == 1 ? "Reset" : $"Reset {MainViewModel.Count(copies.Count, "file")}"))
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

        foreach (var row in TickedRows().Where(p => p.Art != PieceArt.Original))
        {
            row.ResetArt();
            SetFitNote(row, "");
            await RefreshThumbnailAsync(row);
        }

        // Nothing is made of the picture any more, so the switch has nothing left to lay (spec 6.2).
        if (AllRows().All(p => p.Art != PieceArt.Replacement))
        {
            ClearPicture();
        }

        if (!failed)
        {
            ImageError = "";
        }

        // 3.10: Reset puts every slider of the Image section back too: the ticked rows' values and the pan and zoom.
        FanOut(row => row.ResetValues());
        _syncing = true;
        SyncValues(TickedRows().FirstOrDefault());
        _syncing = false;
        _restoringFit = true;
        PanX = 0.5;
        PanY = 0.5;
        Zoom = PanZoom.MinZoom;
        _restoringFit = false;
        RaiseValueTexts();
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
        var rows = Pieces;
        var model = _services.LevelData.Model;
        var masks = await Task.Run(() => PlatformPieceViewModel.SeamMasks(rows, model, out _));
        foreach (var row in Pieces.Where(p => p.IsTicked).ToList())
        {
            var copy = Path.Combine(folder, row.FileName);
            // A copy already there at the piece's own values is the file to hand over as it stands; anything
            // else is written out first, because what the user edits has to be what the editor is showing.
            if (!File.Exists(copy) || row.Art == PieceArt.Replacement || !row.IsDefault)
            {
                try
                {
                    await Task.Run(() => row.WriteResult(copy, masks.GetValueOrDefault(row.RelativePath)));
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
            SyncValues(first);
        }

        _syncing = false;
        RaiseValueTexts();
        OnImageChanged();
    }

    /// <summary>The pack folder this map's working copies go in, made if it is not there, or null with
    /// <paramref name="error"/> set. Save builds the same path, so the two write into the one place.</summary>
    private string? EnsurePackFolder(out string packName, out string error)
    {
        packName = EffectivePackName;
        error = "";
        var packRoot = PackScanner.PackRootFor(_services.LibraryPath, packName);
        // TryCreate refuses a name another pack already holds, which is not a failure here: a new name the user
        // already edited into once is the pack this one goes in too.
        if (IsNewPack && !Directory.Exists(packRoot) && !PackCreator.TryCreate(_services.LibraryPath, packName, out error))
        {
            return null;
        }

        var folder = Path.Combine(packRoot, CurrentMap.FolderName);
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
            // The pack exists now, so the list has it and Save writes there rather than making a second one.
            // A new list can clear the editable box's text, so the name goes back in after it.
            PackChoices = PackChoices.Append(packName).ToList();
            OnPropertyChanged(nameof(PackChoices));
            PackName = packName;
            OnPropertyChanged(nameof(IsNewPack));
            OnPropertyChanged(nameof(PackNameHint));
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
        foreach (var row in AllRows())
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

        foreach (var row in AllRows().Where(p => p.Replacement is null && IsWatched(p.SourcePath)))
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
            case nameof(PlatformPieceViewModel.Saturation):
            case nameof(PlatformPieceViewModel.Contrast):
            case nameof(PlatformPieceViewModel.Darken):
            case nameof(PlatformPieceViewModel.Blur):
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
            SyncValues(first);
        }

        _syncing = false;
        OnPropertyChanged(nameof(FilesHeader));
        OnPropertyChanged(nameof(TickedCount));
        OnPropertyChanged(nameof(HasTicked));
        OnPropertyChanged(nameof(CanAdjust));
        RaiseValueTexts();

        // On each piece the pan box is the first ticked piece's, so whether there is room to pan can change.
        OnPropertyChanged(nameof(CanPanX));
        OnPropertyChanged(nameof(CanPanY));
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

        // In All pieces a tick only moves the sliders, as in 2.3. In Selected only it changes the picture, so the
        // render is asked for; the 60 ms throttle collapses the run of ticks All, None and Solo produce.
        if (IsolatePreview)
        {
            SchedulePreview();
        }
    }

    /// <summary>A row moved on its own: the shared readings may have gone Mixed, and the map did change.</summary>
    private void OnRowValuesChanged()
    {
        RaiseValueTexts();
        OnPropertyChanged(nameof(CanResetImage));
        ResetImageCommand.NotifyCanExecuteChanged();
        SchedulePreview();
    }

    /// <summary>The shared sliders read one row, or the defaults for none. Callers hold _syncing so nothing fans
    /// back out.</summary>
    private void SyncValues(PlatformPieceViewModel? from)
    {
        Opacity = from?.Opacity ?? PlatformPieceViewModel.DefaultOpacity;
        Hue = from?.Hue ?? PlatformPieceViewModel.DefaultHue;
        Saturation = from?.Saturation ?? PlatformPieceViewModel.DefaultTone;
        Contrast = from?.Contrast ?? PlatformPieceViewModel.DefaultTone;
        Darken = from?.Darken ?? PlatformPieceViewModel.DefaultTone;
        Blur = from?.Blur ?? PlatformPieceViewModel.DefaultTone;
    }

    /// <summary>Every slider reading, which can go Mixed or blank whenever the ticks or a row move.</summary>
    private void RaiseValueTexts()
    {
        OnPropertyChanged(nameof(OpacityText));
        OnPropertyChanged(nameof(HueText));
        OnPropertyChanged(nameof(SaturationText));
        OnPropertyChanged(nameof(ContrastText));
        OnPropertyChanged(nameof(DarkenText));
        OnPropertyChanged(nameof(BlurText));
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
        foreach (var row in TickedRows())
        {
            set(row);
        }

        _syncing = false;
    }

    private bool IsMixed(Func<PlatformPieceViewModel, int> value) =>
        TickedRows().Select(value).Distinct().Count() > 1;

    /// <summary>"Save and apply": the recoloured sets go into the pack and on into the game. The game write
    /// itself is the shell's business, because it needs the boundary, the snapshot and the undo (spec 8, 3.0 E).
    /// Spec 9: one save writes every map of the set, one map at a time, and the question is asked once for all of
    /// them. Cancel is read between maps only, so a map is written whole or not at all.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAndApplyAsync() => SaveAsync(apply: true);

    /// <summary>"Save only": the same write into the library, with nothing said to the game (3.0 E).</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveOnlyAsync() => SaveAsync(apply: false);

    /// <summary>3.6 E1: "Add as new" asks for a fresh pack's name, then runs Save only into it. 3.9.2: it opens
    /// the background editor's popup instead, so an existing pack can be picked too; one that already has
    /// platforms for any of the maps is refused there, so no Replace question can come up.</summary>
    [RelayCommand(CanExecute = nameof(CanAddAsNew))]
    private async Task AddAsNewAsync()
    {
        var popup = AddAsNewViewModel.ForPlatforms(
            PackChoices, EffectivePackName, TakenPackNames(), MapsWithPlatforms);
        if (!_dialogs.AddAsNew(popup))
        {
            return;
        }

        var packBefore = PackName;
        PackName = popup.EffectivePackName;
        await SaveAsync(apply: false);
        if (Saved is null)
        {
            // The save failed and the editor stays open, so it goes back to what its own boxes say.
            PackName = packBefore;
        }
    }

    /// <summary>3.9.2: the maps being saved that a pack already has platforms of its own for, by display name.
    /// The same check SaveAsync asks the Replace question on.</summary>
    private IReadOnlyList<string> MapsWithPlatforms(string pack)
    {
        var packRoot = PackScanner.PackRootFor(_services.LibraryPath, pack);
        return _sets.Where(s => HasOwnFiles(Path.Combine(packRoot, s.Map.FolderName)))
            .Select(s => s.Map.DisplayName)
            .ToList();
    }

    /// <summary>Every pack name a new pack must not reuse: the list's, plus folders on disk the snapshot has not
    /// picked up.</summary>
    private List<string> TakenPackNames()
    {
        var names = PackChoices.ToList();
        var root = PackScanner.PacksRoot(_services.LibraryPath);
        try
        {
            if (Directory.Exists(root))
            {
                names.AddRange(Directory.EnumerateDirectories(root).Select(Path.GetFileName).OfType<string>());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable packs folder leaves the list's names, which is what the editor saves against anyway.
        }

        return names;
    }

    private async Task SaveAsync(bool apply)
    {
        var packRoot = PackScanner.PackRootFor(_services.LibraryPath, EffectivePackName);
        var replacing = _sets.Count(s => HasOwnFiles(Path.Combine(packRoot, s.Map.FolderName)));
        if (replacing > 0
            && !_dialogs.Confirm(
                "Replace platforms?",
                _sets.Count == 1
                    ? $"{EffectivePackName} already has platforms for {CurrentMap.DisplayName}. Replace them?"
                    : $"{EffectivePackName} already has platforms for {MainViewModel.Count(replacing, "map")}."
                        + " Replace them?",
                _sets.Count == 1 ? "Replace" : $"Replace {MainViewModel.Count(replacing, "map")}"))
        {
            return;
        }

        var (panX, panY, zoom) = (PanX, PanY, Zoom);
        var fit = Fit;
        var written = new List<MapEntry>();
        using var cts = new CancellationTokenSource();
        _saveCts = cts;
        IsSaving = true;
        try
        {
            // One record for the run: every map of the set goes into the one pack, and it is written after each
            // map so a cancelled run leaves the record saying exactly what is in the pack.
            var record = await Task.Run(() => PlatformEditRecord.Load(packRoot));
            foreach (var set in _sets)
            {
                if (cts.IsCancellationRequested)
                {
                    break;
                }

                SaveProgressText =
                    $"Saving {written.Count + 1} of {MainViewModel.Count(_sets.Count, "map")} into {EffectivePackName}";

                // Every row, ticked or not: what Save leaves behind is the whole set, not the part worked on.
                var rows = set.Rows;
                var folder = set.Map.FolderName;
                var model = _services.LevelData.Model;
                await Task.Run(() =>
                {
                    var masks = PlatformPieceViewModel.SeamMasks(rows, model, out var missing);
                    SeamFixSkipped |= missing;
                    foreach (var row in rows)
                    {
                        row.CopyOrWriteResult(Path.Combine(packRoot, row.RelativePath), masks.GetValueOrDefault(row.RelativePath));
                    }

                    record.SetMap(folder, DateTimeOffset.Now, EntriesFor(rows, packRoot, panX, panY, fit, zoom));
                    record.Save(packRoot);
                });
                written.Add(set.Map);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            _dialogs.Error("Could not save the platforms", ex.Message);
            return;
        }
        finally
        {
            IsSaving = false;
            SaveProgressText = "";
            _saveCts = null;
        }

        if (written.Count == 0)
        {
            // Cancelled before the first map was written, so there is nothing in the pack to apply or to say.
            return;
        }

        Saved = new PlatformSave(
            EffectivePackName, Path.Combine(packRoot, written[0].FolderName), apply, written);
        CloseRequested?.Invoke(true);
    }

    /// <summary>Whether a destination folder already holds work of the pack's own, which is what the Replace
    /// question is about: a working copy this editor put there is not the pack's own work.</summary>
    private bool HasOwnFiles(string folder) =>
        Directory.Exists(folder) && Directory.EnumerateFiles(folder).Any(f => !IsWorkingCopy(f));

    /// <summary>Builds the record entry set for the rows just written into packRoot (hash from the written file).
    /// A row whose file is not there was not written, so the record says nothing about it. The pan belongs to the
    /// editor rather than to a row, so an Across row writes down the one the picture was laid with (spec 6.2).</summary>
    internal static Dictionary<string, PlatformPieceEntry> EntriesFor(
        IReadOnlyList<PlatformPieceViewModel> rows, string packRoot, double panX, double panY,
        PictureFit fit = PictureFit.Fill,
        double zoom = PanZoom.MinZoom)
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
                entry.Saturation = row.Saturation;
                entry.Contrast = row.Contrast;
                entry.Darken = row.Darken;
                entry.Blur = row.Blur;
                if (entry.Art is PlatformArt.EachPiece or PlatformArt.Across)
                {
                    // 3.10: the pan and zoom apply on each piece as well as across the platforms.
                    entry.Picture = row.ReplacementPath;
                    entry.Fit = fit;
                    entry.PanX = panX;
                    entry.PanY = panY;
                    entry.Zoom = zoom;
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
        var level = CurrentMap.BaseLevel;
        var background = Current.BackgroundPath;
        var (focus, viewport) = FocusFor(level);
        var model = _services.LevelData.Model;
        var reading = "";
        try
        {
            // The record's pictures are what some of the rows are made of, so the first render waits for them.
            await _recordArt;
            await Task.Run(
                () =>
                {
                    // The preview shows the corrected files, the same ones Save writes (3.2 O1).
                    var masks = PlatformPieceViewModel.SeamMasks(rows, model, out _);
                    foreach (var row in rows)
                    {
                        ct.ThrowIfCancellationRequested();
                        reading = row.FileName;
                        row.CopyOrWriteResult(Path.Combine(root, row.RelativePath), masks.GetValueOrDefault(row.RelativePath));
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
