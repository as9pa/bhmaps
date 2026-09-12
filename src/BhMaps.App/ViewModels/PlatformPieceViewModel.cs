using System.Windows.Media;
using System.Windows.Media.Imaging;
using BhMaps.Core.Imaging;
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

    public string OriginalPath { get; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    [ObservableProperty]
    public partial bool IsTicked { get; set; }

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

    public void SetReplacement(BitmapSource fitted, string pickedFileName)
    {
        Replacement = fitted;
        ReplacementName = pickedFileName;
        Art = PieceArt.Replacement;
        Raise();
    }

    public void SetWorkingCopy(string path, string packName)
    {
        Replacement = null;
        ReplacementName = null;
        WorkingCopyPath = path;
        WorkingCopyPack = packName;
        Opacity = DefaultOpacity;
        Hue = DefaultHue;
        (Width, Height) = Measure(path);
        Art = PieceArt.WorkingCopy;
        Raise();
    }

    /// <summary>Back to the piece's own art. Values are not touched (spec 4).</summary>
    public void ResetArt()
    {
        Replacement = null;
        ReplacementName = null;
        WorkingCopyPath = null;
        WorkingCopyPack = null;
        (Width, Height) = Measure(OriginalPath);
        Art = PieceArt.Original;
        Raise();
    }

    /// <summary>Writes this row's result to <paramref name="destPng"/>: the source recoloured by this row's
    /// values, from the fitted bitmap for a replacement. Runs on the calling thread; callers use Task.Run.</summary>
    public void WriteResult(string destPng)
    {
        var opacity = Opacity / 100.0;
        if (Replacement is { } fitted)
        {
            PlatformRecolor.Apply(fitted, destPng, opacity, Hue);
        }
        else
        {
            PlatformRecolor.Apply(SourcePath, destPng, opacity, Hue);
        }
    }

    /// <summary>What Save and the preview write for this row: a plain copy when nothing about it changed, its
    /// result otherwise (spec 3). A working copy at defaults is a copy of itself, which callers skip when source
    /// and destination are the same file.</summary>
    public void CopyOrWriteResult(string destPng)
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

        WriteResult(destPng);
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
