using BhMaps.App.ViewModels;
using BhMaps.Core.Operations;

namespace BhMaps.Core.Tests;

/// <summary>3.9.2: the platform editor's Add as new popup, the shared one without the picture name.</summary>
public class AddAsNewPlatformsTests
{
    private static readonly string[] Packs = ["flower", "night"];

    private static AddAsNewViewModel Popup(
        string? startPack, Func<string, IReadOnlyList<string>>? mapsWithPlatforms = null, string[]? taken = null) =>
        AddAsNewViewModel.ForPlatforms(Packs, startPack, taken ?? Packs, mapsWithPlatforms ?? (_ => []));

    [Fact]
    public void HidesAndSkipsThePictureName()
    {
        var popup = Popup("flower");

        Assert.False(popup.ShowPictureName);
        Assert.Equal("", popup.PictureName);
        Assert.Equal("", popup.Error);
        Assert.True(popup.CanAdd);
    }

    [Fact]
    public void BackgroundPopupStillShowsThePictureName()
    {
        var popup = new AddAsNewViewModel(Packs, "flower", "Wharf", _ => [], Packs);

        Assert.True(popup.ShowPictureName);
        Assert.Equal("Wharf (2)", popup.PictureName);
    }

    [Fact]
    public void ListsThePacksAndNewPack()
    {
        var popup = Popup("flower");

        Assert.Equal(["flower", "night", AddAsNewViewModel.NewPackChoice], popup.PackChoices);
        Assert.Equal("flower", popup.TargetPack);
        Assert.Equal("flower", popup.EffectivePackName);
    }

    [Fact]
    public void StartsOnNewPackWhenTheCurrentPackIsNotListed()
    {
        var popup = Popup("brand new");

        Assert.True(popup.IsNewPack);
        Assert.Equal(PackNames.NextFree(Packs), popup.NewPackName);
        Assert.True(popup.CanAdd);
    }

    [Fact]
    public void RefusesAPackWithPlatformsForOneMap()
    {
        var popup = Popup("night", pack => pack == "night" ? ["Wharf"] : []);

        Assert.Equal("night already has platforms for Wharf.", popup.Error);
        Assert.False(popup.CanAdd);
        Assert.False(popup.AddCommand.CanExecute(null));

        popup.TargetPack = "flower";

        Assert.Equal("", popup.Error);
        Assert.True(popup.CanAdd);
    }

    [Fact]
    public void RefusesAPackWithPlatformsForSeveralMaps()
    {
        var popup = Popup("night", _ => ["Wharf", "Mammoth Fortress", "Shipwreck Falls"]);

        Assert.Equal("night already has platforms for Wharf and 2 more.", popup.Error);
        Assert.False(popup.CanAdd);
    }

    [Fact]
    public void NewPackIsNotAskedAboutPlatforms()
    {
        var popup = Popup("night", _ => ["Wharf"]);

        popup.TargetPack = AddAsNewViewModel.NewPackChoice;

        Assert.Equal("", popup.Error);
        Assert.True(popup.CanAdd);
    }

    [Fact]
    public void NewPackNameIsValidated()
    {
        var popup = Popup(null);
        Assert.True(popup.IsNewPack);

        popup.NewPackName = "a:b";
        PackNameValidator.IsValid("a:b", out var expected);
        Assert.Equal(expected, popup.Error);
        Assert.False(popup.CanAdd);

        popup.NewPackName = "Night";
        Assert.Equal("A pack called Night already exists.", popup.Error);

        popup.NewPackName = "on disk";
        Assert.True(popup.CanAdd);
    }

    [Fact]
    public void NewPackNameCountsFoldersOnDisk()
    {
        var popup = Popup(null, taken: ["flower", "night", "on disk"]);

        popup.NewPackName = "On Disk";

        Assert.Equal("A pack called On Disk already exists.", popup.Error);
        Assert.False(popup.CanAdd);
    }

    [Fact]
    public void AddClosesWithTrue()
    {
        var popup = Popup("flower");
        bool? closed = null;
        popup.CloseRequested += ok => closed = ok;

        popup.AddCommand.Execute(null);

        Assert.True(closed);
        Assert.Equal("flower", popup.EffectivePackName);
    }
}
