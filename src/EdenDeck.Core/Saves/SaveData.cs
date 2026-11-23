using System.Globalization;
using System.IO.Compression;
using EdenDeck.Core.Library;

namespace EdenDeck.Core.Saves;

/// <summary>One save folder or file of a game.</summary>
/// <param name="Relative">'/'-separated name inside a backup zip.</param>
public sealed record SaveFolder(string Relative, string Path, long Bytes, DateTime LastWriteUtc);

/// <summary>A backup zip of a game's saves.</summary>
public sealed record SaveBackup(string Path, DateTime CreatedUtc, long Bytes, bool Automatic)
{
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
}

/// <summary>
/// Save backups for a backend: what the game's saves are, and back up / restore / delete with a
/// backup first. Every backend keeps its zips in &lt;data&gt;/saves/&lt;title id&gt;/.
/// </summary>
public interface ISaveData
{
    IReadOnlyList<SaveFolder> Saves(GameEntry game);
    IReadOnlyList<SaveBackup> Backups(GameEntry game);
    string BackupDir(GameEntry game);

    /// <summary>Null when the game has no save yet.</summary>
    SaveBackup? Backup(GameEntry game, bool automatic = false);

    /// <summary>Backs up what's there, then puts the backup back. Returns the safety backup.</summary>
    SaveBackup? Restore(GameEntry game, string backupZip);

    /// <summary>Backs up, then deletes the save. Returns the backup.</summary>
    SaveBackup? Delete(GameEntry game);
}

/// <summary>Shared zip handling: names, listing and pruning of backups.</summary>
public static class SaveZips
{
    /// <summary>Automatic backups (before each play) kept per game; manual ones are never pruned.</summary>
    public const int KeepAutomatic = 5;

    public static string Dir(string dataDir, string titleId) => System.IO.Path.Combine(dataDir, "saves", titleId.ToUpperInvariant());

    public static IReadOnlyList<SaveBackup> List(string dir)
    {
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

    /// <summary>Writes a zip of (entry name, file) pairs as yyyyMMdd-HHmmss[-auto].zip and prunes old automatic ones.</summary>
    public static SaveBackup Write(string dir, IEnumerable<(string Entry, string File)> files, bool automatic, DateTime? now = null)
    {
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
            foreach (var (entry, f) in files)
            {
                zip.CreateEntryFromFile(f, entry, CompressionLevel.Optimal);
            }
        }
        File.Move(tmp, file);
        if (automatic)
        {
            foreach (var old in List(dir).Where(b => b.Automatic).Skip(KeepAutomatic))
            {
                File.Delete(old.Path);
            }
        }
        return List(dir).First(b => b.Path == file);
    }
}
