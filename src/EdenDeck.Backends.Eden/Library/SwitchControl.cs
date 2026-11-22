using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EdenDeck.Backends.Eden.Library;

/// <summary>A game's control data: its square icon (JPEG) and its name, as the console shows them.</summary>
public sealed record ControlData(byte[]? IconJpeg, string? Name);

/// <summary>
/// Reads a game's control NCA (icon and name) from an NSP or XCI with the user's own keys:
/// header_key for the NCA header, then either the key area (key_area_key_application_XX) or a
/// title key (from the NSP's ticket or the user's title.keys, decrypted with titlekek_XX), and
/// AES-CTR for the RomFS section that holds icon_&lt;Language&gt;.dat and control.nacp.
/// Compressed containers (.nsz/.xcz with .ncz entries) aren't read.
/// </summary>
public static class SwitchControl
{
    private const int HeaderSize = 0xC00;

    public static ControlData? Read(string path, IReadOnlyDictionary<string, byte[]> prodKeys, IReadOnlyDictionary<string, byte[]>? titleKeys = null)
    {
        if (!KeyFile.TryGetHeaderKey(prodKeys, out var headerKey))
        {
            return null;
        }
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);
        var head = new byte[0x104];
        if (fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) < head.Length)
        {
            return null;
        }
        var entries = head[0x100] == 'H' && head[0x101] == 'E' && head[0x102] == 'A' && head[0x103] == 'D'
            ? SwitchFormats.ReadXciSecurePartition(fs)
            : SwitchFormats.ReadPfs0(fs, 0);
        var tickets = ReadTickets(fs, entries);
        foreach (var e in entries.Where(e => e.Name.EndsWith(".nca", StringComparison.OrdinalIgnoreCase) && e.Size >= HeaderSize))
        {
            var header = NcaHeaderReader.XtsDecrypt(SwitchFormats.ReadAt(fs, e.Offset, HeaderSize), headerKey, 0x200);
            if (Encoding.ASCII.GetString(header, 0x200, 4) != "NCA3" || (NcaContentType)header[0x205] != NcaContentType.Control)
            {
                continue;
            }
            var key = ContentKey(header, prodKeys, titleKeys, tickets);
            if (key == null)
            {
                continue;
            }
            if (ReadRomFs(fs, e.Offset, header, key) is { } data)
            {
                return data;
            }
        }
        return null;
    }

    /// <summary>The AES-CTR key for the NCA's sections, or null if the user's keys don't have it.</summary>
    private static byte[]? ContentKey(byte[] header, IReadOnlyDictionary<string, byte[]> prodKeys, IReadOnlyDictionary<string, byte[]>? titleKeys, Dictionary<string, byte[]> tickets)
    {
        var gen = Math.Max(header[0x206], header[0x220]);
        var mk = Math.Max(0, gen - 1).ToString("x2", CultureInfo.InvariantCulture);
        var rights = header.AsSpan(0x230, 16);
        if (rights.IndexOfAnyExcept((byte)0) >= 0)
        {
            var rid = Convert.ToHexString(rights).ToLowerInvariant();
            byte[]? enc = tickets.GetValueOrDefault(rid);
            if (enc == null && titleKeys != null)
            {
                titleKeys.TryGetValue(rid, out enc);
            }
            if (enc is not { Length: 16 } || !prodKeys.TryGetValue("titlekek_" + mk, out var kek) || kek.Length != 16)
            {
                return null;
            }
            return Ecb(kek, enc);
        }
        var kind = header[0x207] switch { 0 => "application", 1 => "ocean", _ => "system" };
        if (!prodKeys.TryGetValue($"key_area_key_{kind}_{mk}", out var kak) || kak.Length != 16)
        {
            return null;
        }
        return Ecb(kak, header.AsSpan(0x300 + 2 * 16, 16).ToArray());  // key area slot 2: the CTR key
    }

    /// <summary>NSP tickets (.tik): rights id → encrypted common title key.</summary>
    private static Dictionary<string, byte[]> ReadTickets(Stream fs, IReadOnlyList<ContainerEntry> entries)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries.Where(e => e.Name.EndsWith(".tik", StringComparison.OrdinalIgnoreCase) && e.Size >= 0x2C0 && e.Size < 0x10000))
        {
            var t = SwitchFormats.ReadAt(fs, e.Offset, (int)e.Size);
            var sigType = BinaryPrimitives.ReadUInt32LittleEndian(t);
            var sigBlock = sigType switch
            {
                0x10000 or 0x10003 => 4 + 0x200 + 0x3C,
                0x10001 or 0x10004 => 4 + 0x100 + 0x3C,
                0x10002 or 0x10005 => 4 + 0x3C + 0x40,
                _ => -1,
            };
            if (sigBlock < 0 || sigBlock + 0x180 > t.Length)
            {
                continue;
            }
            var titleKey = t.AsSpan(sigBlock + 0x40, 16).ToArray();  // after the 0x40-byte issuer
            var rightsId = Convert.ToHexString(t.AsSpan(sigBlock + 0x160, 16)).ToLowerInvariant();
            result[rightsId] = titleKey;
        }
        return result;
    }

    private static ControlData? ReadRomFs(Stream fs, long ncaOffset, byte[] header, byte[] key)
    {
        for (var i = 0; i < 4; i++)
        {
            var start = (long)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0x240 + i * 0x10)) * 0x200;
            var end = (long)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0x244 + i * 0x10)) * 0x200;
            var fsh = header.AsSpan(0x400 + i * 0x200, 0x200);
            // RomFS (fs type 0) with an IVFC hash tree (hash type 3), AES-CTR (encryption type 3).
            if (end <= start || fsh[2] != 0 || fsh[3] != 3 || fsh[4] != 3 || Encoding.ASCII.GetString(fsh.Slice(8, 4)) != "IVFC")
            {
                continue;
            }
            var levels = BinaryPrimitives.ReadUInt32LittleEndian(fsh[0x14..]);
            var dataLevel = (int)Math.Clamp(levels - 2, 0, 5);
            var romfs = BinaryPrimitives.ReadInt64LittleEndian(fsh.Slice(0x18 + dataLevel * 0x18));
            var ctrHigh = new byte[8];
            for (var b = 0; b < 8; b++)
            {
                ctrHigh[b] = fsh[0x140 + 7 - b];
            }
            var section = new CtrReader(fs, ncaOffset + start, end - start, key, ctrHigh, start);
            return ReadFiles(section, romfs);
        }
        return null;
    }

    private static ControlData? ReadFiles(CtrReader s, long romfs)
    {
        var h = s.Read(romfs, 0x50);
        if (BinaryPrimitives.ReadInt64LittleEndian(h) != 0x50)
        {
            return null;
        }
        var fileMetaOff = BinaryPrimitives.ReadInt64LittleEndian(h.AsSpan(0x38));
        var fileMetaSize = BinaryPrimitives.ReadInt64LittleEndian(h.AsSpan(0x40));
        var dataOff = BinaryPrimitives.ReadInt64LittleEndian(h.AsSpan(0x48));
        if (fileMetaSize is <= 0 or > 1 << 20)
        {
            return null;
        }
        var meta = s.Read(romfs + fileMetaOff, (int)fileMetaSize);
        var files = new Dictionary<string, (long Off, long Size)>(StringComparer.Ordinal);
        for (var p = 0; p + 0x20 <= meta.Length;)
        {
            var off = BinaryPrimitives.ReadInt64LittleEndian(meta.AsSpan(p + 8));
            var size = BinaryPrimitives.ReadInt64LittleEndian(meta.AsSpan(p + 16));
            var nameLen = BinaryPrimitives.ReadInt32LittleEndian(meta.AsSpan(p + 28));
            if (nameLen < 0 || p + 0x20 + nameLen > meta.Length)
            {
                break;
            }
            files[Encoding.UTF8.GetString(meta, p + 0x20, nameLen)] = (off, size);
            p += 0x20 + ((nameLen + 3) & ~3);
        }
        byte[]? icon = null;
        var iconName = files.ContainsKey("icon_AmericanEnglish.dat") ? "icon_AmericanEnglish.dat"
            : files.Keys.Where(n => n.StartsWith("icon_", StringComparison.Ordinal)).Order(StringComparer.Ordinal).FirstOrDefault();
        if (iconName != null && files[iconName] is var (io, isz) && isz is > 0 and < 1 << 20)
        {
            icon = s.Read(romfs + dataOff + io, (int)isz);
        }
        string? name = null;
        if (files.TryGetValue("control.nacp", out var nacp) && nacp.Size >= 0x200)
        {
            var raw = s.Read(romfs + dataOff + nacp.Off, 0x200);  // language 0 (American English) name
            var end = Array.IndexOf(raw, (byte)0);
            name = Encoding.UTF8.GetString(raw, 0, end < 0 ? raw.Length : end).Trim();
            if (name.Length == 0)
            {
                name = null;
            }
        }
        return icon == null && name == null ? null : new ControlData(icon, name);
    }

    private static byte[] Ecb(byte[] key, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.DecryptEcb(data, PaddingMode.None);
    }

    /// <summary>Random-access reads from an AES-CTR section (counter = section ctr || absolute NCA offset / 16, big-endian).</summary>
    internal sealed class CtrReader(Stream fs, long sectionStart, long sectionSize, byte[] key, byte[] ctrHigh, long ncaRelativeStart)
    {
        public byte[] Read(long offset, int length)
        {
            if (offset < 0 || offset + length > sectionSize)
            {
                throw new InvalidDataException("read outside the NCA section");
            }
            var aligned = offset & ~15L;
            var pad = (int)(offset - aligned);
            var total = (pad + length + 15) & ~15;
            var enc = SwitchFormats.ReadAt(fs, sectionStart + aligned, (int)Math.Min(total, sectionSize - aligned));
            var dec = Transform(enc, key, ctrHigh, ncaRelativeStart + aligned);
            return dec.AsSpan(pad, length).ToArray();
        }
    }

    /// <summary>AES-128-CTR (same operation both ways) starting at an absolute NCA offset (multiple of 16).</summary>
    internal static byte[] Transform(byte[] data, byte[] key, byte[] ctrHigh, long ncaOffset)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        var output = new byte[data.Length];
        var counter = new byte[16];
        ctrHigh.CopyTo(counter, 0);
        var block = (ulong)(ncaOffset / 16);
        var ks = new byte[16];
        for (var off = 0; off < data.Length; off += 16, block++)
        {
            BinaryPrimitives.WriteUInt64BigEndian(counter.AsSpan(8), block);
            aes.EncryptEcb(counter, ks, PaddingMode.None);
            for (var i = 0; i < 16 && off + i < data.Length; i++)
            {
                output[off + i] = (byte)(data[off + i] ^ ks[i]);
            }
        }
        return output;
    }
}
