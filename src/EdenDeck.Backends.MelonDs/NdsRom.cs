using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace EdenDeck.Backends.MelonDs;

/// <summary>What a DS cartridge image's header says: its internal title, game code ("AMCE") and maker.</summary>
public sealed record NdsHeader(string Title, string GameCode, string Maker, uint BannerOffset, uint HeaderCrc)
{
    /// <summary>A real game code: four letters or digits (homebrew often has "####" or zeros).</summary>
    public bool HasGameCode => GameCode.Length == 4 && GameCode.All(char.IsAsciiLetterOrDigit) && GameCode != "0000";

    /// <summary>
    /// Scanline's id for the game: "NTR-AMCE" (the DS product code, so it can't clash with a GBA
    /// game's four-letter code), or "NTR-H" and the header's checksum for homebrew.
    /// </summary>
    public string TitleId => HasGameCode ? "NTR-" + GameCode : $"NTR-H{HeaderCrc:X8}";
}

/// <summary>Reads a .nds file offline: the header, and the 32x32 icon in its banner.</summary>
public static class NdsRom
{
    public const int HeaderSize = 0x200;

    public static NdsHeader? ReadHeader(string path)
    {
        using var f = File.OpenRead(path);
        if (f.Length < HeaderSize)
        {
            return null;
        }
        var h = new byte[HeaderSize];
        f.ReadExactly(h);
        return ParseHeader(h);
    }

    public static NdsHeader? ParseHeader(ReadOnlySpan<byte> h)
    {
        if (h.Length < 0x160)
        {
            return null;
        }
        // The header checksum (CRC-16 over 0x000-0x15D, at 0x15E) tells a DS ROM from anything else.
        if (Crc16(h[..0x15E]) != BinaryPrimitives.ReadUInt16LittleEndian(h[0x15E..]))
        {
            return null;
        }
        static string Ascii(ReadOnlySpan<byte> b) => new(Encoding.ASCII.GetString(b).TakeWhile(c => c != '\0').Where(c => c >= ' ' && c < 127).ToArray());
        return new NdsHeader(Ascii(h[..12]).Trim(), Ascii(h.Slice(0x0C, 4)), Ascii(h.Slice(0x10, 2)),
            BinaryPrimitives.ReadUInt32LittleEndian(h[0x68..]), Crc32(h[..0x160]));
    }

    /// <summary>The usual CRC-32 (zlib, PNG).</summary>
    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }
        return ~crc;
    }

    /// <summary>The CRC-16 (MODBUS) the DS uses for its header.</summary>
    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
        }
        return crc;
    }

    /// <summary>The banner's 32x32 icon as RGBA pixels (palette entry 0 is see-through), or null.</summary>
    public static byte[]? ReadIcon(string path, NdsHeader header)
    {
        using var f = File.OpenRead(path);
        if (header.BannerOffset == 0 || header.BannerOffset + 0x240 > f.Length)
        {
            return null;
        }
        var banner = new byte[0x240];
        f.Position = header.BannerOffset;
        f.ReadExactly(banner);
        return DecodeIcon(banner);
    }

    /// <summary>The icon is 4 bits per pixel in 8x8 tiles at 0x20, with 16 BGR555 colours at 0x220.</summary>
    public static byte[] DecodeIcon(ReadOnlySpan<byte> banner)
    {
        var palette = new byte[16][];
        for (var i = 0; i < 16; i++)
        {
            var c = BinaryPrimitives.ReadUInt16LittleEndian(banner[(0x220 + i * 2)..]);
            int r = c & 31, g = (c >> 5) & 31, b = (c >> 10) & 31;
            palette[i] = [(byte)(r << 3 | r >> 2), (byte)(g << 3 | g >> 2), (byte)(b << 3 | b >> 2), (byte)(i == 0 ? 0 : 255)];
        }
        var rgba = new byte[32 * 32 * 4];
        for (var tile = 0; tile < 16; tile++)
        {
            for (var p = 0; p < 64; p++)
            {
                var v = banner[0x20 + tile * 32 + p / 2];
                var index = (p & 1) == 0 ? v & 0xF : v >> 4;
                var x = (tile % 4) * 8 + p % 8;
                var y = (tile / 4) * 8 + p / 8;
                palette[index].CopyTo(rgba, (y * 32 + x) * 4);
            }
        }
        return rgba;
    }

    /// <summary>A plain RGBA PNG (no filtering), enough for small icons.</summary>
    public static byte[] Png(byte[] rgba, int width, int height)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10]);
        void Chunk(string type, byte[] data)
        {
            Span<byte> len = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
            ms.Write(len);
            var t = Encoding.ASCII.GetBytes(type);
            ms.Write(t);
            ms.Write(data);
            BinaryPrimitives.WriteUInt32BigEndian(len, Crc32([.. t, .. data]));
            ms.Write(len);
        }
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // RGBA
        Chunk("IHDR", ihdr);
        using (var raw = new MemoryStream())
        {
            using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
            {
                for (var y = 0; y < height; y++)
                {
                    z.WriteByte(0);
                    z.Write(rgba, y * width * 4, width * 4);
                }
            }
            Chunk("IDAT", raw.ToArray());
        }
        Chunk("IEND", []);
        return ms.ToArray();
    }
}

/// <summary>Offline DS game names (and cheat-file names) by game code, from libretro-database's No-Intro list.</summary>
public static class DsNames
{
    public const string Credit = "Game names: No-Intro, via libretro-database (CC BY-SA 4.0).";

    private static readonly Lazy<Dictionary<string, string>> Table = new(() =>
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using var res = typeof(DsNames).Assembly.GetManifestResourceStream("ds-names.tsv.gz");
        if (res == null)
        {
            return map;
        }
        using var reader = new StreamReader(new GZipStream(res, CompressionMode.Decompress), Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            var tab = line.IndexOf('\t');
            if (tab > 1)
            {
                map[line[..tab]] = line[(tab + 1)..];
            }
        }
        return map;
    });

    public static string? Find(string gameCode) => Table.Value.GetValueOrDefault("s" + gameCode.ToUpperInvariant());

    /// <summary>The name of the game's file in libretro-database's DS cheat folder, or null.</summary>
    public static string? CheatFile(string gameCode) => Table.Value.GetValueOrDefault("h" + gameCode.ToUpperInvariant());
}
