using BhMaps.Core.Hashing;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class RefreshPlannerTests
{
    private static readonly DateTimeOffset Noted = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A game folder, a library beside it and the record of what the app wrote into the one from the
    /// other, all under one temp folder.</summary>
    private sealed record World(string Game, string Library, string Record);

    private static World Arrange(TempDir tmp) =>
        new(Path.Combine(tmp.Path, "game"),
            Path.Combine(tmp.Path, "library"),
            Path.Combine(tmp.Path, "appdata", "applied.json"));

    private static string Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string PackFile(World world, string packName, string folder, string name, string content) =>
        Write(Path.Combine(world.Library, "packs", packName, folder, name), content);

    /// <summary>Records the write as the app would have, with no game file there before it.</summary>
    private static void Note(World world, params AppliedSource[] sources) =>
        AppliedRecord.Note(world.Record, world.Game, world.Library, sources, Noted, _ => null);

    private static string? HashOf(string fullPath) => File.Exists(fullPath) ? FileHasher.Hash(fullPath) : null;

    private static RefreshPlan Plan(World world, IReadOnlyDictionary<string, MapStatus>? statuses = null)
    {
        var packs = PackScanner.ScanAll(world.Library);
        return RefreshPlanner.Plan(
            world.Game,
            world.Library,
            GameTreeScanner.Scan(world.Game),
            packs,
            DefaultPack.Find(packs),
            statuses ?? new Dictionary<string, MapStatus>(StringComparer.OrdinalIgnoreCase),
            AppliedRecord.Load(world.Record),
            HashOf);
    }

    /// <summary>One map folder whose files are all missing from the game, matched to the packs given.</summary>
    private static IReadOnlyDictionary<string, MapStatus> Missing(
        string folderName, IReadOnlyList<string> packNames, params string[] relativePaths) =>
        new Dictionary<string, MapStatus>(StringComparer.OrdinalIgnoreCase)
        {
            [folderName] = new MapStatus(
                folderName,
                MapState.Missing,
                packNames,
                [.. relativePaths.Select(p => new MapFileStatus(p, MapFileState.Missing, []))]),
        };

    [Fact]
    public void Plan_SourceUnchanged_WritesNothing()
    {
        using var tmp = new TempDir();
        var world = Arrange(tmp);
        var source = PackFile(world, "dark", "Grove", "a.png", "art");
        Write(Path.Combine(world.Game, "Grove", "a.png"), "art");
        Note(world, new AppliedSource("Grove\\a.png", source, "dark"));

        var plan = Plan(world);

        Assert.Empty(plan.Copies);
        Assert.Empty(plan.Skipped);
        Assert.Empty(plan.Folders);
    }

    [Fact]
    public void Plan_PackFileEditedOnDisk_CopiesItRaw()
    {
        using var tmp = new TempDir();
        var world = Arrange(tmp);
        var source = PackFile(world, "dark", "Grove", "a.png", "art");
        Write(Path.Combine(world.Game, "Grove", "a.png"), "art");
        Note(world, new AppliedSource("Grove\\a.png", source, "dark"));
        Write(source, "art edited in another app");

        var plan = Plan(world);

        var copy = Assert.Single(plan.Copies);
        Assert.Equal("Grove\\a.png", copy.GameRelativePath);
        Assert.Equal(source, copy.SourceFullPath);
        Assert.Equal("dark", copy.PackName);
        Assert.Equal(RefreshReason.SourceChanged, copy.Reason);
        Assert.False(copy.Fit);
        Assert.Equal(["Grove"], plan.Folders);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Plan_PictureEditedOnDisk_FitsItIntoTheSlotAgain()
    {
        using var tmp = new TempDir();
        var world = Arrange(tmp);
        var source = Write(Path.Combine(world.Library, "My Backgrounds", "sunset.jpg"), "sunset");
        Write(Path.Combine(world.Game, "Backgrounds", "BG_Grove.jpg"), "sunset fitted to the slot");
        Note(world, new AppliedSource("Backgrounds\\BG_Grove.jpg", source, null));
        Write(source, "sunset edited");

        var plan = Plan(world);

        var copy = Assert.Single(plan.Copies);
        Assert.Equal("Backgrounds\\BG_Grove.jpg", copy.GameRelativePath);
        Assert.Null(copy.PackName);
        Assert.Equal(RefreshReason.SourceChanged, copy.Reason);
        Assert.True(copy.Fit);
    }

    [Fact]
    public void Plan_PictureFolderEndingInTheSlotFolder_StillFits()
    {
        using var tmp = new TempDir();
        var world = Arrange(tmp);
        var source = Write(Path.Combine(world.Library, "My Backgrounds", "BG_Grove.jpg"), "sunset");
        Write(Path.Combine(world.Game, "Backgrounds", "BG_Grove.jpg"), "sunset fitted to the slot");
        Note(world, new AppliedSource("Backgrounds\\BG_Grove.jpg", source, null));
        Write(source, "sunset edited");

        var plan = Plan(world);

        Assert.True(Assert.Single(plan.Copies).Fit);
    }

    [Fact]
    public void Plan_GameFileChangedByHand_LeavesItAlone()
    {
        using var tmp = new TempDir();
        var world = Arrange(tmp);
        var source = PackFile(world, "dark", "Grove", "a.png", "art");
        var gameFile = Write(Path.Combine(world.Game, "Grove", "a.png"), "art");
        Note(world, new AppliedSource("Grove\\a.png", source, "dark"));
        Write(source, "art edited in another app");
        Write(gameFile, "dropped in by hand");

        var plan = Plan(world);

        Assert.Empty(plan.Copies);
        var skip = Assert.Single(plan.Skipped);
        Assert.Equal("Grove\\a.png", skip.GameRelativePath);
        Assert.Equal("changed since", skip.Why);
    }

    [Fact]
    public void Plan_SourceGone_LeavesItAlone()
    {
        using var tmp = new TempDir();
        var world = Arrange(tmp);
        var source = PackFile(world, "dark", "Grove", "a.png", "art");
        Write(Path.Combine(world.Game, "Grove", "a.png"), "art");
        Note(world, new AppliedSource("Grove\\a.png", source, "dark"));
        File.Delete(source);

        var plan = Plan(world);

        Assert.Empty(plan.Copies);
        var skip = Assert.Single(plan.Skipped);
        Assert.Equal("source missing", skip.Why);
    }

    [Fact]
    public void Plan_GameFileGone_WritesNothing()
    {
        using var tmp = new TempDir();
        var world = Arrange(tmp);
        var source = PackFile(world, "dark", "Grove", "a.png", "art");
        var gameFile = Write(Path.Combine(world.Game, "Grove", "a.png"), "art");
        Note(world, new AppliedSource("Grove\\a.png", source, "dark"));
        Write(source, "art edited in another app");
        File.Delete(gameFile);

        var plan = Plan(world);

        Assert.Empty(plan.Copies);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Plan_MissingFile_RestoresFromTheMatchedPack()
    {
        using var tmp = new TempDir();
        var world = Arrange(tmp);
        var dark = PackFile(world, "dark", "Grove", "b.png", "dark-b");
        PackFile(world, "Default", "Grove", "b.png", "default-b");

        var plan = Plan(world, Missing("Grove", ["dark"], "Grove\\b.png"));

        var copy = Assert.Single(plan.Copies);
        Assert.Equal(dark, copy.SourceFullPath);
        Assert.Equal("dark", copy.PackName);
        Assert.Equal(RefreshReason.Missing, copy.Reason);
        Assert.False(copy.Fit);
        Assert.Equal(["Grove"], plan.Folders);
    }

    [Fact]
    public void Plan_MatchedPackLacksTheMissingFile_RestoresFromDefault()
    {
        using var tmp = new TempDir();
        var world = Arrange(tmp);
        PackFile(world, "dark", "Grove", "a.png", "dark-a");
        var fallback = PackFile(world, "Default", "Grove", "b.png", "default-b");

        var plan = Plan(world, Missing("Grove", ["dark"], "Grove\\b.png"));

        var copy = Assert.Single(plan.Copies);
        Assert.Equal(fallback, copy.SourceFullPath);
        Assert.Equal("Default", copy.PackName);
    }

    [Fact]
    public void Plan_TwoPacksInTheFolder_RestoresFromDefault()
    {
        using var tmp = new TempDir();
        var world = Arrange(tmp);
        PackFile(world, "dark", "Grove", "b.png", "dark-b");
        var fallback = PackFile(world, "Default", "Grove", "b.png", "default-b");

        var plan = Plan(world, Missing("Grove", ["dark", "flowers"], "Grove\\b.png"));

        var copy = Assert.Single(plan.Copies);
        Assert.Equal(fallback, copy.SourceFullPath);
        Assert.Equal("Default", copy.PackName);
    }

    [Fact]
    public void Plan_NoPackHasTheMissingFile_RestoresNothing()
    {
        using var tmp = new TempDir();
        var world = Arrange(tmp);
        PackFile(world, "Default", "Grove", "a.png", "default-a");

        var plan = Plan(world, Missing("Grove", [], "Grove\\c.png"));

        Assert.Empty(plan.Copies);
    }

    [Fact]
    public void Plan_NothingRecordedAndNothingMissing_IsEmpty()
    {
        using var tmp = new TempDir();
        var world = Arrange(tmp);
        PackFile(world, "Default", "Grove", "a.png", "default-a");
        Write(Path.Combine(world.Game, "Grove", "a.png"), "default-a");

        var plan = Plan(world);

        Assert.Empty(plan.Copies);
        Assert.Empty(plan.Skipped);
        Assert.Empty(plan.Folders);
    }
}
