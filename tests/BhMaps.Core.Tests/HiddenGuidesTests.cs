using BhMaps.App.ViewModels;
using BhMaps.Core.Imaging;

namespace BhMaps.Core.Tests;

/// <summary>G4: the hidden guide layers in the settings, read by the platform editor and written back after a
/// legend click.</summary>
public class HiddenGuidesTests
{
    [Fact]
    public void KnownNamesAreReadAndUnknownOnesKeptAside()
    {
        var hidden = PlatformEditorViewModel.ReadHiddenGuides(["lava", "Sparkles", "Boxes", "99"], out var unknown);

        Assert.Equal([GuideLayer.Boxes, GuideLayer.Lava], hidden.Order());
        Assert.Equal(["Sparkles", "99"], unknown);
    }

    [Fact]
    public void UnknownNamesSurviveASave()
    {
        var hidden = PlatformEditorViewModel.ReadHiddenGuides(["Sparkles", "Mud"], out var unknown);
        hidden.Add(GuideLayer.Hard);

        Assert.Equal(["Hard", "Mud", "Sparkles"], PlatformEditorViewModel.HiddenGuideNames(hidden, unknown));
    }
}
