using BhMaps.Core.LevelData;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class SwzRandomTests
{
    [Fact]
    public void Next_MatchesTheReferencePortForSeed12345()
    {
        var rng = new SwzRandom(12345);
        uint[] expected =
        [
            0xB721ADF3, 0xC4F1D2C0, 0xB0289D0F, 0xB88B52F4,
            0x3C4EAC5D, 0x16EFB5D0, 0x62FE931E, 0x08FFE707,
        ];

        var actual = new uint[expected.Length];
        for (var i = 0; i < actual.Length; i++)
        {
            actual[i] = rng.Next();
        }

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Next_MatchesTheReferencePortForSeedZero()
    {
        var rng = new SwzRandom(0);
        Assert.Equal(0x49A05247u, rng.Next());
        Assert.Equal(0x66088A6Cu, rng.Next());
        Assert.Equal(0x95D63087u, rng.Next());
        Assert.Equal(0xD4C35361u, rng.Next());
    }

    [Fact]
    public void Check_AcceptsTheRightKeyAndRejectsAnother()
    {
        var container = SwzWriter.Write(826351010, [System.Text.Encoding.UTF8.GetBytes("<X/>")]);

        Assert.True(SwzKey.Check(container.AsSpan(0, 8), 826351010, out _));
        Assert.False(SwzKey.Check(container.AsSpan(0, 8), 12345, out _));
    }

    [Fact]
    public void Check_HeaderForKey42MatchesTheReferenceChecksum()
    {
        // seedWord 0x12345678 xor key 42, 16 rounds ((42 % 31) + 5), computed with swz.py.
        var container = SwzWriter.Write(42, [System.Text.Encoding.UTF8.GetBytes("<X/>")], seedWord: 0x12345678);

        Assert.Equal(0x93A39B23u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(container.AsSpan(0, 4)));
        Assert.Equal(0x12345678u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(container.AsSpan(4, 4)));
    }
}
