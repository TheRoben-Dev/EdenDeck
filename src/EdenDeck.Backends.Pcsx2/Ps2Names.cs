using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace EdenDeck.Backends.Pcsx2;

/// <summary>
/// Offline PS2 game names by disc serial, and DVD image hashes to serials (for .chd files, whose
/// contents can't be read without extra codecs). Names only (no disc data or links), built by
/// tools/ps2names/gen.py from libretro-database's Redump metadata.
/// </summary>
public static partial class Ps2Names
{
    public const string Credit = "Game names: Redump, via libretro-database (CC BY-SA 4.0).";

    private static readonly Lazy<Dictionary<string, string>> Table = new(Load);

    private static Dictionary<string, string> Load()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using var res = typeof(Ps2Names).Assembly.GetManifestResourceStream("ps2-names.tsv.gz");
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
    }

    /// <summary>The Redump name (with region tags) for a serial, or null.</summary>
    public static string? Find(string serial) => Table.Value.GetValueOrDefault("s" + serial.ToUpperInvariant());

    /// <summary>The serial of the Redump DVD image with this SHA-1 (hex), or null.</summary>
    public static string? SerialForSha1(string sha1) =>
        sha1.Length >= 16 ? Table.Value.GetValueOrDefault("h" + sha1[..16].ToLowerInvariant()) : null;

    /// <summary>
    /// A readable title from a Redump name or a file name: region, language and version tags go,
    /// "(Disc 2)" stays. "Grand Theft Auto - San Andreas (USA) (v1.03)" -> "Grand Theft Auto - San Andreas".
    /// </summary>
    public static string CleanTitle(string name)
    {
        var disc = DiscTag().Match(name);
        var t = Tags().Replace(name, " ").Replace('_', ' ');
        t = Regex.Replace(t, @"\s+", " ").Trim(' ', '-');
        var m = Regex.Match(t, @"^(.+?), (The|A|An)( - .*)?$");
        if (m.Success)
        {
            t = $"{m.Groups[2].Value} {m.Groups[1].Value}{m.Groups[3].Value}";
        }
        if (t.Any(char.IsLetter) && (!t.Any(char.IsUpper) || !t.Any(char.IsLower)))
        {
            t = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(t.ToLowerInvariant());
        }
        if (t.Length == 0)
        {
            return name;
        }
        return disc.Success ? $"{t} (Disc {disc.Groups[1].Value})" : t;
    }

    [GeneratedRegex(@"\s*[\(\[][^\)\]]*[\)\]]")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\(Disc (\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex DiscTag();
}
