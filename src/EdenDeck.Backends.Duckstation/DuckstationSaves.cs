using System.IO.Compression;
using EdenDeck.Backends.Pcsx2;
using EdenDeck.Core.Library;
using EdenDeck.Core.Saves;

namespace EdenDeck.Backends.Duckstation;

/// <summary>
/// PS1 saves: this game's memory cards and save states. DuckStation gives each game its own cards
/// by default, named after the game's title (<c>memcards/&lt;Title&gt;_1.mcd</c>), or by serial or
/// file name when set to; a shared card (<c>shared_card_1.mcd</c>) holds every game's saves, as on
/// a real PS1. Save states are <c>savestates/&lt;SERIAL&gt;_&lt;slot&gt;.sav</c> and
/// <c>&lt;SERIAL&gt;_resume.sav</c>. Zips hold <c>memcards/…</c> and <c>savestates/…</c>.
/// </summary>
public sealed class DuckstationSaves(Func<DuckstationPaths> paths, string dataDir, Func<string, string?> saveTitleFor) : ISaveData
{
    /// <summary>What each slot's card is, from settings.ini's [MemoryCards] (slot 1 per game by title, slot 2 off by default).</summary>
    public IReadOnlyList<(int Slot, string Type, string? Path)> Slots()
    {
        var ini = Pcsx2Ini.Load(paths().ConfigFile);
        return
        [
            (1, ini.Get("MemoryCards", "Card1Type") ?? "PerGameTitle", ini.Get("MemoryCards", "Card1Path")),
            (2, ini.Get("MemoryCards", "Card2Type") ?? "None", ini.Get("MemoryCards", "Card2Path")),
        ];
    }

    /// <summary>True when a slot uses a card every game shares.</summary>
    public bool UsesSharedCard() => Slots().Any(s => s.Type == "Shared");

