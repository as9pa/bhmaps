using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class BackgroundChoicesTests
{
    [Fact]
    public void For_ListsOnlyThePacksHoldingTheSlotFileAndPutsDefaultFirst()
    {
        using var tmp = new TempDir();
        WritePack(tmp, "zeta", "BG_Grove.jpg");
        WritePack(tmp, "alpha", "BG_Sewer.jpg");
        WritePack(tmp, "Default", "BG_Grove.jpg");
        var packs = PackScanner.ScanAll(tmp.Path);

        var choices = BackgroundChoices.For("BG_Grove.jpg", packs);

        Assert.Equal(new[] { "Default", "zeta" }, choices.Select(c => c.Pack.Name));
        Assert.Equal("BG_Grove.jpg", choices[1].File.Name);
        Assert.Equal(
            Path.Combine(tmp.Path, "packs", "zeta", "Backgrounds", "BG_Grove.jpg"),
            choices[1].File.FullPath);
    }

    [Fact]
    public void For_KeepsTheGivenOrderForEverythingButDefault()
    {
        using var tmp = new TempDir();
        WritePack(tmp, "aaa", "BG_Grove.jpg");
        WritePack(tmp, "bbb", "BG_Grove.jpg");
        WritePack(tmp, "Default", "BG_Grove.jpg");
        var packs = PackScanner.ScanAll(tmp.Path);

        // The Packs page shows the scan order, so a rows page must show that order too (addendum B).
        Assert.Equal(new[] { "aaa", "bbb", "Default" }, packs.Select(p => p.Name));
        Assert.Equal(
            new[] { "Default", "aaa", "bbb" },
            BackgroundChoices.For("BG_Grove.jpg", packs).Select(c => c.Pack.Name));
    }

    [Fact]
    public void For_ResolvesASlotBorrowedFromAnotherFolderToItsFileName()
    {
        using var tmp = new TempDir();
        WritePack(tmp, "snowy", "Snow1.jpg");
        var packs = PackScanner.ScanAll(tmp.Path);

        // A slot written "../Snow/Snow1.jpg" in the level data is still one file in the pack's Backgrounds folder.
        var choices = BackgroundChoices.For("../Snow/Snow1.jpg", packs);

        Assert.Equal(new[] { "snowy" }, choices.Select(c => c.Pack.Name));
    }

    [Fact]
    public void For_ReturnsNothingWhenNoPackHasThePicture()
    {
        using var tmp = new TempDir();
        WritePack(tmp, "alpha", "BG_Sewer.jpg");
        var packs = PackScanner.ScanAll(tmp.Path);

        Assert.Empty(BackgroundChoices.For("BG_Grove.jpg", packs));
        Assert.Empty(BackgroundChoices.For("BG_Grove.jpg", []));
    }

    private static void WritePack(TempDir tmp, string packName, params string[] fileNames)
    {
        foreach (var fileName in fileNames)
        {
            File.WriteAllText(tmp.Sub("packs", packName, "Backgrounds", fileName), packName + fileName);
        }
    }
}
