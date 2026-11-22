using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EdenDeck.Backends.Eden.Library;

/// <summary>A file entry inside a PFS0 (NSP/NSZ) or HFS0 (XCI partition) container.</summary>
public sealed record ContainerEntry(string Name, long Offset, long Size);

/// <summary>Readers for the Switch container formats EdenDeck needs to identify games.</summary>
public static class SwitchFormats
{
    public static IReadOnlyList<ContainerEntry> ReadPfs0(Stream s, long baseOffset) => ReadPartitionFs(s, baseOffset, "PFS0", 0x18);

    public static IReadOnlyList<ContainerEntry> ReadHfs0(Stream s, long baseOffset) => ReadPartitionFs(s, baseOffset, "HFS0", 0x40);

    private static IReadOnlyList<ContainerEntry> ReadPartitionFs(Stream s, long baseOffset, string magic, int entrySize)
    {
        var header = ReadAt(s, baseOffset, 0x10);
        if (Encoding.ASCII.GetString(header, 0, 4) != magic)
        {
            throw new InvalidDataException($"not a {magic} container");
        }
        var count = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        var strTableSize = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
        if (count is < 0 or > 10000 || strTableSize is < 0 or > 1 << 20)
        {
            throw new InvalidDataException($"implausible {magic} header");
        }
        var entries = ReadAt(s, baseOffset + 0x10, count * entrySize);
        var strings = ReadAt(s, baseOffset + 0x10 + count * entrySize, strTableSize);
        var dataStart = baseOffset + 0x10 + (long)count * entrySize + strTableSize;
        var result = new List<ContainerEntry>(count);
        for (var i = 0; i < count; i++)
        {
            var e = entries.AsSpan(i * entrySize);
            var offset = BinaryPrimitives.ReadInt64LittleEndian(e);
            var size = BinaryPrimitives.ReadInt64LittleEndian(e[8..]);
            var nameOffset = BinaryPrimitives.ReadInt32LittleEndian(e[16..]);
            var end = Array.IndexOf(strings, (byte)0, nameOffset);
            var name = Encoding.UTF8.GetString(strings, nameOffset, (end < 0 ? strings.Length : end) - nameOffset);
            result.Add(new ContainerEntry(name, dataStart + offset, size));
        }
        return result;
    }

    /// <summary>XCI: the root HFS0 lists partitions; NCAs live in "secure".</summary>
    public static IReadOnlyList<ContainerEntry> ReadXciSecurePartition(Stream s)
    {
        var header = ReadAt(s, 0x100, 0x40);
        if (Encoding.ASCII.GetString(header, 0, 4) != "HEAD")
        {
            throw new InvalidDataException("not an XCI");
        }
        var rootOffset = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(0x30));
        var root = ReadHfs0(s, rootOffset);
        var secure = root.FirstOrDefault(e => e.Name == "secure") ?? throw new InvalidDataException("XCI has no secure partition");
        return ReadHfs0(s, secure.Offset);
    }

    internal static byte[] ReadAt(Stream s, long offset, int length)
    {
        var buf = new byte[length];
        s.Seek(offset, SeekOrigin.Begin);
        s.ReadExactly(buf);
        return buf;
    }
}

public enum NcaContentType : byte
{
    Program = 0,
    Meta = 1,
    Control = 2,
    Manual = 3,
    Data = 4,
    PublicData = 5,
}

public sealed record NcaHeader(NcaContentType ContentType, ulong ProgramId, string Magic);

/// <summary>
/// Decrypts the first 0xC00 bytes of an NCA (AES-128-XTS, 0x200-byte sectors, big-endian
/// sector tweak) with the user's own <c>header_key</c> from prod.keys.
/// </summary>
public static class NcaHeaderReader
{
    private const int HeaderSize = 0xC00;
    private const int SectorSize = 0x200;

    public static NcaHeader Read(Stream s, long offset, byte[] headerKey)
    {
        var enc = SwitchFormats.ReadAt(s, offset, HeaderSize);
        var dec = XtsDecrypt(enc, headerKey, SectorSize);
        var magic = Encoding.ASCII.GetString(dec, 0x200, 4);
        if (magic is not ("NCA3" or "NCA2" or "NCA0"))
        {
            throw new InvalidDataException("NCA header did not decrypt (wrong header_key or not an NCA)");
        }
        var type = (NcaContentType)dec[0x205];
        var programId = BinaryPrimitives.ReadUInt64LittleEndian(dec.AsSpan(0x210));
        return new NcaHeader(type, programId, magic);
    }

