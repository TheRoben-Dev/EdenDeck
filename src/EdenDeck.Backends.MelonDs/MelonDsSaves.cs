using System.IO.Compression;
using System.Text.RegularExpressions;
using EdenDeck.Core.Library;
using EdenDeck.Core.Saves;

namespace EdenDeck.Backends.MelonDs;

/// <summary>
/// melonDS's saves: the cartridge save <c>&lt;rom name&gt;.sav</c> and save states
/// <c>&lt;rom name&gt;.ml1</c>…<c>.ml8</c>, next to the ROM unless melonDS's SaveFilePath /
/// SavestatePath point elsewhere. A backup zips them as <c>save/…</c> and <c>states/…</c>.
/// </summary>
public sealed class MelonDsSaves(Func<MelonDsPaths> paths, string dataDir) : ISaveData
{
    private string Dir(GameEntry game, string key)
    {
        var configured = paths().Load().Get("Instance0." + key);
        return string.IsNullOrWhiteSpace(configured) ? Path.GetDirectoryName(game.Path)! : configured;
    }

    private IEnumerable<(string Entry, string File)> Files(GameEntry game)
    {
        var stem = Path.GetFileNameWithoutExtension(game.Path);
        var sav = Path.Combine(Dir(game, "SaveFilePath"), stem + ".sav");
        if (File.Exists(sav))
        {
            yield return ("save/" + Path.GetFileName(sav), sav);
        }
        var states = Dir(game, "SavestatePath");
        if (Directory.Exists(states))
        {
            var pattern = new Regex("^" + Regex.Escape(stem) + @"\.ml\d$");
            foreach (var f in Directory.EnumerateFiles(states).Where(f => pattern.IsMatch(Path.GetFileName(f))).Order(StringComparer.Ordinal))
            {
                yield return ("states/" + Path.GetFileName(f), f);
            }
        }
    }

    public IReadOnlyList<SaveFolder> Saves(GameEntry game) => Files(game)
        .Select(x => new FileInfo(x.File))
        .Select(f => new SaveFolder(f.Name, f.FullName, f.Length, f.LastWriteTimeUtc))
        .ToList();

    public string BackupDir(GameEntry game) => SaveZips.Dir(dataDir, game.TitleId);

    public IReadOnlyList<SaveBackup> Backups(GameEntry game) => SaveZips.List(BackupDir(game));

    public SaveBackup? Backup(GameEntry game, bool automatic = false) => Backup(game, automatic, null);

    public SaveBackup? Backup(GameEntry game, bool automatic, DateTime? now)
    {
        var files = Files(game).ToList();
        return files.Count == 0 ? null : SaveZips.Write(BackupDir(game), files, automatic, now);
    }

    public SaveBackup? Restore(GameEntry game, string backupZip)
    {
        var stem = Path.GetFileNameWithoutExtension(game.Path);
        var allowed = new Regex("^(save/" + Regex.Escape(stem) + @"\.sav|states/" + Regex.Escape(stem) + @"\.ml\d)$");
        using var zip = ZipFile.OpenRead(backupZip);
        var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        if (entries.Count == 0)
        {
            throw new InvalidDataException("The backup is empty.");
        }
        if (entries.FirstOrDefault(e => !allowed.IsMatch(e.FullName)) is { } bad)
        {
            throw new InvalidDataException($"\"{Path.GetFileName(backupZip)}\" isn't a save backup of this game ({bad.FullName}).");
        }
        var safety = Backup(game);
        foreach (var (_, f) in Files(game).ToList())
        {
            File.Delete(f);
        }
        foreach (var e in entries)
        {
            var dir = Dir(game, e.FullName.StartsWith("save/", StringComparison.Ordinal) ? "SaveFilePath" : "SavestatePath");
            Directory.CreateDirectory(dir);
            e.ExtractToFile(Path.Combine(dir, e.Name), overwrite: true);
        }
        return safety;
    }

    public SaveBackup? Delete(GameEntry game)
    {
        var backup = Backup(game);
        foreach (var (_, f) in Files(game).ToList())
        {
            File.Delete(f);
        }
        return backup;
    }
}
