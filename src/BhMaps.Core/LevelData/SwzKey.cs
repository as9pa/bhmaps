using System.Buffers.Binary;

namespace BhMaps.Core.LevelData;

/// <summary>The key check that tells a candidate decryption key from a wrong one.</summary>
public static class SwzKey
{
    public const uint ChecksumSeed = 0x2DF4A1CD;

    /// <summary>Header is the first 8 bytes of a .swz. True when the key is right;
    /// rng is left positioned for the entry loop.</summary>
    public static bool Check(ReadOnlySpan<byte> header, uint key, out SwzRandom rng)
    {
        var expected = BinaryPrimitives.ReadUInt32BigEndian(header);
        var seed = BinaryPrimitives.ReadUInt32BigEndian(header[4..]) ^ key;
        rng = new SwzRandom(seed);
        var checksum = ChecksumSeed;
        var rounds = (key % 31) + 5;
        for (var i = 0u; i < rounds; i++)
        {
            checksum ^= rng.Next();
        }

        return checksum == expected;
    }
}
