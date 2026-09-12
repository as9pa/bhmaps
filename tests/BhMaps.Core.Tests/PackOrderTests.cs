using BhMaps.Core.Model;
using BhMaps.Core.Operations;

namespace BhMaps.Core.Tests;

public class PackOrderTests
{
    [Fact]
    public void DefaultFirst_ThenStampedNewestFirst_ThenUnstampedByName()
    {
        var packs = new[] { Pack("Zeta"), Pack("Default"), Pack("alpha"), Pack("Beta") };
        var stamps = new Dictionary<string, DateTimeOffset>
        {
            ["Beta"] = At(10),
            ["alpha"] = At(11),
        };

        var sorted = PackOrder.Sort(packs, stamps);

        Assert.Equal(new[] { "Default", "alpha", "Beta", "Zeta" }, sorted.Select(p => p.Name));
    }

    [Fact]
    public void NoStamps_IsDefaultThenName()
    {
        var packs = new[] { Pack("Zeta"), Pack("alpha"), Pack("Default"), Pack("Beta") };

        var sorted = PackOrder.Sort(packs, new Dictionary<string, DateTimeOffset>());

        Assert.Equal(new[] { "Default", "alpha", "Beta", "Zeta" }, sorted.Select(p => p.Name));
    }

    [Fact]
    public void StampForUnknownPack_IsIgnored()
    {
        var packs = new[] { Pack("Zeta"), Pack("alpha") };
        var stamps = new Dictionary<string, DateTimeOffset> { ["gone"] = At(11) };

        var sorted = PackOrder.Sort(packs, stamps);

        Assert.Equal(new[] { "alpha", "Zeta" }, sorted.Select(p => p.Name));
    }

    [Fact]
    public void StampLookup_IgnoresCase()
    {
        var packs = new[] { Pack("alpha"), Pack("Zeta") };
        var stamps = new Dictionary<string, DateTimeOffset> { ["ZETA"] = At(10) };

        var sorted = PackOrder.Sort(packs, stamps);

        Assert.Equal(new[] { "Zeta", "alpha" }, sorted.Select(p => p.Name));
    }

    private static Pack Pack(string name) => new(name, @"C:\x\" + name, []);

    private static DateTimeOffset At(int hour) => new(2026, 9, 12, hour, 0, 0, TimeSpan.Zero);
}
