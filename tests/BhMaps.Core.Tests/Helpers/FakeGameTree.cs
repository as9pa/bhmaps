namespace BhMaps.Core.Tests.Helpers;

/// <summary>Writes a small mapArt-shaped tree of text files. Contents are arbitrary bytes; scanning never decodes them.</summary>
public sealed class FakeGameTree
{
    public string Root { get; }

    public FakeGameTree(string root)
    {
        Root = root;
        Directory.CreateDirectory(root);
    }

    public FakeGameTree File(string folder, string name, string content)
    {
        var dir = Path.Combine(Root, folder);
        Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(Path.Combine(dir, name), content);
        return this;
    }

    public FakeGameTree Folder(string folder)
    {
        Directory.CreateDirectory(Path.Combine(Root, folder));
        return this;
    }

    public string PathOf(string folder, string name) => Path.Combine(Root, folder, name);

    /// <summary>Eight folders, 13 files, including the four colliding filenames from spec section 2.</summary>
    public static FakeGameTree Standard(string root) => new FakeGameTree(root)
        .File("BloodMoon", "BloodMoon_PlatformA01.png", "bm-a01")
        .File("BloodMoon", "BloodMoon_PlatformA02.png", "bm-a02")
        .File("BrawlFest", "Lava3_Bottom.png", "bf-lava-bottom")
        .File("BrawlFest", "Lava3_Top.png", "bf-lava-top")
        .File("Mustafar", "Lava3_Bottom.png", "mu-lava-bottom")
        .File("Mustafar", "Lava3_Top.png", "mu-lava-top")
        .File("BP8", "LeftWall.png", "bp8-leftwall")
        .File("BP8", "MainPlat.png", "bp8-mainplat")
        .File("Zombie", "LeftWall.png", "zo-leftwall")
        .File("Tekken", "MainPlat.png", "te-mainplat")
        .File("Backgrounds", "BG_Sewer.jpg", "bg-sewer")
        .File("Backgrounds", "BG_Space.jpg", "bg-space")
        .File("Swamp", "Mud1.png", "swamp-mud1");
}
