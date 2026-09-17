using System.Windows;
using BhMaps.Core.Imaging;
using BhMaps.Core.Operations;

namespace BhMaps.Core.Tests;

/// <summary>The one fit set, and the rectangle each of its four modes puts a picture in. Both fitters, the
/// piece's own and the one that lays a picture across the stage, place through
/// <see cref="BackgroundFitter.DestinationRect"/>, so the rectangle is what says a mode does what it is called.</summary>
public sealed class PictureFitsTests
{
    // A 200 x 100 box and a 400 x 100 picture: wider than the box and the same height, so every mode lands
    // somewhere different.
    private const int BoxWidth = 200;
    private const int BoxHeight = 100;
    private const int PictureWidth = 400;
    private const int PictureHeight = 100;

    [Fact]
    public void The_order_is_fill_fit_center_stretch()
    {
        Assert.Equal(
            [PictureFit.Fill, PictureFit.Fit, PictureFit.Center, PictureFit.Stretch],
            PictureFits.Order);
        Assert.Equal(["Fill", "Fit", "Center", "Stretch"], PictureFits.Order.Select(PictureFits.Label));
    }

    [Fact]
    public void Fill_covers_the_box_and_hangs_over_the_sides()
    {
        // The scale is the larger of the two, so the picture is 400 x 100 and centred by the default pan.
        Assert.Equal(new Rect(-100, 0, 400, 100), Rect(PictureFit.Fill));
    }

    [Fact]
    public void Fit_puts_the_whole_picture_inside_the_box()
    {
        // The smaller scale, 0.5, so 200 x 50 with the spare height split above and below.
        Assert.Equal(new Rect(0, 25, 200, 50), Rect(PictureFit.Fit));
    }

    [Fact]
    public void Center_keeps_the_pictures_own_pixels_and_crops_them()
    {
        // No scale at all: 400 x 100 centred, so 100 px falls outside each side and is cropped when it is drawn.
        Assert.Equal(new Rect(-100, 0, 400, 100), Rect(PictureFit.Center));

        // A picture smaller than the box is not grown either.
        Assert.Equal(
            new Rect(75, 25, 50, 50),
            BackgroundFitter.DestinationRect(50, 50, PictureFits.Options(PictureFit.Center), BoxWidth, BoxHeight));
    }

    [Fact]
    public void Center_on_a_preview_canvas_crops_the_way_the_save_will()
    {
        // A 4000 x 3000 photo, previewed on the editor's 640 x 360 canvas: the canvas is 640/2048 of the output,
        // so the picture is drawn at that share of its natural size and the preview shows the save's crop.
        var preview = BackgroundFitter.DestinationRect(
            640,
            360,
            PictureFits.Options(PictureFit.Center),
            640,
            360,
            4000,
            3000,
            BackgroundFitter.OutputWidth,
            BackgroundFitter.OutputHeight);
        Assert.Equal(1250, preview.Width, 0.01);
        Assert.Equal(3000 * (360.0 / BackgroundFitter.OutputHeight), preview.Height, 0.01);
        Assert.Equal((640 - preview.Width) / 2, preview.X, 0.01);
        Assert.Equal((360 - preview.Height) / 2, preview.Y, 0.01);

        // The same picture on the output itself keeps every one of its own pixels, centred.
        var saved = BackgroundFitter.DestinationRect(
            4000,
            3000,
            PictureFits.Options(PictureFit.Center),
            BackgroundFitter.OutputWidth,
            BackgroundFitter.OutputHeight);
        Assert.Equal(
            new Rect((BackgroundFitter.OutputWidth - 4000) / 2.0, (BackgroundFitter.OutputHeight - 3000) / 2.0, 4000, 3000),
            saved);
    }

    [Fact]
    public void Stretch_takes_the_box_exactly()
    {
        Assert.Equal(new Rect(0, 0, 200, 100), Rect(PictureFit.Stretch));
    }

    [Fact]
    public void Only_fill_reads_the_pan()
    {
        Assert.Equal(new Rect(0, 0, 400, 100), Rect(PictureFit.Fill, panX: 0));
        Assert.Equal(new Rect(-200, 0, 400, 100), Rect(PictureFit.Fill, panX: 1));
        Assert.Equal(Rect(PictureFit.Fit), Rect(PictureFit.Fit, panX: 0));
        Assert.Equal(Rect(PictureFit.Center), Rect(PictureFit.Center, panX: 1));
    }

    private static Rect Rect(PictureFit fit, double panX = 0.5) =>
        BackgroundFitter.DestinationRect(
            PictureWidth, PictureHeight, PictureFits.Options(fit, panX), BoxWidth, BoxHeight);
}
