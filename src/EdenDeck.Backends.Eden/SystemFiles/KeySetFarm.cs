namespace EdenDeck.Backends.Eden.SystemFiles;

/// <summary>
/// Applies a key set to one Eden launch without touching the user's real Eden directories:
/// builds a portable "user/" tree of symlinks (docs/eden-integration.md, "Per-game key and
/// firmware sets"). Eden is started with the farm root as its working directory.
/// </summary>
public sealed class KeySetFarm
{
    public string Root { get; }
    public IReadOnlyList<string> Warnings { get; }

    private KeySetFarm(string root, IReadOnlyList<string> warnings)
    {
        Root = root;
        Warnings = warnings;
    }

    public static KeySetFarm Build(EdenPaths real, KeySet set, string runtimeDir, string tag)
    {
        if (real.Portable)
        {
            throw new InvalidOperationException("Key sets can't be combined with a portable Eden install.");
        }
        var warnings = new List<string>();
        var root = Path.Combine(runtimeDir, "launch", $"{tag}-{Environment.ProcessId}");
        if (Directory.Exists(root))
        {
            DeleteTree(root);
        }
        var user = Path.Combine(root, "user");
        Directory.CreateDirectory(user);
        Directory.CreateDirectory(real.DataDir);
        Directory.CreateDirectory(real.ConfigDir);
        Directory.CreateDirectory(real.CacheDir);

        Link(Path.Combine(user, "config"), real.ConfigDir);
        Link(Path.Combine(user, "cache"), real.CacheDir);
        Link(Path.Combine(user, "keys"), set.Directory);

        var defaultNand = Path.Combine(real.DataDir, "nand");
        var nandOverridden = !SamePath(real.NandDir, defaultNand);
        foreach (var entry in Directory.EnumerateFileSystemEntries(real.DataDir))
        {
            var name = Path.GetFileName(entry);
            if (name is "keys" or "config" or "cache")
            {
                continue;
            }
            if (name == "nand" && set.HasFirmware && !nandOverridden)
            {
                continue;  // built below
            }
            Link(Path.Combine(user, name), entry);
        }

        if (set.HasFirmware)
        {
            if (nandOverridden)
            {
                warnings.Add("NAND is redirected in Eden's settings, so this key set's firmware can't be applied; only its keys are used.");
            }
            else
            {
                Directory.CreateDirectory(Path.Combine(defaultNand, "system", "Contents"));
                var nand = Path.Combine(user, "nand");
                Directory.CreateDirectory(nand);
                LinkAllExcept(defaultNand, nand, "system");
                var system = Path.Combine(nand, "system");
                Directory.CreateDirectory(system);
                LinkAllExcept(Path.Combine(defaultNand, "system"), system, "Contents");
                var contents = Path.Combine(system, "Contents");
                Directory.CreateDirectory(contents);
                LinkAllExcept(Path.Combine(defaultNand, "system", "Contents"), contents, "registered");
                Link(Path.Combine(contents, "registered"), Path.Combine(set.Directory, "firmware"));
            }
        }
        return new KeySetFarm(root, warnings);
    }

    public void Remove() => DeleteTree(Root);

    private static void LinkAllExcept(string realDir, string farmDir, string except)
    {
        if (!Directory.Exists(realDir))
        {
            return;
        }
        foreach (var entry in Directory.EnumerateFileSystemEntries(realDir))
        {
            var name = Path.GetFileName(entry);
            if (name != except)
            {
                Link(Path.Combine(farmDir, name), entry);
            }
        }
    }

    private static void Link(string linkPath, string target)
    {
        if (Directory.Exists(target))
        {
            Directory.CreateSymbolicLink(linkPath, target);
        }
        else
        {
            File.CreateSymbolicLink(linkPath, target);
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('/'), Path.GetFullPath(b).TrimEnd('/'), StringComparison.Ordinal);

    /// <summary>Deletes a tree without ever following symlinks into the user's real data.</summary>
    internal static void DeleteTree(string dir)
    {
        var info = new DirectoryInfo(dir);
        if (!info.Exists)
        {
            return;
        }
        if (info.LinkTarget != null)
        {
            info.Delete();
            return;
        }
        foreach (var e in info.EnumerateFileSystemInfos())
        {
            if (e.LinkTarget != null || e is FileInfo)
            {
                e.Delete();
            }
            else
            {
                DeleteTree(e.FullName);
            }
        }
        info.Delete();
    }
}
