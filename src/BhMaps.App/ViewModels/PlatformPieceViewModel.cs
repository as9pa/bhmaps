using System.Windows.Media;
using System.Windows.Media.Imaging;
using BhMaps.Core.Imaging;
using BhMaps.Core.LevelData;
using BhMaps.Core.Packs;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BhMaps.App.ViewModels;

/// <summary>Where a row's picture is coming from right now (spec 2).</summary>
public enum PieceArt
{
    /// <summary>The piece's own art: the file the request's set holds.</summary>
    Original,

    /// <summary>A picture the user picked, fitted to the piece and held in memory until Save.</summary>
    Replacement,

    /// <summary>A file in a pack's set folder that another program may be editing.</summary>
    WorkingCopy,
}

/// <summary>One piece of the map's set inside the editor: its own Opacity and Hue, whether it is ticked, and
/// the source its result is made from (spec 3). The row never writes anything; the editor asks it for its
/// result when it renders or saves.</summary>
public partial class PlatformPieceViewModel : ObservableObject
{
    public const int DefaultOpacity = 100;
    public const int DefaultHue = 0;

    public PlatformPieceViewModel(string relativePath, string originalPath, bool ticked)
    {
        RelativePath = relativePath;
        OriginalPath = originalPath;
        FileName = Path.GetFileName(relativePath);
        IsTicked = ticked;
        (Width, Height) = Measure(originalPath);
    }

    public string RelativePath { get; }

    public string FileName { get; }

    /// <summary>Where the piece sits in the list, from 1. The editor numbers the rows as it builds them.</summary>
    public int Number { get; internal set; }

    /// <summary>3.0 E: what the row is called, with the file name as the line under it.</summary>
    public string Label => $"Piece {Number}";

    /// <summary>"Piece 2, platform_bm1.png": the whole row in one line, for the screen reader.</summary>
    public string RowName => $"{Label}, {FileName}";

