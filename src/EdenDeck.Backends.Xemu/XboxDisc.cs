using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace EdenDeck.Backends.Xemu;

/// <summary>What's on an Xbox disc: the title id from its default.xbe ("4D530004"), its name and region flags.</summary>
public sealed record XboxDiscInfo(uint TitleId, string? Name, uint Regions)
{
    /// <summary>The 8-digit hex title id, as xemu and the Xbox show it ("4D530004").</summary>
    public string Hex => TitleId.ToString("X8");

    /// <summary>The serial printed on the disc ("MS-004"): the publisher's two letters and the game number.</summary>
    public string? Serial => XboxDisc.Serial(TitleId);
}

/// <summary>
/// Reads an Xbox game image offline: the XDVDFS file system (an "XISO"), either on its own or after
/// the video partition of a full Redump dump, then the certificate in its <c>default.xbe</c>, which
/// holds the title id and the game's name.
/// </summary>
public static class XboxDisc
{
    public const int Sector = 2048;
    public const string Magic = "MICROSOFT*XBOX*MEDIA";

    /// <summary>Where the game partition starts: an XISO, or a full dump (XGD1, XGD2, XGD3).</summary>
    public static readonly long[] PartitionOffsets = [0, 0x18300000, 0xFD90000, 0x2080000];

    /// <summary>The game partition's start, or null when the file isn't an Xbox image.</summary>
    public static long? FindPartition(Stream s)
    {
        var buf = new byte[20];
        foreach (var off in PartitionOffsets)
        {
            var at = off + 32L * Sector;
            if (at + 20 > s.Length)
            {
                continue;
            }
            s.Position = at;
            s.ReadExactly(buf);
            if (Encoding.ASCII.GetString(buf) == Magic)
            {
                return off;
            }
        }
        return null;
    }

    public static XboxDiscInfo? Read(string path)
    {
        using var s = File.OpenRead(path);
        if (FindPartition(s) is not { } part)
        {
            return null;
        }
        var head = new byte[28];
        s.Position = part + 32L * Sector;
        s.ReadExactly(head);
        var rootSector = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(20));
        var rootSize = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(24));
        if (FindFile(s, part, rootSector, rootSize, "default.xbe") is not { } xbe || xbe.Size < 0x200 || xbe.Size > 256 << 20)
        {
            return null;
        }
        // The headers and certificate sit in the first pages of the XBE.
        var data = new byte[Math.Min(xbe.Size, 64 << 10)];
        s.Position = part + (long)xbe.Sector * Sector;
        s.ReadExactly(data);
        return ParseXbe(data);
    }

    /// <summary>The title id, name and regions from an XBE's certificate, or null when it isn't an XBE.</summary>
    public static XboxDiscInfo? ParseXbe(ReadOnlySpan<byte> xbe)
    {
        if (xbe.Length < 0x120 || Encoding.ASCII.GetString(xbe[..4]) != "XBEH")
        {
            return null;
        }
        var baseAddr = BinaryPrimitives.ReadUInt32LittleEndian(xbe[0x104..]);
        var certAddr = BinaryPrimitives.ReadUInt32LittleEndian(xbe[0x118..]);
        var cert = (long)certAddr - baseAddr;
        if (cert < 0 || cert + 0xA4 > xbe.Length)
        {
            return null;
        }
        var c = xbe[(int)cert..];
        var titleId = BinaryPrimitives.ReadUInt32LittleEndian(c[0x08..]);
        var name = Encoding.Unicode.GetString(c.Slice(0x0C, 80)).Split('\0')[0].Trim();
        var regions = BinaryPrimitives.ReadUInt32LittleEndian(c[0xA0..]);
        return new XboxDiscInfo(titleId, name.Length > 0 ? name : null, regions);
    }

    /// <summary>"MS-004" for 0x4D530004, or null when the top bytes aren't letters or digits.</summary>
    public static string? Serial(uint titleId)
    {
        var a = (char)(titleId >> 24);
        var b = (char)((titleId >> 16) & 0xFF);
        return char.IsAsciiLetterOrDigit(a) && char.IsAsciiLetterOrDigit(b) ? $"{a}{b}-{titleId & 0xFFFF:D3}" : null;
    }

    private readonly record struct Entry(uint Sector, uint Size, bool IsDir);

    /// <summary>A file in the root directory (a binary tree of entries, compared case-insensitively).</summary>
    private static Entry? FindFile(Stream s, long part, uint dirSector, uint dirSize, string name)
    {
        if (dirSize == 0 || dirSize > 4 << 20)
        {
            return null;
        }
        var dir = new byte[dirSize];
        s.Position = part + (long)dirSector * Sector;
        if (s.Read(dir, 0, dir.Length) != dir.Length)
        {
            return null;
        }
        // Walk the tree; offsets are in 4-byte units from the directory's start.
        var stack = new Stack<int>();
        stack.Push(0);
        var seen = new HashSet<int>();
        while (stack.Count > 0)
        {
            var at = stack.Pop();
            if (at < 0 || at + 14 > dir.Length || !seen.Add(at))
            {
                continue;
            }
            var left = BinaryPrimitives.ReadUInt16LittleEndian(dir.AsSpan(at));
            var right = BinaryPrimitives.ReadUInt16LittleEndian(dir.AsSpan(at + 2));
            if (left == 0xFFFF && right == 0xFFFF)
            {
                continue;  // padding
            }
            var sector = BinaryPrimitives.ReadUInt32LittleEndian(dir.AsSpan(at + 4));
            var size = BinaryPrimitives.ReadUInt32LittleEndian(dir.AsSpan(at + 8));
            var attr = dir[at + 12];
            var len = dir[at + 13];
            if (at + 14 + len <= dir.Length &&
                Encoding.ASCII.GetString(dir, at + 14, len).Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return new Entry(sector, size, (attr & 0x10) != 0);
            }
            if (left != 0)
            {
                stack.Push(left * 4);
            }
            if (right != 0)
            {
                stack.Push(right * 4);
            }
        }
        return null;
    }
}

/// <summary>
/// Offline Xbox game names by disc serial, built by tools/xboxnames/gen.py from libretro-database's
/// Redump metadata.
/// </summary>
public static class XboxNames
{
    public const string Credit = "Game names: Redump, via libretro-database (CC BY-SA 4.0).";

    private static readonly Lazy<Dictionary<string, string>> Table = new(() =>
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using var res = typeof(XboxNames).Assembly.GetManifestResourceStream("xbox-names.tsv.gz");
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

    public static string? Find(string serial) => Table.Value.GetValueOrDefault("s" + serial.ToUpperInvariant());
}
