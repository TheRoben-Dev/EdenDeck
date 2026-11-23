using System.Text;

namespace EdenDeck.Backends.Rpcs3;

/// <summary>What a PS3 game's PARAM.SFO says about it.</summary>
public sealed record Ps3Game(string TitleId, string Title, string Category, string? Version);

/// <summary>
/// Reads PS3 games: PARAM.SFO (the PSF key/value file every game has), from a disc folder
/// (<c>PS3_GAME/PARAM.SFO</c>), an installed or PSN folder (<c>PARAM.SFO</c> next to USRDIR), or
/// straight out of a disc .iso (plain ISO 9660, no extraction).
/// </summary>
public static class Ps3Sfo
{
    /// <summary>Disc game, HDD (PSN) game. Patches (GD), save data and DLC aren't games.</summary>
    public static bool IsGame(string category) => category is "DG" or "HG";

    public static Ps3Game? Parse(byte[] data)
    {
        if (data.Length < 20 || data[0] != 0 || data[1] != (byte)'P' || data[2] != (byte)'S' || data[3] != (byte)'F')
        {
            return null;
        }
        var keyTable = BitConverter.ToInt32(data, 8);
        var dataTable = BitConverter.ToInt32(data, 12);
        var count = BitConverter.ToInt32(data, 16);
        if (count < 0 || count > 1024 || 20 + count * 16 > data.Length || keyTable >= data.Length || dataTable > data.Length)
        {
            return null;
        }
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var e = 20 + i * 16;
            var keyOff = BitConverter.ToUInt16(data, e);
            var fmt = BitConverter.ToUInt16(data, e + 2);
            var len = BitConverter.ToInt32(data, e + 4);
            var off = BitConverter.ToInt32(data, e + 12);
            var k = keyTable + keyOff;
            var d = dataTable + off;
            if (k >= data.Length || d < 0 || len < 0 || d + len > data.Length)
            {
                continue;
            }
            var keyEnd = Array.IndexOf(data, (byte)0, k);
            var key = Encoding.ASCII.GetString(data, k, (keyEnd < 0 ? data.Length : keyEnd) - k);
            if (fmt is 0x0204 or 0x0004)  // UTF-8 (null-terminated or not)
            {
                values[key] = Encoding.UTF8.GetString(data, d, len).TrimEnd('\0').Trim();
            }
        }
        if (!values.TryGetValue("TITLE_ID", out var id) || id.Length == 0)
        {
            return null;
        }
        var title = values.GetValueOrDefault("TITLE") is { Length: > 0 } t ? t.Replace('\n', ' ') : id;
        return new Ps3Game(id, title, values.GetValueOrDefault("CATEGORY") ?? "", values.GetValueOrDefault("APP_VER") ?? values.GetValueOrDefault("VERSION"));
    }

    public static Ps3Game? ReadFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Length is > 0 and < 1 << 20 ? Parse(File.ReadAllBytes(path)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>PS3_GAME/PARAM.SFO inside a disc image, or null when it isn't a readable PS3 disc.</summary>
    public static Ps3Game? ReadIso(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            return Iso9660.ReadFile(f, ["PS3_GAME", "PARAM.SFO"]) is { } data ? Parse(data) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            return null;
        }
    }
}

/// <summary>Just enough ISO 9660 to find one small file by path.</summary>
public static class Iso9660
{
    private const int Sector = 2048;

    public static byte[]? ReadFile(Stream iso, IReadOnlyList<string> path)
    {
        var pvd = ReadAt(iso, 16L * Sector, Sector);
        if (pvd == null || pvd[0] != 1 || Encoding.ASCII.GetString(pvd, 1, 5) != "CD001")
        {
            return null;
        }
        // Root directory record at offset 156 of the primary volume descriptor.
        var (extent, size) = (BitConverter.ToUInt32(pvd, 156 + 2), BitConverter.ToUInt32(pvd, 156 + 10));
        for (var i = 0; i < path.Count; i++)
        {
            var found = Find(iso, extent, size, path[i]);
            if (found == null)
            {
                return null;
            }
            (extent, size) = (found.Value.Extent, found.Value.Size);
            if (i == path.Count - 1)
            {
                return found.Value.IsDir || size > 1 << 20 ? null : ReadAt(iso, (long)extent * Sector, (int)size);
            }
            if (!found.Value.IsDir)
            {
                return null;
            }
        }
        return null;
    }

    private static (uint Extent, uint Size, bool IsDir)? Find(Stream iso, uint extent, uint size, string name)
    {
        if (size > 4 << 20)
        {
            return null;
        }
        var dir = ReadAt(iso, (long)extent * Sector, (int)size);
        if (dir == null)
        {
            return null;
        }
        var pos = 0;
        while (pos < dir.Length)
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

    private static byte[]? ReadAt(Stream s, long offset, int count)
    {
        if (offset < 0 || offset + count > s.Length)
        {
            return null;
        }
        s.Position = offset;
        var buf = new byte[count];
        s.ReadExactly(buf);
        return buf;
    }
}