    internal static string Normalize(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private IEnumerable<(string Relative, string Path, bool Shared)> Cards(GameEntry game)
    {
        var dir = paths().MemoryCardsDir;
        if (!Directory.Exists(dir))
        {
            yield break;
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (slot, type, cardPath) in Slots())
        {
            switch (type)
            {
                case "Shared":
                    var shared = cardPath is { Length: > 0 } c ? (Path.IsPathRooted(c) ? c : Path.Combine(dir, c)) : Path.Combine(dir, $"shared_card_{slot}.mcd");
                    if (File.Exists(shared) && Path.GetDirectoryName(Path.GetFullPath(shared)) == Path.GetFullPath(dir) && names.Add(Path.GetFileName(shared)))
                    {
                        yield return ("memcards/" + Path.GetFileName(shared), shared, true);
                    }
                    break;
                case "PerGame" or "PerGameTitle" or "PerGameFileTitle":
                    foreach (var f in GameCards(game, dir, slot).Where(f => names.Add(Path.GetFileName(f))))
                    {
                        yield return ("memcards/" + Path.GetFileName(f), f, false);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// The game's own cards for a slot: <c>&lt;X&gt;_&lt;slot&gt;.mcd</c> where X is its serial, file name,
    /// or title (DuckStation's, or the game's name without its disc number), compared loosely since
    /// DuckStation's database spells some titles differently.
    /// </summary>
    private IEnumerable<string> GameCards(GameEntry game, string dir, int slot)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal) { Normalize(Path.GetFileNameWithoutExtension(game.Path)) };
        if (!string.IsNullOrEmpty(game.TitleId))
        {
            keys.Add(Normalize(game.TitleId));
            if (saveTitleFor(game.TitleId) is { } t)
            {
                keys.Add(Normalize(t));
            }
        }
        keys.Add(Normalize(game.Title));
        keys.Add(Normalize(System.Text.RegularExpressions.Regex.Replace(game.Title, @"\s*\(Disc \d+\)", "")));
        keys.Remove("");
        var suffix = $"_{slot}.mcd";
        return Directory.EnumerateFiles(dir, "*" + suffix)
            .Where(f => keys.Contains(Normalize(Path.GetFileName(f)[..^suffix.Length])))
            .Order(StringComparer.Ordinal);
    }

    private IEnumerable<(string Relative, string Path, bool Shared)> Items(GameEntry game)
    {
        foreach (var c in Cards(game))
        {
            yield return c;
        }
        var states = paths().SaveStatesDir;
        if (!string.IsNullOrEmpty(game.TitleId) && Directory.Exists(states))
        {
            foreach (var f in Directory.EnumerateFiles(states, game.TitleId + "_*.sav").Order(StringComparer.Ordinal))
            {
                yield return ("savestates/" + Path.GetFileName(f), f, false);
            }
        }
    }

    public IReadOnlyList<SaveFolder> Saves(GameEntry game) => Items(game).Select(i =>
    {
        var f = new FileInfo(i.Path);
        return new SaveFolder(i.Relative, i.Path, f.Length, f.LastWriteTimeUtc);
    }).ToList();

    public string BackupDir(GameEntry game) => SaveZips.Dir(dataDir, Key(game));

    /// <summary>The serial, or for a disc without one "PS1-" and its file name.</summary>
    internal static string Key(GameEntry game) =>
        !string.IsNullOrEmpty(game.TitleId) ? game.TitleId : "PS1-" + DuckstationPaths.Sanitize(Path.GetFileNameWithoutExtension(game.Path));

    public IReadOnlyList<SaveBackup> Backups(GameEntry game) => SaveZips.List(BackupDir(game));

    public SaveBackup? Backup(GameEntry game, bool automatic = false) => Backup(game, automatic, null);

    public SaveBackup? Backup(GameEntry game, bool automatic, DateTime? now)
    {
        var files = Items(game).Select(i => (i.Relative, i.Path)).ToList();
        return files.Count == 0 ? null : SaveZips.Write(BackupDir(game), files, automatic, now);
    }

    /// <summary>memcards/&lt;card&gt;.mcd or savestates/&lt;SERIAL&gt;_….sav, with no folders or "..".</summary>
    private static bool Allowed(string entry, string serial)
    {
        var parts = entry.Replace('\\', '/').Split('/');
        if (parts.Length != 2 || parts[1] is "" or "." or "..")
        {
            return false;
        }
        return (parts[0] == "memcards" && parts[1].EndsWith(".mcd", StringComparison.OrdinalIgnoreCase)) ||
               (parts[0] == "savestates" && serial.Length > 0 && parts[1].StartsWith(serial + "_", StringComparison.Ordinal) && parts[1].EndsWith(".sav", StringComparison.Ordinal));
    }

    public SaveBackup? Restore(GameEntry game, string backupZip)
    {
        using var zip = ZipFile.OpenRead(backupZip);
        var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        if (entries.Count == 0)
        {
            throw new InvalidDataException("The backup is empty.");
        }
        if (entries.FirstOrDefault(e => !Allowed(e.FullName, game.TitleId)) is { } bad)
        {
            throw new InvalidDataException($"\"{Path.GetFileName(backupZip)}\" isn't a save backup of this game ({bad.FullName}).");
        }
        var safety = Backup(game);
        var p = paths();
        // Put back what the backup has; states it doesn't have are removed, cards it doesn't have stay.
        foreach (var (_, path, _) in Items(game).Where(i => i.Relative.StartsWith("savestates/", StringComparison.Ordinal)).ToList())
        {
            File.Delete(path);
        }
        foreach (var e in entries)
        {
            var parts = e.FullName.Split('/');
            var root = parts[0] == "memcards" ? p.MemoryCardsDir : p.SaveStatesDir;
            Directory.CreateDirectory(root);
            e.ExtractToFile(Path.Combine(root, parts[1]), overwrite: true);
        }
        return safety;
    }

    /// <summary>Backs up, then deletes this game's own cards and save states. A shared card stays.</summary>
    public SaveBackup? Delete(GameEntry game)
    {
        var backup = Backup(game);
        foreach (var (_, path, _) in Items(game).Where(i => !i.Shared).ToList())
        {
            File.Delete(path);
        }
        return backup;
    }
}
