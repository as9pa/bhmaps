using BhMaps.App.ViewModels;
using BhMaps.Core.Imaging;

namespace BhMaps.Core.Tests;

public class BackgroundEditorZoomTests
{
    [Fact]
    public void Wheel_zooms_ten_percent_a_notch()
    {
        var (zoom, _, _) = BackgroundEditorViewModel.Wheeled(1920, 1080, 1, 0.5, 0.5, 320, 180, 1);

        Assert.Equal(1.1, zoom, 6);
    }

    [Fact]
    public void Wheel_zoom_holds_to_one_and_four()
    {
        var (up, _, _) = BackgroundEditorViewModel.Wheeled(1920, 1080, 3.9, 0.5, 0.5, 320, 180, 5);
        var (down, panX, panY) = BackgroundEditorViewModel.Wheeled(1920, 1080, 1.05, 0.2, 0.8, 0, 0, -5);

        Assert.Equal(PanZoom.MaxZoom, up);
        Assert.Equal(PanZoom.MinZoom, down);

        // At 100% a 16:9 picture fills the 16:9 preview exactly, so there is nothing to pan and it sits centred.
        Assert.Equal(0.5, panX);
        Assert.Equal(0.5, panY);
    }

    [Fact]
    public void Wheel_keeps_the_point_under_the_cursor()
    {
        var (zoom, panX, panY) = BackgroundEditorViewModel.Wheeled(1920, 1080, 1, 0.5, 0.5, 0, 0, 3);

        // Zooming about the top left corner keeps that corner of the picture there.
        Assert.True(zoom > 1);
        Assert.Equal(0, panX, 6);
        Assert.Equal(0, panY, 6);
    }
}
