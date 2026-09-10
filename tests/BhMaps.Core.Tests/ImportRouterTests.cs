using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class ImportRouterTests
{
    private static GameTree StandardTree(TempDir tmp)
    {
        var game = Path.Combine(tmp.Path, "game");
        FakeGameTree.Standard(game);
        return GameTreeScanner.Scan(game);
    }

    private static string Src(TempDir tmp) => Path.Combine(tmp.Path, "src");

    [Fact]
    public void ParentFolderNameWinsEvenForCollidingAndUnknownNames()
    {
        using var tmp = new TempDir();
        var tree = StandardTree(tmp);
        new FakeGameTree(Path.Combine(Src(tmp), "mapArt"))
            .File("mustafar", "Lava3_Top.png", "x")
            .File("BP8", "MainPlat.png", "x")
            .File("Swamp", "BrandNew.png", "x");

        var plan = ImportRouter.Plan(Src(tmp), tree);

        Assert.Equal(3, plan.Rows.Count);
        Assert.All(plan.Rows, r => Assert.Equal(Route.Routed, r.Route));
        Assert.All(plan.Rows, r => Assert.True(r.Include));
        Assert.All(plan.Rows, r => Assert.False(r.Conflict));
        Assert.Equal(new[] { "BP8", "Mustafar", "Swamp" }, plan.Rows.Select(r => r.TargetFolder).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(3, plan.IncludedCount);
    }

    [Fact]
    public void UniqueFilenameRoutesLooseFile()
    {
        using var tmp = new TempDir();
        var tree = StandardTree(tmp);
        new FakeGameTree(Src(tmp)).File("Installed backgrounds", "BG_Sewer.jpg", "x").File("loose", "mud1.png", "x");

        var plan = ImportRouter.Plan(Src(tmp), tree);

        var bg = Assert.Single(plan.Rows, r => r.FileName == "BG_Sewer.jpg");
        Assert.Equal(Route.Routed, bg.Route);
        Assert.Equal("Backgrounds", bg.TargetFolder);
        Assert.Equal(Path.Combine("Backgrounds", "BG_Sewer.jpg"), bg.TargetRelativePath);
        var mud = Assert.Single(plan.Rows, r => r.FileName == "mud1.png");
        Assert.Equal("Swamp", mud.TargetFolder);
        Assert.Equal(Path.Combine("Swamp", "mud1.png"), mud.TargetRelativePath);
    }

    [Fact]
    public void CollidingFilenameWithoutFolderHintIsAmbiguousAndExcluded()
    {
        using var tmp = new TempDir();
        var tree = StandardTree(tmp);
        new FakeGameTree(Src(tmp)).File("stuff", "LeftWall.png", "x");

        var plan = ImportRouter.Plan(Src(tmp), tree);

        var row = Assert.Single(plan.Rows);
        Assert.Equal(Route.Ambiguous, row.Route);
        Assert.Equal(new[] { "BP8", "Zombie" }, row.Candidates);
        Assert.Null(row.TargetFolder);
        Assert.Null(row.TargetRelativePath);
        Assert.False(row.Include);
        Assert.Equal(0, plan.IncludedCount);
    }

    [Fact]
    public void UnknownFilenameWithoutFolderHintIsUnmatchedAndNonImagesAreSkipped()
    {
        using var tmp = new TempDir();
        var tree = StandardTree(tmp);
        new FakeGameTree(Src(tmp)).File("stuff", "Whatever.png", "x").File("stuff", "readme.txt", "x");

        var plan = ImportRouter.Plan(Src(tmp), tree);

        var row = Assert.Single(plan.Rows);
        Assert.Equal(Route.Unmatched, row.Route);
        Assert.Empty(row.Candidates);
        Assert.False(row.Include);
    }

    [Fact]
    public void UnreadableSubfolderIsSkippedInsteadOfAbortingThePlan()
    {
        using var tmp = new TempDir();
        var tree = StandardTree(tmp);
        new FakeGameTree(Src(tmp)).File("Swamp", "Mud1.png", "readable").File("locked", "LeftWall.png", "x");
        var denied = Path.Combine(Src(tmp), "locked");

        using (AccessDenial.DenyListing(denied))
        {
            var plan = ImportRouter.Plan(Src(tmp), tree);

            var row = Assert.Single(plan.Rows);
            Assert.Equal(Path.Combine(Src(tmp), "Swamp", "Mud1.png"), row.SourcePath);
            Assert.Equal("Swamp", row.TargetFolder);
            Assert.Equal(1, plan.IncludedCount);
        }
    }

    [Fact]
    public void HiddenImagesAreStillPlanned()
    {
        using var tmp = new TempDir();
        var tree = StandardTree(tmp);
        new FakeGameTree(Src(tmp)).File("Swamp", "Mud1.png", "x");
        var hidden = Path.Combine(Src(tmp), "Swamp", "Mud1.png");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var plan = ImportRouter.Plan(Src(tmp), tree);

        var row = Assert.Single(plan.Rows);
        Assert.Equal(hidden, row.SourcePath);
        Assert.Equal("Swamp", row.TargetFolder);
    }

    [Fact]
    public void MissingSourceFolderYieldsEmptyPlan()
    {
        using var tmp = new TempDir();
        var tree = StandardTree(tmp);

        var plan = ImportRouter.Plan(Path.Combine(tmp.Path, "nope"), tree);

        Assert.Empty(plan.Rows);
        Assert.Equal(0, plan.IncludedCount);
    }

    [Fact]
    public void AssigningFolderMakesRowRoutedAndIncluded()
    {
        using var tmp = new TempDir();
        var tree = StandardTree(tmp);
        new FakeGameTree(Src(tmp)).File("stuff", "LeftWall.png", "x").File("stuff", "Whatever.png", "x");
        var plan = ImportRouter.Plan(Src(tmp), tree);
        var ambiguous = plan.Rows.Single(r => r.FileName == "LeftWall.png");
        var unmatched = plan.Rows.Single(r => r.FileName == "Whatever.png");

        plan.AssignFolder(ambiguous, "Zombie");
        plan.AssignFolder(unmatched, "Swamp");

        Assert.Equal(Route.Routed, ambiguous.Route);
        Assert.Equal("Zombie", ambiguous.TargetFolder);
        Assert.True(ambiguous.Include);
        Assert.Equal("Swamp", unmatched.TargetFolder);
        Assert.True(unmatched.Include);
        Assert.Equal(2, plan.IncludedCount);

        plan.SetInclude(ambiguous, false);
        Assert.False(ambiguous.Include);
        Assert.Equal(1, plan.IncludedCount);
    }

    [Theory]
    [InlineData("Zombie")]
    [InlineData("BloodMoon")]
    [InlineData("My Folder")]
    public void AssignFolderAcceptsAnOrdinaryFolderName(string folderName)
    {
        using var tmp = new TempDir();
        var tree = StandardTree(tmp);
        new FakeGameTree(Src(tmp)).File("stuff", "Whatever.png", "x");
        var plan = ImportRouter.Plan(Src(tmp), tree);

        plan.AssignFolder(plan.Rows[0], folderName);

        Assert.Equal(Route.Routed, plan.Rows[0].Route);
        Assert.Equal(folderName, plan.Rows[0].TargetFolder);
        Assert.Equal(1, plan.IncludedCount);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("C:\\Windows")]
    [InlineData("sub\\folder")]
    [InlineData("")]
    public void AssignFolderRejectsNamesThatAreNotOneFolder(string folderName)
    {
        using var tmp = new TempDir();
        var tree = StandardTree(tmp);
        new FakeGameTree(Src(tmp)).File("stuff", "Whatever.png", "x");
        var plan = ImportRouter.Plan(Src(tmp), tree);

        Assert.Throws<ArgumentException>(() => plan.AssignFolder(plan.Rows[0], folderName));

        Assert.Equal(Route.Unmatched, plan.Rows[0].Route);
        Assert.Null(plan.Rows[0].TargetFolder);
        Assert.Equal(0, plan.IncludedCount);
    }

    [Fact]
    public void IncludingANonRoutedRowThrows()
    {
        using var tmp = new TempDir();
        var tree = StandardTree(tmp);
        new FakeGameTree(Src(tmp)).File("stuff", "Whatever.png", "x");
        var plan = ImportRouter.Plan(Src(tmp), tree);

        Assert.Throws<InvalidOperationException>(() => plan.SetInclude(plan.Rows[0], true));
    }

    [Fact]
    public void DuplicateTargetsAreFlaggedAndOnlyOneCanBeIncluded()
    {
        using var tmp = new TempDir();
        var tree = StandardTree(tmp);
        new FakeGameTree(Src(tmp)).File("a", "Mud1.png", "first").File("b", "Mud1.png", "second");
        var plan = ImportRouter.Plan(Src(tmp), tree);
        var first = plan.Rows.Single(r => r.SourcePath.Contains(Path.Combine("a", "Mud1.png")));
        var second = plan.Rows.Single(r => r.SourcePath.Contains(Path.Combine("b", "Mud1.png")));

        Assert.True(first.Conflict);
        Assert.True(second.Conflict);
        Assert.True(first.Include);
        Assert.False(second.Include);
        Assert.Equal(1, plan.IncludedCount);

        plan.SetInclude(second, true);

        Assert.False(first.Include);
        Assert.True(second.Include);
        Assert.Equal(1, plan.IncludedCount);
    }

    [Fact]
    public void ReassigningOneRowOfADuplicateClearsBothConflictFlags()
    {
        using var tmp = new TempDir();
        var tree = StandardTree(tmp);
        new FakeGameTree(Src(tmp)).File("Swamp", "Mud1.png", "routed").File("stuff", "Mud1.png", "x");
        var plan = ImportRouter.Plan(Src(tmp), tree);
        var loose = plan.Rows.Single(r => r.SourcePath.Contains("stuff"));
        var swamp = plan.Rows.Single(r => r.SourcePath.Contains(Path.Combine("Swamp", "Mud1.png")));

        // Both route to Swamp\Mud1.png (folder hint for one, unique filename for the other): a conflict from the start.
        // Walk order is by full path, so "stuff" sorts before "Swamp" and is the one included by default.
        Assert.All(plan.Rows, r => Assert.True(r.Conflict));
        Assert.True(loose.Include);
        Assert.False(swamp.Include);
        Assert.Equal(1, plan.IncludedCount);

        plan.AssignFolder(loose, "BloodMoon");

        Assert.All(plan.Rows, r => Assert.False(r.Conflict));
        Assert.Equal("BloodMoon", loose.TargetFolder);
        Assert.True(loose.Include);
        Assert.False(swamp.Include); // the excluded duplicate stays excluded until the user includes it
        Assert.Equal(1, plan.IncludedCount);

        plan.SetInclude(swamp, true);

        Assert.Equal(2, plan.IncludedCount);
    }

    [Fact]
    public void ExecuteCopiesIncludedRowsAndKeepsSources()
    {
        using var tmp = new TempDir();
        var tree = StandardTree(tmp);
        var src = new FakeGameTree(Path.Combine(Src(tmp), "mapArt"))
            .File("Mustafar", "Lava3_Top.png", "lava")
            .File("stuff", "Whatever.png", "unmatched");
        var plan = ImportRouter.Plan(Src(tmp), tree);
        var lib = Path.Combine(tmp.Path, "lib");
        var progress = new List<string>();

        var result = ImportRouter.Execute(plan, "mypack", lib, new SyncProgress(progress));

        Assert.Equal(1, result.Copied);
        Assert.Empty(result.Failures);
        Assert.Equal("lava", File.ReadAllText(Path.Combine(lib, "packs", "mypack", "Mustafar", "Lava3_Top.png")));
        Assert.False(File.Exists(Path.Combine(lib, "packs", "mypack", "stuff", "Whatever.png")));
        Assert.True(File.Exists(src.PathOf("Mustafar", "Lava3_Top.png")));
        Assert.Equal(new[] { Path.Combine("Mustafar", "Lava3_Top.png") }, progress);
    }

    [Fact]
    public void ExecuteIntoExistingPackOverwritesSameNamesAndKeepsOthers()
    {
        using var tmp = new TempDir();
        var tree = StandardTree(tmp);
        var lib = Path.Combine(tmp.Path, "lib");
        new FakeGameTree(Path.Combine(lib, "packs", "mypack")).File("Swamp", "Mud1.png", "old").File("Swamp", "Old.png", "keep");
        new FakeGameTree(Src(tmp)).File("Swamp", "Mud1.png", "new");
        var plan = ImportRouter.Plan(Src(tmp), tree);

        ImportRouter.Execute(plan, "mypack", lib);

        Assert.Equal("new", File.ReadAllText(Path.Combine(lib, "packs", "mypack", "Swamp", "Mud1.png")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(lib, "packs", "mypack", "Swamp", "Old.png")));
    }

    [Fact]
    public void ExecuteRejectsInvalidPackName()
    {
        using var tmp = new TempDir();
        var tree = StandardTree(tmp);
        new FakeGameTree(Src(tmp)).File("Swamp", "Mud1.png", "new");
        var plan = ImportRouter.Plan(Src(tmp), tree);

        Assert.Throws<ArgumentException>(() => ImportRouter.Execute(plan, "bad/name", tmp.Path));
    }

    [Fact]
    public void ExecuteHonorsCancellation()
    {
        using var tmp = new TempDir();
        var tree = StandardTree(tmp);
        new FakeGameTree(Src(tmp)).File("Swamp", "Mud1.png", "new");
        var plan = ImportRouter.Plan(Src(tmp), tree);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => ImportRouter.Execute(plan, "mypack", tmp.Path, null, cts.Token));
    }

    [Fact]
    public void SaveCurrentShapeRoutesEveryGameFileByFolder()
    {
        using var tmp = new TempDir();
        var tree = StandardTree(tmp);

        var plan = ImportRouter.Plan(tree.RootPath, tree);

        Assert.Equal(13, plan.Rows.Count);
        Assert.Equal(13, plan.IncludedCount);
        Assert.All(plan.Rows, r => Assert.Equal(Route.Routed, r.Route));
        Assert.All(plan.Rows, r => Assert.Equal(Path.GetFileName(Path.GetDirectoryName(r.SourcePath)!), r.TargetFolder));
        Assert.All(plan.Rows, r => Assert.False(r.Conflict));
    }
}
