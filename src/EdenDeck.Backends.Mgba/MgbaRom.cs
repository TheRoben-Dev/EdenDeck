using System.Text;
using System.Text.RegularExpressions;

namespace EdenDeck.Backends.Mgba;

/// <summary>
/// Reads the cartridge header of a GBA, GB or GBC ROM. GBA: title at 0xA0 (12 bytes), game code
/// at 0xAC (4), maker at 0xB0 (2), fixed value 0x96 at 0xB2, header checksum at 0xBD. GB/GBC:
/// Nintendo logo at 0x104, title at 0x134, CGB flag at 0x143, header checksum at 0x14D.
/// </summary>
public static partial class MgbaRom
{
    public enum Kind
    {
        Gba,
        Gb,
        Gbc,
    }

    public sealed record Header(Kind System, string Title, string? GameCode, string Id)
    {
        /// <summary>CRC32 of the whole ROM, when it was cheap to get (from a .zip, or a small ROM).</summary>
        public uint? Crc { get; init; }
    }

    // First bytes of the Nintendo logo every GB/GBC cartridge carries at 0x104.
    private static readonly byte[] GbLogo = [0xCE, 0xED, 0x66, 0x66, 0xCC, 0x0D, 0x00, 0x0B];

    /// <summary>ROM file extensions mGBA runs straight from a .zip.</summary>
    public static readonly string[] RomExtensions = [".gba", ".gb", ".gbc"];

    public static Header? Read(string path)
    {
        try
        {
            if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return ReadZip(path);
            }
            using var f = File.OpenRead(path);
            var header = Read(f, path);
            // The name list knows GBA games by game code; others (and GBA homebrew) need the CRC,
            // which is quick for Game Boy sized ROMs.
            if (header != null && (header.GameCode == null || header.System != Kind.Gba) && f.Length <= MaxCrcBytes)
            {
                f.Position = 0;
                header = header with { Crc = Crc32(f) };
            }
            return header;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// The ROM inside a .zip: the first .gba/.gb/.gbc entry that is a cartridge (mGBA also takes the
    /// first cartridge it finds). mGBA opens the archive itself and names saves after the .zip, so
    /// nothing is extracted.
    /// </summary>
    private static Header? ReadZip(string path)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
        var entries = zip.Entries.Where(e => e.Length >= 0xC0 && RomExtensions.Contains(Path.GetExtension(e.Name).ToLowerInvariant()));
        foreach (var entry in entries)
        {
            using var s = entry.Open();
            if (Read(s, entry.Name) is { } header)
            {
                return header with { Crc = entry.Crc32 };
            }
        }
        return null;
    }

    private const long MaxCrcBytes = 32L << 20;

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++)
        {
            c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return c;
    }).ToArray();

    internal static uint Crc32(Stream s)
    {
        var crc = 0xFFFFFFFFu;
        var buf = new byte[1 << 16];
        int n;
        while ((n = s.Read(buf, 0, buf.Length)) > 0)
        {
            for (var i = 0; i < n; i++)
            {
                crc = CrcTable[(crc ^ buf[i]) & 0xFF] ^ (crc >> 8);
            }
        }
        return ~crc;
    }

    private static Header? Read(Stream stream, string path)
    {
        var head = new byte[0x150];
        var n = 0;
        int got;
        while (n < head.Length && (got = stream.Read(head, n, head.Length - n)) > 0)
        {
            n += got;
        }
        if (n < 0xC0)
        {
            return null;
        }
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".gba" || head[0xB2] == 0x96 && ext is not (".gb" or ".gbc"))
        {
            var title = Ascii(head, 0xA0, 12);
            var code = Ascii(head, 0xAC, 4);
            var validCode = code.Length == 4 && code.All(char.IsAsciiLetterOrDigit);
            // Code plus header checksum: the same game in another language or revision gets its own id.
            var id = (validCode ? code : "GBA_" + Sanitize(title)) + "-" + head[0xBD].ToString("X2");
            return new Header(Kind.Gba, title, validCode ? code : null, id);
        }
        if (head.Length >= 0x150 && head.AsSpan(0x104, GbLogo.Length).SequenceEqual(GbLogo))
        {
            var cgb = head[0x143] is 0x80 or 0xC0;
            var title = Ascii(head, 0x134, cgb ? 11 : 16);
            var id = (cgb ? "GBC_" : "GB_") + Sanitize(title) + "-" + head[0x14D].ToString("X2");
            return new Header(cgb ? Kind.Gbc : Kind.Gb, title, null, id);
        }
        return null;
    }

    /// <summary>
    /// A readable title from a No-Intro name or a file name, offline:
    /// "Pokemon - Emerald Version (USA, Europe) [!].gba" -> "Pokemon - Emerald Version",
    /// "1986_-_pokemon_emerald_(U)(TrashMan).gba" -> "Pokemon Emerald".
    /// </summary>
    public static string CleanTitle(string fileName)
    {
        var raw = Path.GetFileNameWithoutExtension(fileName);
        var name = Tags().Replace(raw, " ");
        name = name.Replace('_', ' ');
        // "Golden.Sun.2" style names: dots stand in for spaces.
        if (!name.Contains(' ') && name.Count(c => c == '.') >= 1)
        {
            name = name.Replace('.', ' ');
        }
        name = Regex.Replace(name, @"\s+", " ").Trim();
        // Release-list numbering: "1986 - Pokemon Emerald" -> "Pokemon Emerald".
        name = Regex.Replace(name, @"^\d{3,4}\s*-\s+(?=\D)", "");
        name = name.Trim(' ', '-');
        // "Legend of Zelda, The - ..." -> "The Legend of Zelda - ..."
        var m = Regex.Match(name, @"^(.+?), (The|A|An)( - .*)?$");
        if (m.Success)
        {
            name = $"{m.Groups[2].Value} {m.Groups[1].Value}{m.Groups[3].Value}";
        }
        // All lower or all upper case: "pokemon emerald" -> "Pokemon Emerald".
        if (name.Any(char.IsLetter) && (!name.Any(char.IsUpper) || !name.Any(char.IsLower)))
        {
            name = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
        }
        return name.Length > 0 ? name : raw;
    }

    private static string Ascii(byte[] b, int start, int length)
    {
        var sb = new StringBuilder();
        for (var i = start; i < start + length && i < b.Length; i++)
        {
            if (b[i] == 0)
            {
                break;
            }
            sb.Append(b[i] is >= 0x20 and < 0x7F ? (char)b[i] : ' ');
        }
        return sb.ToString().Trim();
    }

    private static string Sanitize(string s)
    {
        var t = new string(s.Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_').ToArray()).Trim('_');
        return t.Length > 0 ? t : "UNKNOWN";
    }

    [GeneratedRegex(@"\s*[\(\[][^\)\]]*[\)\]]")]
    private static partial Regex Tags();
}