    internal static byte[] XtsDecrypt(byte[] data, byte[] key, int sectorSize)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("header_key must be 32 bytes");
        }
        using var dataAes = Aes.Create();
        dataAes.Key = key[..16];
        using var tweakAes = Aes.Create();
        tweakAes.Key = key[16..];
        var output = new byte[data.Length];
        var tweak = new byte[16];
        var block = new byte[16];
        for (var sector = 0; sector * sectorSize < data.Length; sector++)
        {
            Array.Clear(tweak);
            BinaryPrimitives.WriteUInt64BigEndian(tweak.AsSpan(8), (ulong)sector);
            tweakAes.EncryptEcb(tweak, tweak, PaddingMode.None);
            var start = sector * sectorSize;
            var end = Math.Min(start + sectorSize, data.Length);
            for (var off = start; off < end; off += 16)
            {
                for (var i = 0; i < 16; i++)
                {
                    block[i] = (byte)(data[off + i] ^ tweak[i]);
                }
                dataAes.DecryptEcb(block, block, PaddingMode.None);
                for (var i = 0; i < 16; i++)
                {
                    output[off + i] = (byte)(block[i] ^ tweak[i]);
                }
                MultiplyAlpha(tweak);
            }
        }
        return output;
    }

    // Test helper: XTS encryption (inverse of XtsDecrypt).
    internal static byte[] XtsEncrypt(byte[] data, byte[] key, int sectorSize)
    {
        using var dataAes = Aes.Create();
        dataAes.Key = key[..16];
        using var tweakAes = Aes.Create();
        tweakAes.Key = key[16..];
        var output = new byte[data.Length];
        var tweak = new byte[16];
        var block = new byte[16];
        for (var sector = 0; sector * sectorSize < data.Length; sector++)
        {
            Array.Clear(tweak);
            BinaryPrimitives.WriteUInt64BigEndian(tweak.AsSpan(8), (ulong)sector);
            tweakAes.EncryptEcb(tweak, tweak, PaddingMode.None);
            var start = sector * sectorSize;
            var end = Math.Min(start + sectorSize, data.Length);
            for (var off = start; off < end; off += 16)
            {
                for (var i = 0; i < 16; i++)
                {
                    block[i] = (byte)(data[off + i] ^ tweak[i]);
                }
                dataAes.EncryptEcb(block, block, PaddingMode.None);
                for (var i = 0; i < 16; i++)
                {
                    output[off + i] = (byte)(block[i] ^ tweak[i]);
                }
                MultiplyAlpha(tweak);
            }
        }
        return output;
    }

    private static void MultiplyAlpha(byte[] t)
    {
        var carry = 0;
        for (var i = 0; i < 16; i++)
        {
            var next = t[i] >> 7;
            t[i] = (byte)((t[i] << 1) | carry);
            carry = next;
        }
        if (carry != 0)
        {
            t[0] ^= 0x87;
        }
    }
}

/// <summary>Parser for the user's own prod.keys / title.keys ("name = hex" lines).</summary>
public static class KeyFile
{
    public static Dictionary<string, byte[]> Parse(string text)
    {
        var keys = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#' or '[')
            {
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }
            var name = line[..eq].Trim();
            var hex = line[(eq + 1)..].Trim();
            if (hex.Length % 2 != 0 || !hex.All(Uri.IsHexDigit))
            {
                continue;
            }
            keys[name] = Convert.FromHexString(hex);
        }
        return keys;
    }

    public static bool TryGetHeaderKey(IReadOnlyDictionary<string, byte[]> keys, out byte[] headerKey)
    {
        if (keys.TryGetValue("header_key", out var k) && k.Length == 32)
        {
            headerKey = k;
            return true;
        }
        headerKey = [];
        return false;
    }

    public static string FormatTitleId(ulong tid) => tid.ToString("X16", CultureInfo.InvariantCulture);
}
