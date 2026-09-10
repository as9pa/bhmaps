namespace BhMaps.Core.LevelData;

/// <summary>WELL512-style PRNG the SWZ container is keyed with.
/// Ported from a Python reference implementation.</summary>
public sealed class SwzRandom
{
    private readonly uint[] _state = new uint[16];
    private int _index;

    public SwzRandom(uint seed)
    {
        _state[0] = seed;
        for (var i = 1; i < 16; i++)
        {
            var previous = _state[i - 1];
            _state[i] = (uint)i + (0x6C078965u * (previous ^ (previous >> 30)));
        }
    }

    public uint Next()
    {
        var s = _state;
        var i = _index;
        var a = s[i];
        var b = s[(i + 13) % 16];
        var c = a ^ (a << 16) ^ b ^ (b << 15);
        b = s[(i + 9) % 16];
        b ^= b >> 11;
        s[i] = b ^ c;
        a = s[i];
        var d = a ^ ((a << 5) & 0xDA442D24u);
        i = (i + 15) % 16;
        a = s[i];
        s[i] = a ^ (a << 2) ^ (b << 28) ^ c ^ (c << 18) ^ d;
        _index = i;
        return s[i];
    }
}
