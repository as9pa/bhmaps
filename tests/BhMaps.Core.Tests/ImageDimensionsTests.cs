using BhMaps.Core.Imaging;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class ImageDimensionsTests
{
    [Fact]
    public void Read_ReturnsThePixelSizeOfAnImage()
    {
        using var tmp = new TempDir();
        var path = SyntheticImage.SaveQuadrants(tmp.Sub("shot.png"), 40, 20);

        Assert.Equal((40, 20), ImageDimensions.Read(path)!.Value);
    }

    [Fact]
    public void Read_ReturnsNullForAFileThatIsNotAnImage()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("notes.txt");
        File.WriteAllText(path, "not an image");

        Assert.Null(ImageDimensions.Read(path));
    }

    [Fact]
    public void Read_ReturnsNullForAMissingFile()
    {
        using var tmp = new TempDir();

        Assert.Null(ImageDimensions.Read(Path.Combine(tmp.Path, "gone.png")));
    }

    [Fact]
    public void Read_LeavesNoHandleOnTheFile()
    {
        using var tmp = new TempDir();
        var path = SyntheticImage.SaveQuadrants(tmp.Sub("shot.png"), 8, 8);

        ImageDimensions.Read(path);

        File.Delete(path);
        Assert.False(File.Exists(path));
    }
}
