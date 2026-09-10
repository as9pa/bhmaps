using System.Buffers.Binary;
using System.IO.Compression;

namespace BhMaps.Core.LevelData;

/// <summary>The outcome of a key search. Key is null when nothing matched, and Error says why the
/// search could not finish when it could not. CandidatesScanned counts the uints actually tried.</summary>
public sealed record KeySearchResult(uint? Key, int CandidatesScanned, string? Error);

/// <summary>Recovers the SWZ decryption key from BrawlhallaAir.swf, which carries it as one uint
/// among thousands in an ABC constant pool. Ported from a Python reference implementation.</summary>
public static class SwfKeyFinder
{
    public const int DoAbcRawTag = 72;
    public const int DoAbcTag = 82;

    private const string LzmaUnsupported = "This BrawlhallaAir.swf uses LZMA compression, which BhMaps does not read.";

    /// <summary>Scans every uint in every ABC constant pool for one that unlocks swzPath. Never throws.</summary>
    public static KeySearchResult Find(string swfPath, string swzPath)
    {
        var scanned = 0;
        try
        {
            var body = ReadBody(swfPath);
            var header = ReadSwzHeader(swzPath);
            foreach (var (code, tag) in Tags(body))
            {
                var abc = AbcBlob(code, tag);
                if (abc is null)
                {
                    continue;
                }

                IReadOnlyList<uint> uints;
                try
                {
                    uints = AbcUints(abc);
                }
                catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException)
                {
                    // A blob that runs out mid-pool is one unreadable candidate list, not a failed search.
                    continue;
                }

                foreach (var candidate in uints)
                {
                    scanned++;
                    if (SwzKey.Check(header, candidate, out _))
                    {
                        return new KeySearchResult(candidate, scanned, null);
                    }
                }
            }

            return new KeySearchResult(null, scanned, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException or IndexOutOfRangeException)
        {
            // Spec 3.6: a search that cannot run reports a reason and level data falls back, so nothing reaches the UI.
            return new KeySearchResult(null, scanned, ex.Message);
        }
    }

    /// <summary>The uncompressed SWF body, starting at the header RECT.</summary>
    public static byte[] ReadBody(string swfPath)
    {
        var file = File.ReadAllBytes(swfPath);
        if (file.Length < 8)
        {
            throw new InvalidDataException($"{Path.GetFileName(swfPath)} is too short to be an SWF file.");
        }

        var signature = file.AsSpan(0, 3);
        if (signature.SequenceEqual("FWS"u8))
        {
            return file[8..];
        }

        if (signature.SequenceEqual("CWS"u8))
        {
            using var compressed = new MemoryStream(file, 8, file.Length - 8);
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            var body = new MemoryStream();
            zlib.CopyTo(body);
            return body.ToArray();
        }

        if (signature.SequenceEqual("ZWS"u8))
        {
            throw new NotSupportedException(LzmaUnsupported);
        }

        throw new InvalidDataException($"{Path.GetFileName(swfPath)} is not an SWF file.");
    }

    /// <summary>Walks the tag stream after the header RECT. A truncated tail ends the walk instead of throwing.</summary>
    public static IEnumerable<(int Code, byte[] Body)> Tags(byte[] body)
    {
        if (body.Length == 0)
        {
            yield break;
        }

        var nbits = body[0] >> 3;
        var total = 5 + (4 * nbits);
        var offset = (total + 7) / 8;
        offset += 4;                                       // framerate and frame count, two bytes each

        while (offset + 2 <= body.Length)
        {
            var head = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(offset));
            offset += 2;
            var code = head >> 6;
            var declared = (uint)(head & 0x3F);
            if (declared == 0x3F)
            {
                if (offset + 4 > body.Length)
                {
                    yield break;
                }

                declared = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(offset));
                offset += 4;
            }

            // A length past the end clamps to the end, the way the reference's slice does.
            var length = (int)Math.Min(declared, (uint)(body.Length - offset));
            yield return (code, body[offset..(offset + length)]);
            offset += length;
        }
    }

    /// <summary>Every uint in an ABC block's uint constant pool, in pool order.</summary>
    public static IReadOnlyList<uint> AbcUints(byte[] abc)
    {
        var offset = 4;                                    // minor u16 and major u16
        var intCount = ReadU30(abc, ref offset);
        for (var i = 1u; i < intCount; i++)                // entry 0 is the implicit zero and is not stored
        {
            ReadU30(abc, ref offset);
        }

        var uintCount = ReadU30(abc, ref offset);
        var uints = new List<uint>();
        for (var i = 1u; i < uintCount; i++)
        {
            uints.Add(ReadU30(abc, ref offset));
        }

        return uints;
    }

    /// <summary>The ABC bytes a tag carries, or null when it carries none.</summary>
    private static byte[]? AbcBlob(int code, byte[] tag)
    {
        if (code == DoAbcRawTag)
        {
            return tag;
        }

        if (code != DoAbcTag || tag.Length <= 4)
        {
            return null;
        }

        var terminator = Array.IndexOf(tag, (byte)0, 4);   // four bytes of flags, then a NUL-terminated name
        return terminator < 0 ? null : tag[(terminator + 1)..];
    }

    /// <summary>The first 8 bytes of a .swz, which is all SwzKey.Check needs.</summary>
    private static byte[] ReadSwzHeader(string swzPath)
    {
        var header = new byte[8];
        using var stream = File.OpenRead(swzPath);
        if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
        {
            throw new InvalidDataException($"{Path.GetFileName(swzPath)} is too short to be an SWZ file.");
        }

        return header;
    }

    /// <summary>A variable-length unsigned integer. Five bytes is the maximum, and the top three bits
    /// of a fifth byte fall off the shift, exactly as the reference does.</summary>
    private static uint ReadU30(byte[] abc, ref int offset)
    {
        var result = 0u;
        var shift = 0;
        while (true)
        {
            var b = abc[offset++];
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0 || shift >= 28)
            {
                return result;
            }

            shift += 7;
        }
    }
}
