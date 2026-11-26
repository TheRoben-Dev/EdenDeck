using System.Text.Json;
using System.Text.RegularExpressions;
using EdenDeck.Backends.Pcsx2;

namespace EdenDeck.Backends.Duckstation;

/// <summary>
/// What Scanline knows about each PS1 disc file: its serial, read from the disc (.iso, .cue, .pbp),
/// from a .chd's file name when that's a Redump name, or learned from DuckStation's own log the
/// first time the game runs. Kept in <c>&lt;data&gt;/duckstation/discs.json</c> by path, and re-read
/// when the file's size or date changes.
/// </summary>
public sealed partial class DuckstationDiscs(string file)
{
    /// <param name="Serial">Null when the file isn't (known to be) a PS1 disc; empty for a PS1 disc without a serial.</param>
    /// <param name="SaveTitle">The title DuckStation named the game in its log (its memory cards are named after it).</param>
    public sealed record Entry(string? Serial, string? Title, long Size, long Mtime, string Source, string? SaveTitle = null);

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

    /// <summary>The disc's identity (from the cache when the file hasn't changed).</summary>
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
            var serial = chd is { Dvd: false } ? Ps1Names.SerialForName(Path.GetFileNameWithoutExtension(path)) : null;
            entry = new Entry(serial, null, size, mtime, chd == null ? "unreadable" : chd.Dvd ? "chd-dvd" : serial != null ? "chd-name" : "chd");
        }
        else
        {
            var disc = Ps1Disc.Read(path);
            entry = new Entry(disc?.Serial, disc?.Title, size, mtime, disc == null ? "not-ps1" : "disc");
        }
        lock (_lock)
        {
            // A serial DuckStation reported for this file before stays known.
            if (Entries.TryGetValue(path, out var old) && entry.Serial == null && old.Source.EndsWith("+log", StringComparison.Ordinal))
            {
                entry = entry with { Serial = old.Serial, SaveTitle = old.SaveTitle, Source = entry.Source + "+log" };
            }
            Entries[path] = entry;
            Save();
        }
        return entry;
    }

    /// <summary>The title DuckStation used for this serial, if it has run the game.</summary>
    public string? SaveTitleFor(string serial)
    {
        lock (_lock)
        {
            return Entries.Values.Where(e => e.Serial == serial && e.SaveTitle != null).Select(e => e.SaveTitle).FirstOrDefault();
        }
    }

    /// <summary>Records what DuckStation said the disc at <paramref name="path"/> is. True when something new was learned.</summary>
    public bool Learn(string path, string serial, string title)
    {
        if (!File.Exists(path))
        {
            return false;
        }
        var info = new FileInfo(path);
        lock (_lock)
        {
            Entries.TryGetValue(path, out var old);
            if (old != null && old.Serial == serial && old.SaveTitle == title)
            {
                return false;
            }
            // Keep a serial read from the disc: game ids are built from it.
            var source = old?.Source ?? "";
            Entries[path] = new Entry(string.IsNullOrEmpty(old?.Serial) ? serial : old.Serial, old?.Title, info.Length, info.LastWriteTimeUtc.Ticks,
                source.EndsWith("+log", StringComparison.Ordinal) ? source : source + "+log", title);
            Save();
            return true;
        }
    }

    /// <summary>The title and serial of the last game DuckStation's log says it started ("Game: … | SLUS-00067 | HASH-…").</summary>
    public static (string Title, string Serial)? ParseLog(string text)
    {
        (string, string)? found = null;
        foreach (Match m in GameLine().Matches(text))
        {
            var serial = m.Groups[2].Value.Trim();
            if (serial.Length > 0 && !serial.StartsWith("HASH-", StringComparison.Ordinal))
            {
                found = (m.Groups[1].Value.Trim(), serial);
            }
        }
        return found;
    }

    [GeneratedRegex(@"Game: ([^\n|]*)\|([^\n|]*)\| HASH-[0-9A-Fa-f]+")]
    private static partial Regex GameLine();
}
