using System.Text.Json;
using System.Text.RegularExpressions;

namespace EdenDeck.Backends.Pcsx2;

/// <summary>
/// What Scanline knows about each PS2 disc file: its serial and boot ELF CRC, read from the disc
/// (.iso, .cue, .cso, .zso), from a DVD .chd's header hash, or learned from PCSX2's own log the
/// first time the game runs (a .chd's CRC can't be read offline). Kept in
/// <c>&lt;data&gt;/pcsx2/discs.json</c> by path, and re-read when the file's size or date changes.
/// </summary>
public sealed partial class Pcsx2Discs(string file)
{
    public sealed record Entry(string? Serial, uint? Crc, string? Version, long Size, long Mtime, string Source);

    private readonly object _lock = new();
    private Dictionary<string, Entry>? _entries;

    private Dictionary<string, Entry> Entries
    {
        get
        {
            if (_entries == null)
            {
                try
                {
                    _entries = File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(file)) : null;
                }
                catch (JsonException)
                {
                }
                _entries ??= new Dictionary<string, Entry>(StringComparer.Ordinal);
            }
            return _entries;
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Entries));
        File.Move(tmp, file, overwrite: true);
    }

    /// <summary>The disc's identity (from the cache when the file hasn't changed). Serial is null when unknown.</summary>
    public Entry Identify(string path)
    {
        var info = new FileInfo(path);
        var (size, mtime) = (info.Length, info.LastWriteTimeUtc.Ticks);
        lock (_lock)
        {
            if (Entries.TryGetValue(path, out var known) && known.Size == size && known.Mtime == mtime)
            {
                return known;
            }
        }
        Entry entry;
        if (path.EndsWith(".chd", StringComparison.OrdinalIgnoreCase))
        {
            var chd = ChdInfo.Read(path);
            entry = new Entry(chd == null ? null : Ps2Names.SerialForSha1(chd.RawSha1), null, null, size, mtime, chd is { Dvd: true } ? "chd-dvd" : "chd");
        }
        else
        {
            var disc = Ps2Disc.Read(path);
            entry = new Entry(disc?.Serial, disc?.Crc, disc?.Version, size, mtime, disc == null ? "unreadable" : "system-cnf");
        }
        lock (_lock)
        {
            Entries[path] = entry;
            Save();
        }
        return entry;
    }

    /// <summary>The boot ELF CRC known for a serial (from any file of it), or null.</summary>
    public uint? CrcFor(string serial)
    {
        lock (_lock)
        {
            return Entries.Values.Where(e => e.Serial == serial && e.Crc != null).Select(e => e.Crc).FirstOrDefault();
        }
    }

    /// <summary>Records what PCSX2 said the disc at <paramref name="path"/> is. True when something new was learned.</summary>
    public bool Learn(string path, string serial, uint crc)
    {
        if (!File.Exists(path))
        {
            return false;
        }
        var info = new FileInfo(path);
        lock (_lock)
        {
            Entries.TryGetValue(path, out var old);
            if (old != null && old.Serial == serial && old.Crc == crc)
            {
                return false;
            }
            // Keep a serial read from the disc: GameIds are built from it.
            Entries[path] = new Entry(old?.Serial ?? serial, crc, old?.Version, info.Length, info.LastWriteTimeUtc.Ticks, (old?.Source ?? "") + "+pcsx2-log");
            Save();
            return true;
        }
    }

    /// <summary>The serial and CRC of the last disc PCSX2's log says it started ("Disc changed to …").</summary>
    public static (string Serial, uint Crc)? ParseLog(string text)
    {
        (string, uint)? found = null;
        foreach (Match m in DiscBlock().Matches(text))
        {
            if (m.Groups[1].Value.Length > 0 && uint.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.HexNumber, null, out var crc) && crc != 0)
            {
                found = (m.Groups[1].Value, crc);
            }
        }
        return found;
    }

    [GeneratedRegex(@"Serial: ([A-Z0-9-]*)[^\n]*\n(?:[^\n]*\n)?[^\n]*CRC: ([0-9A-Fa-f]{8})")]
    private static partial Regex DiscBlock();
}
