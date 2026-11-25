using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace EdenDeck.Backends.Pcsx2;

/// <summary>What's on a PS2 disc: its serial ("SLUS-20946"), version and boot ELF CRC.</summary>
/// <param name="Crc">The boot ELF's CRC as PCSX2 computes it (XOR of its 32-bit words), or null when unread.</param>
public sealed record Ps2DiscInfo(string Serial, string? Version, uint? Crc);

/// <summary>A disc image read as 2048-byte user-data sectors.</summary>
public interface IDiscImage : IDisposable
{
    /// <summary>Count sectors from <paramref name="lba"/>, or null past the end.</summary>
    byte[]? Read(long lba, int count);
}

/// <summary>
/// Reads a PS2 disc's identity offline, the way PCSX2 does (pcsx2/CDVD/CDVD.cpp): SYSTEM.CNF's
/// <c>BOOT2 = cdrom0:\SLUS_209.46;1</c> gives the serial (SLUS-20946) and the boot ELF, whose
/// 32-bit words XORed together are the CRC that PCSX2 names per-game settings, cheats and patches
/// after. Works on .iso (2048 or raw 2352-byte sectors), .cue/.bin, .cso and .zso. A .chd can't be
/// read without codecs .NET lacks; <see cref="ChdInfo"/> reads its header instead.
/// </summary>
public static class Ps2Disc
{
    public const int Sector = 2048;

    /// <summary>The serial, version and CRC, or null when it isn't a PS2 disc (PS1 discs included) or can't be read.</summary>
    public static Ps2DiscInfo? Read(string path)
    {
        using var image = Open(path);
        return image == null ? null : Read(image);
    }

