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

    public sealed record Header(Kind System, string Title, string? GameCode, string Id);

    // First bytes of the Nintendo logo every GB/GBC cartridge carries at 0x104.
    private static readonly byte[] GbLogo = [0xCE, 0xED, 0x66, 0x66, 0xCC, 0x0D, 0x00, 0x0B];

    public static Header? Read(string path)
    {
        byte[] head;
        try
        {
            using var f = File.OpenRead(path);
            head = new byte[0x150];
            var n = f.Read(head, 0, head.Length);
            if (n < 0xC0)
            {
                return null;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
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

    /// <summary>"Pokemon - Emerald Version (USA, Europe) [!].gba" -> "Pokemon - Emerald Version".</summary>
    public static string CleanTitle(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        name = Tags().Replace(name, " ");
        name = Regex.Replace(name, @"\s+", " ").Trim();
        // "Legend of Zelda, The - ..." -> "The Legend of Zelda - ..."
        var m = Regex.Match(name, @"^(.+?), (The|A|An)( - .*)?$");
        if (m.Success)
        {
            name = $"{m.Groups[2].Value} {m.Groups[1].Value}{m.Groups[3].Value}";
        }
        return name.Length > 0 ? name : Path.GetFileNameWithoutExtension(fileName);
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
