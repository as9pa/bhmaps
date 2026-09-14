using BhMaps.Core.Tests.Helpers;
using BhMaps.Core.Update;

namespace BhMaps.Core.Tests;

public class UpdateInstallerTests
{
    [Fact]
    public void WriteApplyScript_QuotesEveryPathAndWaitsForThePid()
    {
        using var tmp = new TempDir();
        var updates = tmp.Sub("updates", "x")[..^2];
        var newExe = Path.Combine(updates, "bhmaps-v2.6.0-win-x64.exe");
        var running = @"C:\Program Files\Bh Maps\BhMaps.exe";

        var script = UpdateInstaller.WriteApplyScript(updates, newExe, running, 4321);
        var text = File.ReadAllText(script);

        Assert.Equal(Path.Combine(updates, "apply-update.cmd"), script);
        Assert.Contains("tasklist /FI \"PID eq 4321\"", text);
        Assert.Contains("ping -n 2 127.0.0.1", text);
        Assert.Contains($"\"{running}\"", text);
        Assert.Contains($"\"{newExe}\"", text);
        Assert.Contains($"\"{running}.old\"", text);
        Assert.DoesNotContain("C:\\Program Files\\Bh Maps\\BhMaps.exe ", text.Replace($"\"{running}\"", ""));
    }

    [Fact]
    public void WriteApplyScript_MovesRenamesStartsAndDeletesItself()
    {
        using var tmp = new TempDir();
        var script = UpdateInstaller.WriteApplyScript(
            tmp.Path, Path.Combine(tmp.Path, "new.exe"), Path.Combine(tmp.Path, "BhMaps.exe"), 10);
        var text = File.ReadAllText(script);

        // The order matters: the running exe is out of the way before the new one takes its name, and the app is
        // started before anything is deleted, so a failed start still leaves the .old file to go back to.
        var rename = text.IndexOf("move /y", StringComparison.Ordinal);
        var start = text.IndexOf("start \"\"", StringComparison.Ordinal);
        var cleanup = text.IndexOf(".old\"", start, StringComparison.Ordinal);
        Assert.True(rename > 0 && start > rename && cleanup > start);
        Assert.Contains("del /f /q \"%~f0\"", text);
        Assert.StartsWith("@echo off", text);
    }

    [Fact]
    public void WriteApplyScript_OverwritesAnOlderScript()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "apply-update.cmd");
        File.WriteAllText(path, "stale");

        UpdateInstaller.WriteApplyScript(tmp.Path, Path.Combine(tmp.Path, "n.exe"), Path.Combine(tmp.Path, "o.exe"), 1);

        Assert.DoesNotContain("stale", File.ReadAllText(path));
    }

    [Fact]
    public void CanSwap_TrueForAWritableFolderAndASelfContainedRuntime()
    {
        using var tmp = new TempDir();
        var exe = Path.Combine(tmp.Path, "BhMaps.exe");
        File.WriteAllText(exe, "");

        Assert.True(UpdateInstaller.CanSwap(exe, tmp.Path, tmp.Path));
    }

    [Fact]
    public void CanSwap_FalseWhenTheRuntimeIsASharedFrameworkInstall()
    {
        using var tmp = new TempDir();
        var exe = Path.Combine(tmp.Path, "BhMaps.exe");
        File.WriteAllText(exe, "");

        Assert.False(UpdateInstaller.CanSwap(exe, @"C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.0\", tmp.Path));
    }

    [Fact]
    public void CanSwap_TrueWhenTheSingleFileRuntimeExtractedUnderTemp()
    {
        // The shipped build is single-file self-contained: it unpacks its runtime under %TEMP%\.net, so the runtime
        // folder is nowhere near the exe and the app can still swap itself.
        using var tmp = new TempDir();
        var exe = Path.Combine(tmp.Path, "BhMaps.exe");
        File.WriteAllText(exe, "");

        Assert.True(UpdateInstaller.CanSwap(exe, @"C:\Users\x\AppData\Local\Temp\.net\BhMaps\abc123\", tmp.Path));
    }

    [Fact]
    public void CanSwap_TrueWhenTheRuntimeSitsBesideTheExe()
    {
        using var tmp = new TempDir();
        var app = tmp.Sub("app", "BhMaps.exe");
        File.WriteAllText(app, "");

        Assert.True(UpdateInstaller.CanSwap(app, Path.Combine(tmp.Path, "app"), Path.Combine(tmp.Path, "elsewhere")));
    }

    [Fact]
    public void CanSwap_FalseWhenTheFolderIsNotThere()
    {
        using var tmp = new TempDir();
        var missing = Path.Combine(tmp.Path, "gone", "BhMaps.exe");

        Assert.False(UpdateInstaller.CanSwap(missing, Path.Combine(tmp.Path, "gone"), Path.Combine(tmp.Path, "gone")));
    }

    [Fact]
    public void CanSwap_LeavesNoProbeFileBehind()
    {
        using var tmp = new TempDir();
        var exe = Path.Combine(tmp.Path, "BhMaps.exe");
        File.WriteAllText(exe, "");

        UpdateInstaller.CanSwap(exe, tmp.Path, tmp.Path);

        Assert.Equal(new[] { "BhMaps.exe" }, Directory.GetFiles(tmp.Path).Select(Path.GetFileName));
    }
}
