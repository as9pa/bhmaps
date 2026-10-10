using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BhMaps.Core.Imaging;
using BhMaps.Core.Operations;
using BhMaps.Core.Packs;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class PanZoomTests
{
    [Fact]
    public void Zoom_one_is_the_plain_cover_fit()
    {
        // A 400x200 picture in a 100x100 box covers at 0.5: 200x100, hanging 100 over the box along X.
        var rect = PanZoom.Cover(400, 200, 100, 100, 1, 0.5, 0.5);

        Assert.Equal(new Rect(-50, 0, 200, 100), rect);
    }

    [Fact]
    public void Zoom_multiplies_the_cover_fit_and_the_pan_runs_along_the_overflow()
    {
        var centred = PanZoom.Cover(400, 200, 100, 100, 2, 0.5, 0.5);
        var corner = PanZoom.Cover(400, 200, 100, 100, 2, 0, 0);
        var far = PanZoom.Cover(400, 200, 100, 100, 2, 1, 1);

        Assert.Equal(new Rect(-150, -50, 400, 200), centred);
        Assert.Equal(new Rect(0, 0, 400, 200), corner);
        Assert.Equal(new Rect(-300, -100, 400, 200), far);
    }

    [Theory]
    [InlineData(0.5, 1.0)]
    [InlineData(9.0, 4.0)]
    [InlineData(double.NaN, 1.0)]
    public void Zoom_holds_to_one_to_four(double zoom, double expected) =>
        Assert.Equal(expected, PanZoom.ClampZoom(zoom));

    [Fact]
    public void Overflow_is_zero_on_an_axis_the_picture_fits_at_one_and_grows_with_zoom()
    {
        Assert.Equal((100.0, 0.0), PanZoom.Overflow(400, 200, 100, 100, 1));
        Assert.Equal((300.0, 100.0), PanZoom.Overflow(400, 200, 100, 100, 2));
    }

    [Fact]
    public void Drag_moves_the_pan_by_the_overflow_and_leaves_an_axis_with_none()
    {
        // 100 over along X: dragging 25 units right moves the picture right, a quarter of the way.
        var (panX, panY) = PanZoom.Drag(400, 200, 100, 100, 1, 0.5, 0.5, 25, 40);

        Assert.Equal(0.25, panX, 6);
        Assert.Equal(0.5, panY);
    }

    [Fact]
    public void Drag_is_clamped_to_the_edges()
    {
        var (panX, _) = PanZoom.Drag(400, 200, 100, 100, 1, 0.5, 0.5, -1000, 0);

        Assert.Equal(1, panX);
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(20, 70)]
    [InlineData(85, 10)]
    public void Wheel_zoom_keeps_the_picture_point_under_the_cursor(double px, double py)
    {
        var before = PanZoom.Cover(400, 300, 100, 100, 1.5, 0.4, 0.6);
        var u = (px - before.X) / before.Width;
        var v = (py - before.Y) / before.Height;

        var (zoom, panX, panY) = PanZoom.ZoomAbout(400, 300, 100, 100, 1.5, 0.4, 0.6, 1.5 * PanZoom.WheelStep, px, py);
        var after = PanZoom.Cover(400, 300, 100, 100, zoom, panX, panY);

        Assert.Equal(1.65, zoom, 6);
        Assert.Equal(px, after.X + (u * after.Width), 6);
        Assert.Equal(py, after.Y + (v * after.Height), 6);
    }

    [Fact]
    public void Wheel_zoom_is_clamped_and_back_at_one_an_axis_with_no_overflow_centres()
    {
        var (zoom, _, panY) = PanZoom.ZoomAbout(400, 200, 100, 100, 1.2, 0.5, 0.1, 0.5, 10, 10);

        Assert.Equal(1, zoom);
        Assert.Equal(0.5, panY);
    }

    [Fact]
    public void Fill_destination_takes_the_zoom_and_other_fits_ignore_it()
    {
        var fill = BackgroundFitter.DestinationRect(400, 200, PictureFits.Options(PictureFit.Fill, zoom: 2), 100, 100);
        var fit = BackgroundFitter.DestinationRect(400, 200, PictureFits.Options(PictureFit.Fit, zoom: 2), 100, 100);

        Assert.Equal(PanZoom.Cover(400, 200, 100, 100, 2, 0.5, 0.5), fill);
        Assert.Equal(new Rect(0, 25, 100, 50), fit);
    }

    [Fact]
    public void Record_without_the_new_fields_reads_them_as_missing()
    {
        using var tmp = new TempDir();
        File.WriteAllText(
            PlatformEditRecord.PathFor(tmp.Path),
            """
            {"version":2,"maps":{"BLOODMOON":{"savedAt":"2026-09-12T18:30:00-04:00","pieces":{
              "BLOODMOON\\platform_bm1.png":{"opacity":80,"hue":10,"art":"across","picture":"a.png","fit":"fill","panX":0.25,"panY":0.75,"hash":"aa"}}}}}
            """);

        var entry = PlatformEditRecord.Load(tmp.Path).Entry("BLOODMOON", "BLOODMOON\\platform_bm1.png");

        Assert.NotNull(entry);
        Assert.Equal(0.25, entry.PanX);
        Assert.Null(entry.Zoom);
        Assert.Null(entry.Saturation);
        Assert.Null(entry.Contrast);
        Assert.Null(entry.Darken);
        Assert.Null(entry.Blur);
        Assert.Equal(1.0, PanZoom.ClampZoom(entry.Zoom ?? PanZoom.MinZoom));
    }

    [Fact]
    public void Record_round_trips_zoom_and_tone()
    {
        using var tmp = new TempDir();
        var record = new PlatformEditRecord();
        record.SetMap("BLOODMOON", DateTimeOffset.Now, new Dictionary<string, PlatformPieceEntry>
        {
            ["BLOODMOON\\platform_bm1.png"] = new()
            {
                Art = PlatformArt.EachPiece,
                Picture = "a.png",
                PanX = 0.1,
                PanY = 0.9,
                Zoom = 2.5,
                Saturation = -40,
                Contrast = 30,
                Darken = 20,
                Blur = 10,
                Hash = "aa",
            },
        });

        record.Save(tmp.Path);
        var entry = PlatformEditRecord.Load(tmp.Path).Entry("BLOODMOON", "BLOODMOON\\platform_bm1.png");

        Assert.NotNull(entry);
        Assert.Equal(2.5, entry.Zoom);
        Assert.Equal(0.1, entry.PanX);
        Assert.Equal(0.9, entry.PanY);
        Assert.Equal(-40, entry.Saturation);
        Assert.Equal(30, entry.Contrast);
        Assert.Equal(20, entry.Darken);
        Assert.Equal(10, entry.Blur);
    }

    [Fact]
    public void Tone_darkens_and_blurs_inside_the_shape_without_touching_alpha()
    {
        // Left half opaque red, right half transparent.
        const int size = 16;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size / 2; x++)
            {
                var i = ((y * size) + x) * 4;
                pixels[i + 2] = 200;
                pixels[i + 3] = 255;
            }
        }

        var piece = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        piece.Freeze();

        var toned = PlatformRecolor.Toned(piece, new PieceTone(Darken: 0.5, Blur: 0.2));
        var output = new byte[pixels.Length];
        toned.CopyPixels(output, size * 4, 0);

        for (var i = 3; i < output.Length; i += 4)
        {
            Assert.Equal(pixels[i], output[i]);
        }

        var inside = ((8 * size) + 2) * 4;
        Assert.InRange(output[inside + 2], 90, 110);
    }
}