    public static IDiscImage? Open(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".cue")
        {
            return CueTrack(path) is { } bin && File.Exists(bin) ? RawImage.Detect(File.OpenRead(bin)) : null;
        }
        var s = File.OpenRead(path);
        try
        {
            var magic = new byte[4];
            if (s.Read(magic, 0, 4) == 4)
            {
                var tag = Encoding.ASCII.GetString(magic);
                if (tag is "CISO" or "ZISO")
                {
                    return new BlockImage(s, tag == "ZISO");
                }
            }
            return RawImage.Detect(s);
        }
        catch
        {
            s.Dispose();
            throw;
        }
    }

    /// <summary>The data file of a .cue's first track, when that's a MODE1/MODE2 data track.</summary>
    internal static string? CueTrack(string cue)
    {
        string? file = null;
        foreach (var raw in File.ReadLines(cue).Take(200))
        {
            var line = raw.Trim();
            if (line.StartsWith("FILE ", StringComparison.OrdinalIgnoreCase))
            {
                var q1 = line.IndexOf('"');
                var q2 = q1 >= 0 ? line.IndexOf('"', q1 + 1) : -1;
                file = q2 > q1 ? line[(q1 + 1)..q2] : line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
            }
            else if (line.StartsWith("TRACK ", StringComparison.OrdinalIgnoreCase))
            {
                return file != null && line.Contains("MODE", StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(Path.GetDirectoryName(cue) ?? "", file)
                    : null;
            }
        }
        return null;
    }

    public static Ps2DiscInfo? Read(IDiscImage image)
    {
        var pvd = image.Read(16, 1);
        if (pvd == null || Encoding.ASCII.GetString(pvd, 1, 5) != "CD001")
        {
            return null;
        }
        var root = (BitConverter.ToUInt32(pvd, 156 + 2), BitConverter.ToUInt32(pvd, 156 + 10));
        if (ReadFile(image, root, ["SYSTEM.CNF"], 64 << 10) is not { } cnf)
        {
            return null;
        }
        var (boot, version) = ParseSystemCnf(Encoding.ASCII.GetString(cnf));
        if (boot == null || Serial(boot) is not { } serial)
        {
            return null;
        }
        uint? crc = null;
        if (ReadFile(image, root, ElfPath(boot), 64 << 20) is { } elf)
        {
            crc = ElfCrc(elf);
        }
        return new Ps2DiscInfo(serial, version, crc);
    }

    /// <summary>BOOT2 (a PS2 disc) and VER from SYSTEM.CNF. A PS1 disc (BOOT) gives no boot path.</summary>
    public static (string? Boot, string? Version) ParseSystemCnf(string text)
    {
        string? boot = null, version = null;
        foreach (var raw in text.Split('\n'))
        {
            var eq = raw.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }
            var key = raw[..eq].Trim();
            var value = raw[(eq + 1)..].Trim().TrimEnd('\0').Trim();
            if (key == "BOOT2")
            {
                boot = value;
            }
            else if (key == "VER")
            {
                version = value;
            }
        }
        return (string.IsNullOrEmpty(boot) ? null : boot, string.IsNullOrEmpty(version) ? null : version);
    }

    /// <summary>"cdrom0:\SLUS_209.46;1" -> "SLUS-20946" (PCSX2's ExecutablePathToSerial), or null for a non-serial name.</summary>
    public static string? Serial(string boot)
    {
        var slash = boot.LastIndexOf('\\');
        var name = slash >= 0 ? boot[(slash + 1)..] : boot[(boot.LastIndexOf(':') + 1)..];
        var semi = name.LastIndexOf(';');
        if (semi >= 0)
        {
            name = name[..semi];
        }
        // "????_???.??*" or "????-???.??*"
        if (name.Length < 11 || (name[4] != '_' && name[4] != '-') || name[8] != '.')
        {
            return null;
        }
        var s = name.Replace(".", "").Replace('_', '-').ToUpperInvariant();
        return s.Length > 0 ? s : null;
    }

    /// <summary>The ELF's path on the disc: "cdrom0:\DIR\FILE.ELF;1" -> ["DIR", "FILE.ELF"].</summary>
    internal static IReadOnlyList<string> ElfPath(string boot)
    {
        var colon = boot.IndexOf(':');
        var p = colon >= 0 ? boot[(colon + 1)..] : boot;
        var semi = p.IndexOf(';');
        if (semi >= 0)
        {
            p = p[..semi];
        }
        return p.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>PCSX2's ElfObject::GetCRC: every whole 32-bit little-endian word XORed.</summary>
    public static uint ElfCrc(ReadOnlySpan<byte> elf)
    {
        uint crc = 0;
        for (var i = 0; i + 4 <= elf.Length; i += 4)
        {
            crc ^= BinaryPrimitives.ReadUInt32LittleEndian(elf[i..]);
        }
        return crc;
    }

    private static byte[]? ReadFile(IDiscImage image, (uint Extent, uint Size) dir, IReadOnlyList<string> path, int maxSize)
    {
        for (var i = 0; i < path.Count; i++)
        {
            if (Find(image, dir.Extent, dir.Size, path[i]) is not { } found)
            {
                return null;
            }
            if (i == path.Count - 1)
            {
                if (found.IsDir || found.Size > maxSize)
                {
                    return null;
                }
                var data = image.Read(found.Extent, (int)((found.Size + Sector - 1) / Sector));
                return data?[..(int)found.Size];
            }
            if (!found.IsDir)
            {
                return null;
            }
            dir = (found.Extent, found.Size);
        }
        return null;
    }

    private static (uint Extent, uint Size, bool IsDir)? Find(IDiscImage image, uint extent, uint size, string name)
    {
        if (size == 0 || size > 4 << 20 || image.Read(extent, (int)((size + Sector - 1) / Sector)) is not { } dir)
        {
            return null;
        }
        var pos = 0;
        while (pos < size)
        {
            int len = dir[pos];
            if (len == 0)
            {
                pos = (pos / Sector + 1) * Sector;  // records don't cross sectors; the rest is padding
                continue;
            }
            if (pos + len > dir.Length || len < 34)
            {
                break;
            }
            var idLen = dir[pos + 32];
            var id = Encoding.ASCII.GetString(dir, pos + 33, Math.Min(idLen, len - 33));
            var semi = id.IndexOf(';');
            if (semi >= 0)
            {
                id = id[..semi];
            }
            if (id.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return (BitConverter.ToUInt32(dir, pos + 2), BitConverter.ToUInt32(dir, pos + 10), (dir[pos + 25] & 2) != 0);
            }
            pos += len;
        }
        return null;
    }

    /// <summary>An .iso or .bin: 2048-byte sectors, or raw 2352-byte CD sectors (mode 1 or mode 2).</summary>
    internal sealed class RawImage(Stream s, int sectorSize, int dataOffset) : IDiscImage
    {
        public static RawImage? Detect(Stream s)
        {
            var buf = new byte[16];
            // A 2048-byte image has "CD001" at sector 16.
            if (s.Length >= 17 * Sector)
            {
                s.Position = 16L * Sector;
                s.ReadExactly(buf, 0, 6);
                if (Encoding.ASCII.GetString(buf, 1, 5) == "CD001")
                {
                    return new RawImage(s, Sector, 0);
                }
            }
            // A raw CD image: sector 16 starts with the sync pattern; byte 15 is the mode.
            if (s.Length >= 17 * 2352)
            {
                s.Position = 16L * 2352;
                s.ReadExactly(buf, 0, 16);
                if (buf[0] == 0 && buf.AsSpan(1, 10).IndexOfAnyExcept((byte)0xFF) < 0 && buf[11] == 0 && buf[15] is 1 or 2)
                {
                    return new RawImage(s, 2352, buf[15] == 1 ? 16 : 24);
                }
            }
            s.Dispose();
            return null;
        }

        public byte[]? Read(long lba, int count)
        {
            if (lba < 0 || count <= 0 || (lba + count) * sectorSize > s.Length)
            {
                return null;
            }
            var result = new byte[count * Sector];
            if (sectorSize == Sector)
            {
                s.Position = lba * Sector;
                s.ReadExactly(result);
                return result;
            }
            var raw = new byte[sectorSize];
            for (var i = 0; i < count; i++)
            {
                s.Position = (lba + i) * sectorSize;
                s.ReadExactly(raw);
                Array.Copy(raw, dataOffset, result, i * Sector, Sector);
            }
            return result;
        }

        public void Dispose() => s.Dispose();
    }

    /// <summary>
    /// CSO (deflate) and ZSO (LZ4) images: a block index after a 24-byte header; an index entry's
    /// top bit marks a block stored uncompressed, and positions are shifted by the header's align.
    /// </summary>
    internal sealed class BlockImage : IDiscImage
    {
        private readonly Stream _s;
        private readonly bool _lz4;
        private readonly long _total;
        private readonly int _blockSize;
        private readonly int _align;
        private readonly uint[] _index;

        public BlockImage(Stream s, bool lz4)
        {
            _s = s;
            _lz4 = lz4;
            var h = new byte[24];
            s.Position = 0;
            s.ReadExactly(h);
            var headerSize = BitConverter.ToUInt32(h, 4);
            _total = BitConverter.ToInt64(h, 8);
            _blockSize = (int)BitConverter.ToUInt32(h, 16);
            _align = h[21];
            if (_blockSize is < Sector or > 1 << 20 || _blockSize % Sector != 0 || _total <= 0 || _align > 16)
            {
                throw new InvalidDataException("Not a readable CSO/ZSO image.");
            }
            var blocks = (int)((_total + _blockSize - 1) / _blockSize);
            var raw = new byte[(blocks + 1) * 4];
            s.Position = headerSize is >= 24 and < 4096 ? headerSize : 24;
            s.ReadExactly(raw);
            _index = new uint[blocks + 1];
            Buffer.BlockCopy(raw, 0, _index, 0, raw.Length);
        }

        public byte[]? Read(long lba, int count)
        {
            var start = lba * Sector;
            var length = (long)count * Sector;
            if (lba < 0 || count <= 0 || start + length > _total)
            {
                return null;
            }
            var result = new byte[length];
            var done = 0L;
            while (done < length)
            {
                var pos = start + done;
                var block = (int)(pos / _blockSize);
                var data = Block(block);
                var within = (int)(pos % _blockSize);
                var n = (int)Math.Min(length - done, data.Length - within);
                if (n <= 0)
                {
                    return null;
                }
                Array.Copy(data, within, result, done, n);
                done += n;
            }
            return result;
        }

        private byte[] Block(int i)
        {
            var entry = _index[i];
            var pos = (long)(entry & 0x7FFFFFFF) << _align;
            var end = (long)(_index[i + 1] & 0x7FFFFFFF) << _align;
            var stored = (entry & 0x80000000) != 0;
            var size = (int)Math.Clamp(end - pos, 0, _blockSize + 1024);
            var src = new byte[size];
            _s.Position = pos;
            var read = _s.Read(src, 0, size);
            if (stored)
            {
                return src[..Math.Min(read, _blockSize)];
            }
            var dst = new byte[_blockSize];
            if (_lz4)
            {
                var n = Lz4Block(src.AsSpan(0, read), dst);
                return dst[..n];
            }
            using var z = new DeflateStream(new MemoryStream(src, 0, read), CompressionMode.Decompress);
            var got = 0;
            int r;
            while (got < dst.Length && (r = z.Read(dst, got, dst.Length - got)) > 0)
            {
                got += r;
            }
            return dst[..got];
        }

        public void Dispose() => _s.Dispose();
    }

    /// <summary>Decodes one raw LZ4 block (no frame header). Returns the bytes written.</summary>
    internal static int Lz4Block(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        int ip = 0, op = 0;
        while (ip < src.Length)
        {
            var token = src[ip++];
            var lit = token >> 4;
            if (lit == 15)
            {
                byte b;
                do
                {
                    b = src[ip++];
                    lit += b;
                } while (b == 255);
            }
            if (op + lit > dst.Length || ip + lit > src.Length)
            {
                throw new InvalidDataException("Corrupt LZ4 block.");
            }
            src.Slice(ip, lit).CopyTo(dst[op..]);
            ip += lit;
            op += lit;
            if (ip >= src.Length)
            {
                break;
            }
            var offset = src[ip] | (src[ip + 1] << 8);
            ip += 2;
            var match = token & 15;
            if (match == 15)
            {
                byte b;
                do
                {
                    b = src[ip++];
                    match += b;
                } while (b == 255);
            }
            match += 4;
            if (offset == 0 || offset > op || op + match > dst.Length)
            {
                throw new InvalidDataException("Corrupt LZ4 block.");
            }
            for (var k = 0; k < match; k++, op++)
            {
                dst[op] = dst[op - offset];
            }
        }
        return op;
    }
}

