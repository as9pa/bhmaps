using BhMaps.Core.Layout;
using BhMaps.Core.Settings;

namespace BhMaps.Core.Tests;

public class JustifiedLayoutTests
{
    [Fact]
    public void Compute_FillsTheRowWithAsManyTilesAsFitTheTarget()
    {
        var (columns, width) = JustifiedLayout.Compute(1200, 200, 12);

        Assert.Equal(5, columns);
        Assert.Equal(230.4, width, 3);

        // The point of the whole thing: the row ends where the grid ends, with no hole down the right edge.
        Assert.Equal(1200, (columns * width) + ((columns - 1) * 12), 3);
    }

    [Fact]
    public void Compute_GivesOneFullWidthColumnWhenOnlyOneTileFits()
    {
        var (columns, width) = JustifiedLayout.Compute(300, 300, 12);

        Assert.Equal(1, columns);
        Assert.Equal(300, width, 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-40)]
    public void Compute_StillAnswersBeforeThePanelHasAWidth(double available)
    {
        var (columns, width) = JustifiedLayout.Compute(available, 200, 12);

        Assert.Equal(1, columns);
        Assert.Equal(1, width, 3);
    }

    [Fact]
    public void CardWidthAndRowTileWidth_ShrinkWithTheSize()
    {
        Assert.Equal(300, TileSizes.CardWidth(TileSize.Large));
        Assert.Equal(200, TileSizes.CardWidth(TileSize.Medium));
        Assert.Equal(120, TileSizes.CardWidth(TileSize.Small));
        Assert.Equal(224, TileSizes.RowTileWidth(TileSize.Large));
        Assert.Equal(150, TileSizes.RowTileWidth(TileSize.Medium));
        Assert.Equal(96, TileSizes.RowTileWidth(TileSize.Small));
    }
}