    public string OriginalPath { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>True for a row the editor opened from a pack's record rather than from the 2.4 resolution, so
    /// Start fresh knows which rows have an original path to put back (spec 5.4).</summary>
    public bool LoadedFromRecord { get; internal set; }

    [ObservableProperty]
    public partial bool IsTicked { get; set; }

    /// <summary>The line under the readout for what the row could not do: empty hides it (spec 5.2).</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Readout), nameof(IsDefault))]
    public partial int Opacity { get; set; } = DefaultOpacity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Readout), nameof(IsDefault))]
    public partial int Hue { get; set; } = DefaultHue;

    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourcePath), nameof(ImageText))]
    public partial PieceArt Art { get; private set; }

    /// <summary>The fitted picture when <see cref="Art"/> is Replacement; frozen.</summary>
    public BitmapSource? Replacement { get; private set; }

    /// <summary>The picked file's name, for the Image value line.</summary>
    public string? ReplacementName { get; private set; }

    /// <summary>Full path of the picture Replace loaded for this row; null for own art and working copies.</summary>
    public string? ReplacementPath { get; private set; }

    /// <summary>True when the replacement was cut from one picture laid across the whole stage rather than
    /// fitted to this piece on its own (spec 6.2).</summary>
    public bool Across { get; private set; }

    public string? WorkingCopyPath { get; private set; }

    public string? WorkingCopyPack { get; private set; }

    public bool IsDefault => Opacity == DefaultOpacity && Hue == DefaultHue;

    /// <summary>The sign is part of the reading: "+140" is a turn one way and "-30" the other, and "0" is neither.</summary>
    public string HueText => Hue > 0 ? $"+{Hue}" : Hue.ToString();

    public string Readout => $"{Opacity}%, {HueText}";

    /// <summary>The file the result is made from: the working copy when there is one, else the original art.
    /// A replacement has no file; callers check <see cref="Replacement"/> first.</summary>
    public string SourcePath => WorkingCopyPath ?? OriginalPath;

    /// <summary>This row's part of the Image value line (spec 4).</summary>
    public string ImageText => Art switch
    {
        PieceArt.Replacement => $"{ReplacementName}, fitted to {Width} by {Height}",
        PieceArt.WorkingCopy => $"in {WorkingCopyPack}",
        _ => "The piece's own art",
    };

    /// <summary>The record's art kind for this row's current state.</summary>
    public PlatformArt ArtKind => Art switch
    {
        PieceArt.WorkingCopy => PlatformArt.WorkingCopy,
        PieceArt.Replacement => Across ? PlatformArt.Across : PlatformArt.EachPiece,
        _ => PlatformArt.Own,
    };

    public void SetReplacement(BitmapSource fitted, string pickedFileName, string sourcePath) =>
        SetReplacement(fitted, pickedFileName, sourcePath, across: false);

    /// <summary>The picture this row is showing now, and whether it was cut from the picture laid across the
    /// platforms, which is what the record writes down (spec 6.2).</summary>
    public void SetReplacement(BitmapSource fitted, string pickedFileName, string sourcePath, bool across)
    {
        Replacement = fitted;
        ReplacementName = pickedFileName;
        ReplacementPath = sourcePath;
        Across = across;
        Art = PieceArt.Replacement;
        Raise();
    }

    public void SetWorkingCopy(string path, string packName)
    {
        Replacement = null;
        ReplacementName = null;
        ReplacementPath = null;
        Across = false;
        WorkingCopyPath = path;
        WorkingCopyPack = packName;
        Opacity = DefaultOpacity;
        Hue = DefaultHue;
        (Width, Height) = Measure(path);
        Art = PieceArt.WorkingCopy;
        Raise();
    }

    /// <summary>Start fresh: the row's own art goes back to the file the 2.4 rules resolve, which is not the
    /// file a loaded record started it from (spec 5.4).</summary>
    internal void ResetOriginal(string path)
    {
        OriginalPath = path;
        LoadedFromRecord = false;
        (Width, Height) = Measure(path);
        Raise();
    }

    /// <summary>Back to the piece's own art. Values are not touched (spec 4).</summary>
    public void ResetArt()
    {
        Replacement = null;
        ReplacementName = null;
        ReplacementPath = null;
        Across = false;
        WorkingCopyPath = null;
        WorkingCopyPack = null;
        (Width, Height) = Measure(OriginalPath);
        Art = PieceArt.Original;
        Raise();
    }

    /// <summary>The status line after a save that faded pieces without the seam fix, because there was no level
    /// data to find the overlaps in (3.2 O1).</summary>
    public const string SeamFixNote = "Seam fix needs the game's level data";

    /// <summary>Writes this row's result to <paramref name="destPng"/>: the source recoloured by this row's
    /// values, from the fitted bitmap for a replacement. <paramref name="seamMask"/> is this row's factor per pixel
    /// from <see cref="SeamMasks"/>, or null to fade by the opacity alone. Runs on the calling thread; callers use
    /// Task.Run.</summary>
    public void WriteResult(string destPng, float[]? seamMask = null)
    {
        var opacity = Opacity / 100.0;
        if (Replacement is { } fitted)
        {
            PlatformRecolor.Apply(fitted, destPng, opacity, Hue, seamMask);
        }
        else
        {
            PlatformRecolor.Apply(SourcePath, destPng, opacity, Hue, seamMask);
        }
    }

    /// <summary>Spec 3.2 O1: the seam mask of every faded row of one map's set, keyed by relative path, over the
    /// playable layouts of <paramref name="model"/>. <paramref name="missing"/> is true when a row is faded and
    /// there is no level data to correct it with, which leaves every row fading on its own as before.</summary>
    public static IReadOnlyDictionary<string, float[]> SeamMasks(
        IReadOnlyList<PlatformPieceViewModel> rows, LevelDataModel? model, out bool missing)
    {
        var faded = rows.Where(r => r.Opacity < DefaultOpacity).ToList();
        missing = faded.Count > 0 && model is null;
        if (faded.Count == 0 || model is null)
        {
            return new Dictionary<string, float[]>();
        }

        var pieces = new List<SeamPiece>();
        foreach (var row in faded)
        {
            try
            {
                var source = row.Replacement ?? PlatformRecolor.Decode(row.SourcePath);
                pieces.Add(new SeamPiece(
                    row.RelativePath, source.PixelWidth, source.PixelHeight, PlatformRecolor.AlphaOf(source), row.Opacity / 100.0));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
            {
                // A row that will not decode cannot cover anything; writing it reports the error as before.
            }
        }

        // A dev or test level is not a layout anyone plays, so it must not veto a correction the real ones agree on.
        var hidden = model.Types.Where(t => !t.Included).Select(t => t.LevelName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return SeamMask.Compute(model.Levels.Where(l => !hidden.Contains(l.LevelName)), pieces);
    }

    /// <summary>What Save and the preview write for this row: a plain copy when nothing about it changed, its
    /// result otherwise (spec 3). A working copy at defaults is a copy of itself, which callers skip when source
    /// and destination are the same file.</summary>
    public void CopyOrWriteResult(string destPng, float[]? seamMask = null)
    {
        if (Art != PieceArt.Replacement && IsDefault)
        {
            if (!string.Equals(Path.GetFullPath(SourcePath), Path.GetFullPath(destPng), StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destPng)!);
                File.Copy(SourcePath, destPng, overwrite: true);
            }

            return;
        }

        WriteResult(destPng, seamMask);
    }

    /// <summary>Pixel size from the PNG header only (no full decode), 0 x 0 when the file cannot be read.</summary>
    private static (int Width, int Height) Measure(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            return (0, 0);
        }
    }

    /// <summary>The picture lines a source change moves that no property's own notification covers: Art is
    /// already what it is when a second Replace lands, and the size the text reads is a plain field.</summary>
    private void Raise()
    {
        OnPropertyChanged(nameof(SourcePath));
        OnPropertyChanged(nameof(ImageText));
    }
}
