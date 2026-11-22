using EdenDeck.Core.Backends;

namespace EdenDeck.Backends.Eden.SystemFiles;

/// <summary>Status and guided import of the user's own default keys and firmware into Eden's dirs.</summary>
public sealed class EdenSystemFiles : ISystemFiles
{
    private readonly Func<EdenPaths> _paths;
    private readonly string _backupRoot;
    private readonly Func<bool> _edenRunning;

    public EdenSystemFiles(Func<EdenPaths> paths, string backupRoot, Func<bool>? edenRunning = null)
    {
        _paths = paths;
        _backupRoot = backupRoot;
        _edenRunning = edenRunning ?? EdenProcess.IsRunning;
    }

    public IReadOnlyList<SystemFileStatus> Status()
    {
        var p = _paths();
        var prod = Path.Combine(p.KeysDir, "prod.keys");
        var title = Path.Combine(p.KeysDir, "title.keys");
        var fwCount = Directory.Exists(p.FirmwareDir) ? Directory.EnumerateFiles(p.FirmwareDir, "*.nca", SearchOption.AllDirectories).Count() : 0;
        return
        [
            new("prod.keys", File.Exists(prod), File.Exists(prod) ? prod : "Missing. Import your own prod.keys."),
            new("title.keys", File.Exists(title), File.Exists(title) ? title : "Optional."),
            new("firmware", fwCount > 0, fwCount > 0 ? $"{fwCount} files in {p.FirmwareDir}" : "Missing. Import your own firmware files."),
        ];
    }

    public void ImportDefaultKeys(string prodKeys, string? titleKeys)
    {
        EnsureNotRunning();
        KeySetStore.ValidateProdKeys(prodKeys);
        var dir = _paths().KeysDir;
        Directory.CreateDirectory(dir);
        Backup(dir, "keys");
        File.Copy(prodKeys, Path.Combine(dir, "prod.keys"), overwrite: true);
        if (titleKeys != null)
        {
            File.Copy(titleKeys, Path.Combine(dir, "title.keys"), overwrite: true);
        }
    }

    /// <summary>Same as Eden's installer: clear nand/system/Contents/registered, copy the .nca files in.</summary>
    public int ImportFirmware(string folder)
    {
        EnsureNotRunning();
        var files = KeySetStore.FirmwareFiles(folder);
        if (files.Count == 0)
        {
            throw new KeyImportException("No .nca firmware files in that folder.");
        }
        var dest = _paths().FirmwareDir;
        Directory.CreateDirectory(dest);
        Backup(dest, "firmware");
        foreach (var f in Directory.EnumerateFiles(dest))
        {
            File.Delete(f);
        }
        foreach (var d in Directory.EnumerateDirectories(dest))
        {
            KeySetFarm.DeleteTree(d);  // split NCAs are stored as "<name>.nca/00" directories
        }
        foreach (var f in files)
        {
            File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), overwrite: true);
        }
        return files.Count;
    }

    private void Backup(string dir, string kind)
    {
        if (!Directory.Exists(dir) || !Directory.EnumerateFileSystemEntries(dir).Any())
        {
            return;
        }
        CopyTree(dir, Path.Combine(_backupRoot, $"{kind}-{DateTime.Now:yyyyMMdd-HHmmss}"));
    }

    private static void CopyTree(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src))
        {
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
        }
        foreach (var d in Directory.EnumerateDirectories(src))
        {
            CopyTree(d, Path.Combine(dst, Path.GetFileName(d)));
        }
    }

    private void EnsureNotRunning()
    {
        if (_edenRunning())
        {
            throw new Config.EdenRunningException();
        }
    }
}
