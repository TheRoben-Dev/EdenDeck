using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using EdenDeck.Core.Saves;

namespace EdenDeck.Backends.Eden.Saves;

/// <summary>
/// Save backups for one game. Eden keeps saves in its NAND (src/core/file_sys/savedata_factory.cpp):
/// <c>user/save/0000000000000000/&lt;profile&gt;/&lt;TITLEID&gt;</c> (and the newer
/// <c>user/save/account/&lt;uuid&gt;/&lt;TITLEID&gt;</c> and <c>user/save/device/&lt;TITLEID&gt;</c>). A
/// backup is a zip of every such folder for the game, paths kept relative to the NAND, stored in
/// Scanline's data dir as <c>saves/&lt;TITLEID&gt;/&lt;time&gt;[-auto].zip</c>. Restoring first backs up
/// what's there, so a restore can always be undone.
/// </summary>
public static class EdenSaves
{
    /// <summary>Automatic backups (before each play) kept per game; manual ones are never pruned.</summary>
    public const int KeepAutomatic = SaveZips.KeepAutomatic;

    private static readonly Regex Hex16 = new("^[0-9A-Fa-f]{16}$", RegexOptions.Compiled);

    public static IReadOnlyList<SaveFolder> Find(EdenPaths paths, string titleId)
    {
        var root = System.IO.Path.Combine(paths.NandDir, "user", "save");
        if (!Directory.Exists(root))
        {
            return [];
        }
        var result = new List<SaveFolder>();
        void Walk(string dir, int depth)
        {
            foreach (var d in Directory.EnumerateDirectories(dir))
            {
                var name = System.IO.Path.GetFileName(d);
                if (depth == 0 && name.Equals("cache", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                // A title id below the top level: the game's save folder (top-level 16-hex dirs are save ids, not games).
                if (depth > 0 && Hex16.IsMatch(name) && name.Equals(titleId, StringComparison.OrdinalIgnoreCase))
                {
                    if (Directory.EnumerateFileSystemEntries(d).Any())
                    {
                        result.Add(Describe(paths.NandDir, d));
                    }
                    continue;
                }
                if (depth < 3)
                {
                    Walk(d, depth + 1);
                }
            }
        }
        Walk(root, 0);
        return result.OrderBy(s => s.Relative, StringComparer.Ordinal).ToList();
    }

    private static SaveFolder Describe(string nand, string dir)
    {
        var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(f => new FileInfo(f)).ToList();
        var last = files.Count == 0 ? Directory.GetLastWriteTimeUtc(dir) : files.Max(f => f.LastWriteTimeUtc);
        return new SaveFolder(System.IO.Path.GetRelativePath(nand, dir).Replace('\\', '/'), dir, files.Sum(f => f.Length), last);
    }

    public static string BackupDir(string dataDir, string titleId) => System.IO.Path.Combine(dataDir, "saves", titleId.ToUpperInvariant());

    public static IReadOnlyList<SaveBackup> Backups(string dataDir, string titleId)
    {
        var dir = BackupDir(dataDir, titleId);
        if (!Directory.Exists(dir))
        {
            return [];
        }
        return Directory.EnumerateFiles(dir, "*.zip")
            .Select(f => new FileInfo(f))
            .Select(f => new SaveBackup(f.FullName, Created(f), f.Length, f.Name.EndsWith("-auto.zip", StringComparison.Ordinal)))
            .OrderByDescending(b => b.CreatedUtc)
            .ToList();
    }

    private static DateTime Created(FileInfo f) =>
        DateTime.TryParseExact(f.Name[..Math.Min(15, f.Name.Length)], "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : f.LastWriteTimeUtc;

    /// <summary>Zips the game's saves. Returns null when the game has no save yet.</summary>
    public static SaveBackup? Backup(EdenPaths paths, string dataDir, string titleId, bool automatic = false, DateTime? now = null)
    {
        var saves = Find(paths, titleId);
        if (saves.Count == 0)
        {
            return null;
        }
        var dir = BackupDir(dataDir, titleId);
        Directory.CreateDirectory(dir);
        var stamp = (now ?? DateTime.UtcNow).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var file = System.IO.Path.Combine(dir, stamp + (automatic ? "-auto" : "") + ".zip");
        for (var n = 2; File.Exists(file); n++)
        {
            file = System.IO.Path.Combine(dir, $"{stamp}-{n}{(automatic ? "-auto" : "")}.zip");
        }
        var tmp = file + ".tmp";
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            foreach (var s in saves)
            {
                foreach (var f in Directory.EnumerateFiles(s.Path, "*", SearchOption.AllDirectories))
                {
                    var rel = System.IO.Path.GetRelativePath(paths.NandDir, f).Replace('\\', '/');
                    zip.CreateEntryFromFile(f, rel, CompressionLevel.Optimal);
                }
            }
        }
        File.Move(tmp, file);
        if (automatic)
        {
            foreach (var old in Backups(dataDir, titleId).Where(b => b.Automatic).Skip(KeepAutomatic))
            {
                File.Delete(old.Path);
            }
        }
        return Backups(dataDir, titleId).First(b => b.Path == file);
    }

    /// <summary>
    /// Puts a backup back: the current saves are backed up first, then replaced by the backup's
    /// folders. Only entries under user/save for this game are written. Returns the safety backup
    /// (null if there was no save to keep).
    /// </summary>
    public static SaveBackup? Restore(EdenPaths paths, string dataDir, string titleId, string backupZip)
    {
        using var zip = ZipFile.OpenRead(backupZip);
        var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        foreach (var e in entries)
        {
            if (!IsSaveEntry(e.FullName, titleId))
            {
                throw new InvalidDataException($"\"{System.IO.Path.GetFileName(backupZip)}\" isn't a save backup of this game ({e.FullName}).");
            }
        }
        if (entries.Count == 0)
        {
            throw new InvalidDataException("The backup is empty.");
        }
        var safety = Backup(paths, dataDir, titleId);
        foreach (var s in Find(paths, titleId))
        {
            Directory.Delete(s.Path, recursive: true);
        }
        var nand = System.IO.Path.GetFullPath(paths.NandDir);
        foreach (var e in entries)
        {
            var target = System.IO.Path.GetFullPath(System.IO.Path.Combine(nand, e.FullName));
            if (!target.StartsWith(nand + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unsafe path in backup: {e.FullName}");
            }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            e.ExtractToFile(target, overwrite: true);
        }
        return safety;
    }

    /// <summary>user/save/…/&lt;TITLEID&gt;/… with no "..": what a backup of this game may contain.</summary>
    private static bool IsSaveEntry(string entry, string titleId)
    {
        var parts = entry.Replace('\\', '/').Split('/');
        return parts.Length >= 5 && parts[0] == "user" && parts[1] == "save" && !parts.Contains("..") &&
               parts.Skip(3).Take(parts.Length - 4).Any(p => p.Equals(titleId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Deletes the game's saves after backing them up (returns that backup).</summary>
    public static SaveBackup? DeleteSaves(EdenPaths paths, string dataDir, string titleId)
    {
        var backup = Backup(paths, dataDir, titleId);
        foreach (var s in Find(paths, titleId))
        {
            Directory.Delete(s.Path, recursive: true);
        }
        return backup;
    }
}
