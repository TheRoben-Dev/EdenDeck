using System.Buffers.Binary;
using System.Text;
using EdenDeck.Backends.Pcsx2;

namespace EdenDeck.Backends.Duckstation;

/// <summary>What's on a PS1 disc: its serial ("SLUS-00067"), or an empty serial for a disc that boots PSX.EXE.</summary>
public sealed record Ps1DiscInfo(string Serial, string? Title = null);

/// <summary>
/// Reads a PS1 disc's identity offline, the way DuckStation does (System::GetGameDetailsFromImage):
/// SYSTEM.CNF's <c>BOOT = cdrom:\SLUS_000.67;1</c> names the executable, and its file name without
/// dots, with '_' as '-', upper case, is the serial (SLUS-00067). A disc with <c>BOOT2</c> is a PS2
/// disc. Works on .iso, .cue/.bin (raw mode 2 sectors) and PSP-style .pbp (its PARAM.SFO's DISC_ID).
/// A .chd can't be read without codecs .NET lacks; it's named by file name instead.
/// </summary>
public static class Ps1Disc
{
    /// <summary>The serial, or null when it isn't a PS1 disc (PS2 discs included) or can't be read.</summary>
    public static Ps1DiscInfo? Read(string path)
    {
        if (path.EndsWith(".pbp", StringComparison.OrdinalIgnoreCase))
        {
            return ReadPbp(path);
        }
        using var image = Ps2Disc.Open(path);
        return image == null ? null : Read(image);
    }

    public static Ps1DiscInfo? Read(IDiscImage image)
    {
        if (Ps2Disc.ReadFile(image, ["SYSTEM.CNF"], 64 << 10) is { } cnf)
        {
            var text = Encoding.ASCII.GetString(cnf);
            if (Ps2Disc.ParseSystemCnf(text).Boot != null)
            {
                return null;  // BOOT2: a PS2 disc
            }
            if (Boot(text) is { } boot)
            {
                return new Ps1DiscInfo(Serial(boot) ?? "");
            }
        }
        // The earliest discs have no SYSTEM.CNF and boot PSX.EXE (DuckStation names those by a hash).
        return Ps2Disc.ReadFile(image, ["PSX.EXE"], 8 << 20) is { Length: > 8 } exe && Encoding.ASCII.GetString(exe, 0, 8) == "PS-X EXE"
            ? new Ps1DiscInfo("")
            : null;
    }

    /// <summary>The BOOT line's value ("cdrom:\SLUS_000.67;1"), or null.</summary>
    public static string? Boot(string systemCnf)
    {
        foreach (var raw in systemCnf.Split('\n'))
        {
            var eq = raw.IndexOf('=');
            if (eq > 0 && raw[..eq].Trim().Equals("BOOT", StringComparison.OrdinalIgnoreCase))
            {
                var v = raw[(eq + 1)..].Trim().TrimEnd('\0').Trim();
                return v.Length > 0 ? v : null;
            }
        }
        return null;
    }

    /// <summary>"cdrom:\SLUS_000.67;1" -> "SLUS-00067" (DuckStation's rule), or null for PSX.EXE.</summary>
    public static string? Serial(string boot)
    {
        var name = boot;
        var slash = name.LastIndexOf('\\');
        name = slash >= 0 ? name[(slash + 1)..] : name[(name.LastIndexOf(':') + 1)..];
        var semi = name.LastIndexOf(';');
        if (semi >= 0)
        {
            name = name[..semi];
        }
        name = name.Trim().TrimStart('/', '\\');
        if (name.Length == 0 || name.Equals("PSX.EXE", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return new string(name.Where(c => c != '.').Select(c => c == '_' ? '-' : char.ToUpperInvariant(c)).ToArray());
    }

    /// <summary>A PSP-format PS1 eboot: "\0PBP", then PARAM.SFO's DISC_ID ("SLUS00067") and TITLE.</summary>
    public static Ps1DiscInfo? ReadPbp(string path)
    {
        using var s = File.OpenRead(path);
        var h = new byte[40];
        if (s.Read(h, 0, h.Length) != h.Length || h[0] != 0 || Encoding.ASCII.GetString(h, 1, 3) != "PBP")
        {
            return null;
        }
        var sfoAt = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(8));
        var sfoEnd = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(12));
        if (sfoEnd <= sfoAt || sfoEnd - sfoAt > 64 << 10 || sfoEnd > s.Length)
        {
            return null;
        }
        var sfo = new byte[sfoEnd - sfoAt];
        s.Position = sfoAt;
        s.ReadExactly(sfo);
        var values = ParseSfo(sfo);
        if (!values.TryGetValue("DISC_ID", out var id) || id.Length < 9)
        {
            return null;
        }
        var serial = id.Length == 9 && char.IsLetter(id[3]) && char.IsDigit(id[4]) ? $"{id[..4]}-{id[4..]}" : id;
        return new Ps1DiscInfo(serial.ToUpperInvariant(), values.GetValueOrDefault("TITLE"));
    }

    /// <summary>The text values of a PARAM.SFO.</summary>
    internal static Dictionary<string, string> ParseSfo(byte[] sfo)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (sfo.Length < 20 || Encoding.ASCII.GetString(sfo, 1, 3) != "PSF")
        {
            return result;
        }
        var keys = (int)BinaryPrimitives.ReadUInt32LittleEndian(sfo.AsSpan(8));
        var data = (int)BinaryPrimitives.ReadUInt32LittleEndian(sfo.AsSpan(12));
        var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(sfo.AsSpan(16));
        for (var i = 0; i < count && 20 + i * 16 + 16 <= sfo.Length; i++)
        {
            var e = sfo.AsSpan(20 + i * 16);
            var keyAt = keys + BinaryPrimitives.ReadUInt16LittleEndian(e);
            var fmt = BinaryPrimitives.ReadUInt16LittleEndian(e[2..]);
            var len = (int)BinaryPrimitives.ReadUInt32LittleEndian(e[4..]);
            var at = data + (int)BinaryPrimitives.ReadUInt32LittleEndian(e[12..]);
            if (keyAt >= sfo.Length || at < 0 || at + len > sfo.Length || fmt == 0x0404)
            {
                continue;
            }
            var keyEnd = Array.IndexOf(sfo, (byte)0, keyAt);
            var key = Encoding.ASCII.GetString(sfo, keyAt, (keyEnd < 0 ? sfo.Length : keyEnd) - keyAt);
            result[key] = Encoding.UTF8.GetString(sfo, at, len).TrimEnd('\0');
        }
        return result;
    }
}
