using BhMaps.Core.Tests.Helpers;
using BhMaps.Core.Update;

namespace BhMaps.Core.Tests;

public class UpdateInstallerTests
{
    [Theory]
    [InlineData(@"C:\Apps\BhMaps.exe", true, true, UpdateBuild.SelfContained)]
    [InlineData(@"C:\Apps\BhMaps.exe", false, true, UpdateBuild.FrameworkDependent)]
    [InlineData(@"C:\src\bhmaps\bin\Debug\BhMaps.exe", false, false, UpdateBuild.Development)]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe", false, false, UpdateBuild.Development)]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe", false, true, UpdateBuild.Development)]
    [InlineData("", true, true, UpdateBuild.Development)]
    [InlineData(null, true, true, UpdateBuild.Development)]
    public void DetectBuild_ReadsTheRunningBuild(string? processPath, bool runtimeBundled, bool appBundled, UpdateBuild expected) =>
        Assert.Equal(expected, UpdateInstaller.DetectBuild(processPath, runtimeBundled, appBundled));

    [Fact]
    public void Swap_MovesTheRunningExeAsideAndTheNewOneIntoItsPlace()
    {
        using var tmp = new TempDir();
        var running = tmp.Sub("app", "BhMaps.exe");
        var fresh = tmp.Sub("updates", "bhmaps-v2.6.0-win-x64.exe");
        File.WriteAllText(running, "old build");
        File.WriteAllText(fresh, "new build");

        UpdateInstaller.Swap(fresh, running);

        Assert.Equal("new build", File.ReadAllText(running));
        Assert.Equal("old build", File.ReadAllText(running + ".old"));
        Assert.False(File.Exists(fresh));
    }

    [Fact]
    public void Swap_ReplacesAStaleOldFile()
    {
        using var tmp = new TempDir();
        var running = tmp.Sub("BhMaps.exe");
        var fresh = tmp.Sub("new.exe");
        File.WriteAllText(running, "running");
        File.WriteAllText(fresh, "new");
        File.WriteAllText(running + ".old", "stale");

        UpdateInstaller.Swap(fresh, running);

        Assert.Equal("running", File.ReadAllText(running + ".old"));
        Assert.Equal("new", File.ReadAllText(running));
    }

    [Fact]
    public void Swap_PutsTheRunningExeBackWhenTheNewOneCannotMove()
    {
        using var tmp = new TempDir();
        var running = tmp.Sub("BhMaps.exe");
        File.WriteAllText(running, "running");

        Assert.ThrowsAny<IOException>(() => UpdateInstaller.Swap(Path.Combine(tmp.Path, "missing.exe"), running));

        Assert.Equal("running", File.ReadAllText(running));
        Assert.False(File.Exists(running + ".old"));
    }

    [Fact]
    public void Rollback_UndoesACompletedSwap()
    {
        using var tmp = new TempDir();
        var running = tmp.Sub("BhMaps.exe");
        var fresh = tmp.Sub("updates", "new.exe");
        File.WriteAllText(running, "running");
        File.WriteAllText(fresh, "new");
        UpdateInstaller.Swap(fresh, running);

        UpdateInstaller.Rollback(fresh, running);

        Assert.Equal("running", File.ReadAllText(running));
        Assert.Equal("new", File.ReadAllText(fresh));
        Assert.False(File.Exists(running + ".old"));
    }

    [Fact]
    public void RestartArgs_AddsTheWaitAndDropsAnEarlierOne()
    {
        var args = UpdateInstaller.RestartArgs(["--appdata", @"C:\x", "--after-update", "12"], 345);

        Assert.Equal(["--appdata", @"C:\x", "--after-update", "345"], args);
    }

    [Fact]
    public void TryDeleteOld_RemovesTheLeftoverExe()
    {
        using var tmp = new TempDir();
        var running = tmp.Sub("BhMaps.exe");
        File.WriteAllText(running + ".old", "");

        Assert.True(UpdateInstaller.TryDeleteOld(running));
        Assert.False(File.Exists(running + ".old"));
    }

    [Fact]
    public void TryDeleteOld_TrueWhenThereIsNothingToDelete()
    {
        using var tmp = new TempDir();

        Assert.True(UpdateInstaller.TryDeleteOld(Path.Combine(tmp.Path, "BhMaps.exe")));
    }

    [Fact]
    public void TryDeleteOld_FalseWhileTheOldExeIsStillLocked()
    {
        using var tmp = new TempDir();
        var running = tmp.Sub("BhMaps.exe");
        File.WriteAllText(running + ".old", "");

        using (new FileStream(running + ".old", FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(UpdateInstaller.TryDeleteOld(running));
        }

        Assert.True(File.Exists(running + ".old"));
    }

    [Fact]
    public void CleanUpdatesFolder_RemovesTheScriptAndPartialDownloadsOnly()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.Path, "apply-update.cmd"), "");
        File.WriteAllText(Path.Combine(tmp.Path, "bhmaps-v2.6.0-win-x64.exe.partial"), "");
        File.WriteAllText(Path.Combine(tmp.Path, "keep.txt"), "");

        UpdateInstaller.CleanUpdatesFolder(tmp.Path);

        Assert.Equal(["keep.txt"], Directory.GetFiles(tmp.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void CleanUpdatesFolder_IgnoresAMissingFolder()
    {
        using var tmp = new TempDir();

        UpdateInstaller.CleanUpdatesFolder(Path.Combine(tmp.Path, "gone"));
    }
}
