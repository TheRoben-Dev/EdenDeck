using System.IO.Compression;
using EdenDeck.Core.Library;
using EdenDeck.Core.Saves;

namespace EdenDeck.Backends.Rpcs3;

/// <summary>
/// PS3 saves: one folder per save in <c>dev_hdd0/home/&lt;user&gt;/savedata/</c>, named after the
/// game's title id (<c>BLUS30443-SAVE00</c>, <c>BLUS30443AUTO</c>, …), for every RPCS3 user. A
/// backup zips them as <c>home/&lt;user&gt;/savedata/&lt;folder&gt;/…</c> (relative to dev_hdd0).
/// </summary>
public sealed class Rpcs3Saves(Func<Rpcs3Paths> paths, string dataDir) : ISaveData
{
    private IEnumerable<string> Folders(GameEntry game)
    {
        var home = Path.Combine(paths().DevHdd0, "home");
        if (string.IsNullOrEmpty(game.TitleId) || !Directory.Exists(home))
        {
            yield break;
        }
        foreach (var user in Directory.EnumerateDirectories(home).Order(StringComparer.Ordinal))
        {
            var saves = Path.Combine(user, "savedata");
            if (!Directory.Exists(saves))
            {
                continue;
            }
            foreach (var d in Directory.EnumerateDirectories(saves).Order(StringComparer.Ordinal))
            {
                if (Path.GetFileName(d).StartsWith(game.TitleId, StringComparison.OrdinalIgnoreCase) && Directory.EnumerateFileSystemEntries(d).Any())
                {
                    yield return d;
                }
            }
        }
    }

    public IReadOnlyList<SaveFolder> Saves(GameEntry game)
    {
        var root = paths().DevHdd0;
        return Folders(game).Select(d =>
        {
            var files = Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).Select(f => new FileInfo(f)).ToList();
            return new SaveFolder(Path.GetRelativePath(root, d), d, files.Sum(f => f.Length),
                files.Count == 0 ? Directory.GetLastWriteTimeUtc(d) : files.Max(f => f.LastWriteTimeUtc));
        }).ToList();
    }

    public string BackupDir(GameEntry game) => SaveZips.Dir(dataDir, game.TitleId);

    public IReadOnlyList<SaveBackup> Backups(GameEntry game) => SaveZips.List(BackupDir(game));

    public SaveBackup? Backup(GameEntry game, bool automatic = false) => Backup(game, automatic, null);

    public SaveBackup? Backup(GameEntry game, bool automatic, DateTime? now)
    {
        var root = paths().DevHdd0;
        var files = Folders(game)
            .SelectMany(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
            .Select(f => (Path.GetRelativePath(root, f).Replace('\\', '/'), f))
            .ToList();
        return files.Count == 0 ? null : SaveZips.Write(BackupDir(game), files, automatic, now);
    }

    /// <summary>home/&lt;user&gt;/savedata/&lt;TITLEID…&gt;/file, with no "..".</summary>
    private static bool Allowed(string entry, string titleId)
    {
        var parts = entry.Replace('\\', '/').Split('/');
        return parts.Length >= 5 && parts[0] == "home" && parts[2] == "savedata" && !parts.Contains("..") && !parts.Contains("") &&
               parts[3].StartsWith(titleId, StringComparison.OrdinalIgnoreCase);
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
        foreach (var d in Folders(game).ToList())
        {
            Directory.Delete(d, recursive: true);
        }
        var root = Path.GetFullPath(paths().DevHdd0);
        foreach (var e in entries)
        {
            var target = Path.GetFullPath(Path.Combine(root, e.FullName));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unsafe path in backup: {e.FullName}");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            e.ExtractToFile(target, overwrite: true);
        }
        return safety;
    }

    public SaveBackup? Delete(GameEntry game)
    {
        var backup = Backup(game);
        foreach (var d in Folders(game).ToList())
        {
            Directory.Delete(d, recursive: true);
        }
        return backup;
    }
}