/// <summary>
/// A CHD's header (MAME's compressed hunks format, v5): the SHA-1 of the uncompressed data, which
/// for a DVD image made with <c>chdman createdvd</c> is the original .iso's SHA-1, and whether it's
/// a DVD (a <c>DVD </c> metadata entry) or a CD (track metadata).
/// </summary>
public sealed record ChdInfo(string RawSha1, bool Dvd)
{
    public static ChdInfo? Read(string path)
    {
        using var s = File.OpenRead(path);
        var h = new byte[124];
        if (s.Length < h.Length || s.Read(h, 0, h.Length) != h.Length || Encoding.ASCII.GetString(h, 0, 8) != "MComprHD" ||
            BinaryPrimitives.ReadUInt32BigEndian(h.AsSpan(12)) != 5)
        {
            return null;
        }
        var sha = Convert.ToHexString(h, 0x40, 20).ToLowerInvariant();
        var meta = (long)BinaryPrimitives.ReadUInt64BigEndian(h.AsSpan(0x30));
        var dvd = false;
        var entry = new byte[16];
        for (var n = 0; meta > 0 && meta + 16 <= s.Length && n < 512; n++)
        {
            s.Position = meta;
            s.ReadExactly(entry);
            var tag = Encoding.ASCII.GetString(entry, 0, 4);
            if (tag == "DVD ")
            {
                dvd = true;
                break;
            }
            meta = (long)BinaryPrimitives.ReadUInt64BigEndian(entry.AsSpan(8));
        }
        return new ChdInfo(sha, dvd);
    }
}
