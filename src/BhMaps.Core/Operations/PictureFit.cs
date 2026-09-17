using BhMaps.Core.Imaging;

namespace BhMaps.Core.Operations;

/// <summary>The one way a picture can fill the box it is put in, wherever BhMaps asks the question: Add Image,
/// the background editor and the platform editor all offer this set and nothing else (3.0 E).</summary>
public enum PictureFit
{
    Fill,
    Fit,
    Center,
    Stretch,
}

/// <summary>The words and the fit options behind <see cref="PictureFit"/>, in one place so every window says the
/// same thing and draws the same picture.</summary>
public static class PictureFits
{
    /// <summary>The order the controls show the choices in.</summary>
    public static readonly IReadOnlyList<PictureFit> Order =
        [PictureFit.Fill, PictureFit.Fit, PictureFit.Center, PictureFit.Stretch];

    /// <summary>The label on the control, which is also what the manual calls the mode.</summary>
    public static string Label(PictureFit fit) => fit switch
    {
        PictureFit.Fit => "Fit",
        PictureFit.Center => "Center",
        PictureFit.Stretch => "Stretch",
        _ => "Fill",
    };

    /// <summary>Fill covers the box and hangs over the edges, which is the only mode the pan moves; Fit sits the
    /// whole picture inside it; Center draws it at its natural pixel size and crops what falls outside; Stretch
    /// pulls it to the box's own shape (decision D9, 3.0 E).</summary>
    public static FitOptions Options(PictureFit fit, double panX = 0.5, double panY = 0.5, double darken = 0.0) =>
        new(ToFitMode(fit), panX, panY, darken);

    public static FitMode ToFitMode(PictureFit fit) => fit switch
    {
        PictureFit.Fit => FitMode.Contain,
        PictureFit.Center => FitMode.Center,
        PictureFit.Stretch => FitMode.Stretch,
        _ => FitMode.Cover,
    };
}
