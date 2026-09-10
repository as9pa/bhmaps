using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text;

namespace BhMaps.Core.LevelData;

/// <summary>One decrypted container entry, identified by the root element of its document.</summary>
public sealed record SwzEntry(string RootElement, string Xml);

/// <summary>Turns an encrypted .swz container into its XML entries.
/// Port of read_swz in docs/superpowers/reference/swz.py.</summary>
public static class SwzReader
{
    private const int HeaderSize = 8;
    private const int EntryHeaderSize = 12;

    /// <summary>Every entry as UTF-8 XML. Throws InvalidDataException on a bad key or a failed checksum.</summary>
    public static IReadOnlyList<SwzEntry> Read(byte[] container, uint key)
    {
        if (container.Length < HeaderSize)
        {
            throw new InvalidDataException($"Not a .swz container: {container.Length} bytes is shorter than the {HeaderSize} byte header.");
        }

        if (!SwzKey.Check(container.AsSpan(0, HeaderSize), key, out var rng))
        {
            throw new InvalidDataException("Wrong .swz key: the header checksum does not match.");
        }

        var entries = new List<SwzEntry>();
        var offset = HeaderSize;
        while (container.Length - offset > EntryHeaderSize)
        {
            var compressedSize = BinaryPrimitives.ReadUInt32BigEndian(container.AsSpan(offset)) ^ rng.Next();
            offset += 4;
            var uncompressedSize = BinaryPrimitives.ReadUInt32BigEndian(container.AsSpan(offset)) ^ rng.Next();
            offset += 4;
            var expected = BinaryPrimitives.ReadUInt32BigEndian(container.AsSpan(offset));
            offset += 4;

            if (compressedSize > (uint)(container.Length - offset))
            {
                throw new InvalidDataException($"Truncated .swz container: entry {entries.Count} claims {compressedSize} compressed bytes but only {container.Length - offset} are left.");
            }

            var compressed = new byte[compressedSize];
            var checksum = rng.Next();
            for (var i = 0; i < compressed.Length; i++)
            {
                var random = rng.Next();
                var bitIndex = i & 0xF;
                var mask = (byte)(((0xFFu << bitIndex) & random) >> bitIndex);
                var plain = (byte)(container[offset + i] ^ mask);
                compressed[i] = plain;
                checksum = plain ^ BitOperations.RotateRight(checksum, (i % 7) + 1);
            }

            offset += compressed.Length;
            if (checksum != expected)
            {
                throw new InvalidDataException($"Corrupt .swz container: the checksum of entry {entries.Count} does not match.");
            }

            var xml = Inflate(compressed, uncompressedSize, entries.Count);
            entries.Add(new SwzEntry(RootElementName(xml), xml));
        }

        return entries;
    }

    /// <summary>The container file at the given path, read whole and decrypted.</summary>
    public static IReadOnlyList<SwzEntry> ReadFile(string path, uint key) => Read(File.ReadAllBytes(path), key);

    /// <summary>The root element name without parsing the document: the first name
    /// after the first left angle bracket that is not '?' or '!'.</summary>
    public static string RootElementName(string xml)
    {
        var bracket = xml.IndexOf('<');
        while (bracket >= 0 && bracket + 1 < xml.Length)
        {
            var start = bracket + 1;
            if (xml[start] is not ('?' or '!'))
            {
                var end = start;
                while (end < xml.Length && !char.IsWhiteSpace(xml[end]) && xml[end] is not ('/' or '>'))
                {
                    end++;
                }

                return xml[start..end];
            }

            bracket = xml.IndexOf('<', start);
        }

        return string.Empty;
    }

    private static string Inflate(byte[] compressed, uint uncompressedSize, int index)
    {
        var raw = new MemoryStream();
        try
        {
            using var zlib = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);
            zlib.CopyTo(raw);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"Entry {index} of the .swz container did not inflate.", ex);
        }

        if (raw.Length != uncompressedSize)
        {
            throw new InvalidDataException($"Entry {index} of the .swz container inflated to {raw.Length} bytes, not the {uncompressedSize} its header declares.");
        }

        return Encoding.UTF8.GetString(raw.GetBuffer(), 0, (int)raw.Length);
    }
}
