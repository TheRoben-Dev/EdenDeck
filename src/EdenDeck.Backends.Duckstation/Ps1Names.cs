using System.IO.Compression;
using System.Text;

namespace EdenDeck.Backends.Duckstation;

/// <summary>
/// Offline PS1 game names by disc serial, and Redump names to serials (for .chd files, whose
/// contents can't be read without extra codecs, but are usually named after their Redump entry).
/// Names only, built by tools/ps1names/gen.py from libretro-database's Redump metadata.
/// </summary>
public static class Ps1Names
{
    public const string Credit = "Game names: Redump, via libretro-database (CC BY-SA 4.0).";

    private static readonly Lazy<Dictionary<string, string>> Table = new(Load);

    private static Dictionary<string, string> Load()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using var res = typeof(Ps1Names).Assembly.GetManifestResourceStream("ps1-names.tsv.gz");
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

    /// <summary>The serial of the Redump entry with exactly this name ("Final Fantasy VII (USA) (Disc 1)"), or null.</summary>
    public static string? SerialForName(string name) => Table.Value.GetValueOrDefault("n" + name.Trim().ToLowerInvariant());
}
