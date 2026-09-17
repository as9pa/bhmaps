using BhMaps.Core.Operations;

namespace BhMaps.Core.Tests;

public class PictureNamesTests
{
    [Fact]
    public void Clean_OpensUnderscoresAndHyphensAndKeepsTheExtensionAsItCame()
    {
        Assert.Equal("castle night v2.PNG", PictureNames.Clean("castle_night-v2.PNG"));
    }

    [Fact]
    public void Clean_CollapsesRunsOfWhitespaceAndTrims()
    {
        Assert.Equal("a b", PictureNames.Clean("  a   b "));
    }

    [Fact]
    public void Clean_KeepsTheCaseTheFileHad()
    {
        Assert.Equal("Sunset Over Grove.jpg", PictureNames.Clean("Sunset_Over_Grove.jpg"));
    }

    [Fact]
    public void Clean_CallsAPictureWhoseBaseCleansAwayToNothingPicture()
    {
        Assert.Equal("Picture", PictureNames.Clean("___"));
        Assert.Equal("Picture.jpg", PictureNames.Clean(".jpg"));
    }

    [Fact]
    public void FileName_DropsWhatWindowsWillNotTakeInAFileNameAndKeepsTypedPunctuation()
    {
        Assert.Equal("a-b_c.jpg", PictureNames.FileName("a-b_c", ".jpg"));
        Assert.Equal("ab.jpg", PictureNames.FileName("a<b>", ".jpg"));
        Assert.Equal("Picture.jpg", PictureNames.FileName("///", ".jpg"));
    }

    [Fact]
    public void Unique_LeavesANameNothingHoldsAlone()
    {
        Assert.Equal("sunset.png", PictureNames.Unique("sunset.png", ["grove.jpg"]));
    }

    [Fact]
    public void Unique_CountsUpPastEveryNameTaken()
    {
        Assert.Equal("sunset (2).png", PictureNames.Unique("sunset.png", ["sunset.png"]));
        Assert.Equal("sunset (3).png", PictureNames.Unique("sunset.png", ["sunset.png", "sunset (2).png"]));
    }

    [Fact]
    public void Unique_ComparesNamesTheWayWindowsDoes()
    {
        Assert.Equal("sunset (2).png", PictureNames.Unique("sunset.png", ["SUNSET.PNG"]));
    }

    [Fact]
    public void ForImport_CleansEachNameAndKeepsTwoOfTheSameName()
    {
        var names = PictureNames.ForImport(
            ["holiday_one.jpg", "sunset.jpg", "sunset.jpg"], ["holiday one.jpg"]);

        Assert.Equal(["holiday one (2).jpg", "sunset.jpg", "sunset (2).jpg"], names);
    }
}
