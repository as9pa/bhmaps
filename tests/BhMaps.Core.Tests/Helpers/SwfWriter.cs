using System.Buffers.Binary;
using System.IO.Compression;

namespace BhMaps.Core.Tests.Helpers;

/// <summary>Builds a minimal SWF whose single DoABC tag carries a constant pool holding the given uints.</summary>
public static class SwfWriter
{
    public static byte[] Uncompressed(IReadOnlyList<uint> uints) => Assemble("FWS"u8.ToArray(), Body(uints));

    public static byte[] Compressed(IReadOnlyList<uint> uints)
    {
        var body = Body(uints);
        var buffer = new MemoryStream();
        using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(body);
        }

        return Assemble("CWS"u8.ToArray(), buffer.ToArray());
    }

    public static byte[] Lzma() => Assemble("ZWS"u8.ToArray(), [0, 0, 0, 0]);

    private static byte[] Assemble(byte[] signature, byte[] body)
    {
        var output = new MemoryStream();
        output.Write(signature);
        output.WriteByte(13);                              // version
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)(8 + body.Length));
        output.Write(length);
        output.Write(body);
        return output.ToArray();
    }

    private static byte[] Body(IReadOnlyList<uint> uints)
    {
        var output = new MemoryStream();
        output.WriteByte(0x00);                            // RECT with nbits 0, so one byte
        output.Write([0x00, 0x0C, 0x01, 0x00]);            // framerate and frame count

        var abc = new MemoryStream();
        abc.Write([0x10, 0x00, 0x2E, 0x00]);               // minor 16, major 46
        abc.Write(U30(1));                                 // int_count 1, so no entries
        abc.Write(U30((uint)uints.Count + 1));             // uint_count
        foreach (var value in uints)
        {
            abc.Write(U30(value));
        }

        var tagBody = new MemoryStream();
        tagBody.Write([0x00, 0x00, 0x00, 0x00]);           // DoABC flags
        tagBody.Write("bhmaps"u8);
        tagBody.WriteByte(0x00);                           // name terminator
        tagBody.Write(abc.ToArray());

        var payload = tagBody.ToArray();
        var header = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)((82 << 6) | 0x3F));
        output.Write(header);
        var longLength = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(longLength, (uint)payload.Length);
        output.Write(longLength);
        output.Write(payload);
        return output.ToArray();
    }

    private static byte[] U30(uint value)
    {
        var bytes = new List<byte>();
        while (true)
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
            {
                bytes.Add((byte)(b | 0x80));
            }
            else
            {
                bytes.Add(b);
                break;
            }
        }

        return bytes.ToArray();
    }
}
