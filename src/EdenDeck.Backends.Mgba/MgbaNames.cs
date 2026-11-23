using System.IO.Compression;
using System.Text;

namespace EdenDeck.Backends.Mgba;

/// <summary>
/// Offline game names for GBA, GB and GBC ROMs, so the library shows "Pokemon - Emerald Version"
/// instead of whatever the file is called. Names only (no ROM data or links), keyed by ROM CRC32
/// and GBA game code, built by tools/gbnames/gen.py from libretro-database's No-Intro metadata.
/// </summary>
public static class MgbaNames
{
    public const string Credit = "Game names: No-Intro, via libretro-database (CC BY-SA 4.0).";

    private static readonly Lazy<Dictionary<string, string>> Table = new(Load);

    private static Dictionary<string, string> Load()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using var res = typeof(MgbaNames).Assembly.GetManifestResourceStream("gb-names.tsv.gz");
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

    /// <summary>The No-Intro name (with its region tags) for this ROM, or null when it isn't listed.</summary>
    public static string? Find(uint? crc, string? gameCode)
    {
        if (crc is { } c && Table.Value.TryGetValue("c" + c.ToString("X8"), out var byCrc))
        {
            return byCrc;
        }
        return gameCode != null && Table.Value.TryGetValue("s" + gameCode, out var byCode) ? byCode : null;
    }
}
