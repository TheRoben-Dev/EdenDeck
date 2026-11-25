using System.IO.Compression;
using EdenDeck.Core.Library;
using EdenDeck.Core.Saves;

namespace EdenDeck.Backends.Pcsx2;

/// <summary>
/// PS2 saves: the memory cards in PCSX2's slots (<c>memcards/Mcd001.ps2</c> by default, a file or
/// a folder card) and this game's save states (<c>sstates/&lt;SERIAL&gt; (&lt;CRC&gt;).NN.p2s</c>).
/// A memory card holds every game's saves, as on a real PS2, so a backup or restore covers the
/// whole card. Zips hold <c>memcards/…</c> and <c>sstates/…</c>.
/// </summary>
public sealed class Pcsx2Saves(Func<Pcsx2Paths> paths, string dataDir) : ISaveData
{
    /// <summary>The enabled slots' card names ("Mcd001.ps2"), from PCSX2.ini's [MemoryCards].</summary>
    public IReadOnlyList<string> Cards()
    {
        var ini = Pcsx2Ini.Load(paths().ConfigFile);
        var cards = new List<string>();
        for (var slot = 1; slot <= 2; slot++)
        {
            if (ini.Get("MemoryCards", $"Slot{slot}_Enable") is { } on && !on.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var name = ini.Get("MemoryCards", $"Slot{slot}_Filename") is { Length: > 0 } n ? n : $"Mcd00{slot}.ps2";
            if (Path.GetFileName(name) == name && name != "." && name != "..")
            {
                cards.Add(name);
            }
        }
        return cards;
    }

    private IEnumerable<(string Relative, string Path)> Items(GameEntry game)
    {
        var p = paths();
        foreach (var card in Cards())
        {
            var path = Path.Combine(p.MemoryCardsDir, card);
            if (File.Exists(path) || Directory.Exists(path))
            {
                yield return ("memcards/" + card, path);
            }
        }
        if (!string.IsNullOrEmpty(game.TitleId) && Directory.Exists(p.SaveStatesDir))
        {
            foreach (var f in Directory.EnumerateFiles(p.SaveStatesDir, game.TitleId + " (*").Order(StringComparer.Ordinal))
            {
                yield return ("sstates/" + Path.GetFileName(f), f);
            }
        }
    }

    private static IEnumerable<string> FilesOf(string path) =>
        Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories) : [path];

    public IReadOnlyList<SaveFolder> Saves(GameEntry game) => Items(game).Select(i =>
    {
        var files = FilesOf(i.Path).Select(f => new FileInfo(f)).ToList();
        return new SaveFolder(i.Relative, i.Path, files.Sum(f => f.Length),
            files.Count == 0 ? Directory.GetLastWriteTimeUtc(i.Path) : files.Max(f => f.LastWriteTimeUtc));
    }).ToList();

    public string BackupDir(GameEntry game) => SaveZips.Dir(dataDir, game.TitleId);

    public IReadOnlyList<SaveBackup> Backups(GameEntry game) => SaveZips.List(BackupDir(game));

    public SaveBackup? Backup(GameEntry game, bool automatic = false) => Backup(game, automatic, null);

    public SaveBackup? Backup(GameEntry game, bool automatic, DateTime? now)
    {
        var files = Items(game)
            .SelectMany(i => FilesOf(i.Path).Select(f => (Directory.Exists(i.Path) ? i.Relative + "/" + Path.GetRelativePath(i.Path, f).Replace('\\', '/') : i.Relative, f)))
            .ToList();
        return files.Count == 0 ? null : SaveZips.Write(BackupDir(game), files, automatic, now);
    }

    /// <summary>memcards/&lt;card&gt;[/…] or sstates/&lt;SERIAL&gt; (…).p2s, with no "..".</summary>
    private static bool Allowed(string entry, string serial)
    {
        var parts = entry.Replace('\\', '/').Split('/');
        if (parts.Contains("..") || parts.Contains("") || parts.Contains("."))
        {
            return false;
        }
        return (parts[0] == "memcards" && parts.Length >= 2) ||
               (parts[0] == "sstates" && parts.Length == 2 && parts[1].StartsWith(serial + " (", StringComparison.Ordinal));
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
        foreach (var (_, path) in Items(game).Where(i => i.Relative.StartsWith("sstates/", StringComparison.Ordinal)).ToList())
        {
            File.Delete(path);
        }
        foreach (var card in entries.Where(e => e.FullName.StartsWith("memcards/", StringComparison.Ordinal))
                     .Select(e => e.FullName.Split('/')[1]).Distinct())
        {
            var dir = Path.Combine(p.MemoryCardsDir, card);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        foreach (var e in entries)
        {
            var parts = e.FullName.Split('/');
            var root = Path.GetFullPath(parts[0] == "memcards" ? p.MemoryCardsDir : p.SaveStatesDir);
            var target = Path.GetFullPath(Path.Combine(root, string.Join('/', parts[1..])));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unsafe path in backup: {e.FullName}");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            e.ExtractToFile(target, overwrite: true);
        }
        return safety;
    }

    /// <summary>Backs up, then deletes this game's save states. Memory cards are shared, so they stay.</summary>
    public SaveBackup? Delete(GameEntry game)
    {
        var backup = Backup(game);
        foreach (var (_, path) in Items(game).Where(i => i.Relative.StartsWith("sstates/", StringComparison.Ordinal)).ToList())
        {
            File.Delete(path);
        }
        return backup;
    }
}
