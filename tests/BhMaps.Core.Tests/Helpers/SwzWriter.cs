using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using BhMaps.Core.LevelData;

namespace BhMaps.Core.Tests.Helpers;

/// <summary>Builds a .swz container the way the game does, so SwzReader can be
/// round-tripped without the real files.</summary>
public static class SwzWriter
{
    public static byte[] Write(uint key, IReadOnlyList<byte[]> entries, uint seedWord = 0x12345678)
    {
        var output = new MemoryStream();
        var header = new byte[8];
        var rng = new SwzRandom(seedWord ^ key);
        var checksum = SwzKey.ChecksumSeed;
        var rounds = (key % 31) + 5;
        for (var i = 0u; i < rounds; i++)
        {
            checksum ^= rng.Next();
        }

        BinaryPrimitives.WriteUInt32BigEndian(header, checksum);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), seedWord);
        output.Write(header);

        foreach (var raw in entries)
        {
            var compressed = Deflate(raw);
            WriteU32(output, (uint)compressed.Length ^ rng.Next());
            WriteU32(output, (uint)raw.Length ^ rng.Next());

            var body = new byte[compressed.Length];
            var accumulator = rng.Next();
            for (var i = 0; i < compressed.Length; i++)
            {
                var random = rng.Next();
                var bitIndex = i & 0xF;
                var mask = (byte)((((0xFFu << bitIndex) & random) >> bitIndex) & 0xFF);
                body[i] = (byte)(compressed[i] ^ mask);
                accumulator = compressed[i] ^ BitOperations.RotateRight(accumulator, (i % 7) + 1);
            }

            WriteU32(output, accumulator);
            output.Write(body);
        }

        return output.ToArray();
    }

    public static byte[] Deflate(byte[] raw)
    {
        var buffer = new MemoryStream();
        using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        return buffer.ToArray();
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> four = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(four, value);
        stream.Write(four);
    }
}
