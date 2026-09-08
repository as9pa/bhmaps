using System.IO;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void TempDir_CreatesUniqueFolderAndDeletesItOnDispose()
    {
        string path;
        using (var tmp = new TempDir())
        {
            path = tmp.Path;
            Assert.True(Directory.Exists(path));
            Assert.StartsWith(System.IO.Path.GetTempPath(), path);
            File.WriteAllText(tmp.Sub("a", "b.txt"), "x");
            Assert.True(File.Exists(Path.Combine(path, "a", "b.txt")));
        }

        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void TempDir_TwoInstancesDoNotCollide()
    {
        using var a = new TempDir();
        using var b = new TempDir();
        Assert.NotEqual(a.Path, b.Path);
    }
}
