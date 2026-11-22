using System.IO.Compression;
using System.Text.RegularExpressions;
using EdenDeck.Backends.Eden.Config;

namespace EdenDeck.Backends.Eden.Mods;

/// <summary>A mod folder for one game: <c>&lt;load&gt;/&lt;TITLEID&gt;/&lt;Name&gt;/{exefs,romfs,...}</c>.</summary>
/// <param name="Name">Folder name, which is also the add-on name Eden shows and stores in [DisabledAddOns].</param>
/// <param name="Kinds">What it changes, in Eden's words (LayeredFS, IPS, Cheats, …).</param>
public sealed record ModInfo(string Name, string Path, IReadOnlyList<string> Kinds, bool Enabled, long Bytes);

/// <summary>Two or more enabled mods replacing the same game file; Eden uses <see cref="Winner"/>'s copy.</summary>
public sealed record ModConflict(string File, IReadOnlyList<string> Mods)
{
    /// <summary>Eden layers mods sorted by folder name, and the first one wins.</summary>
    public string Winner => Mods[0];
}

/// <summary>
/// Per-game mods, managed the way Eden reads them (src/core/file_sys/patch_manager.cpp): every
/// subfolder of the game's load dir with an exefs, romfs, romfslite, romfs_ext or cheats folder
/// is one add-on, named by its folder; turning one off adds its name to [DisabledAddOns]. Eden
/// layers enabled mods sorted by name, first wins. Installing from a .zip finds the mod folders
/// inside however the archive nests them (Mod/romfs, TITLEID/Mod/romfs,
/// atmosphere/contents/TITLEID/romfs, or romfs at the top).
/// </summary>
public static class EdenMods
{
    /// <summary>Folders Eden loads from a mod (matched case-insensitively, like Eden).</summary>
    public static readonly string[] Parts = ["exefs", "romfs", "romfslite", "romfs_ext", "cheats"];

    private static readonly Regex TitleIdName = new("^[0-9A-Fa-f]{16}$", RegexOptions.Compiled);

    public static IReadOnlyList<ModInfo> List(EdenPaths paths, string titleId)
    {
        var dir = paths.ModDir(titleId);
        if (!Directory.Exists(dir))
        {
            return [];
        }
        var disabled = Disabled(paths.GlobalConfigFile, titleId);
        return Directory.EnumerateDirectories(dir)
            .Select(d => (Dir: d, Kinds: Kinds(d)))
            .Where(x => x.Kinds.Count > 0)
            .OrderBy(x => Path.GetFileName(x.Dir), StringComparer.Ordinal)
            .Select(x => new ModInfo(Path.GetFileName(x.Dir), x.Dir, x.Kinds, !disabled.Contains(Path.GetFileName(x.Dir)), Size(x.Dir)))
            .ToList();
    }

    /// <summary>What a mod folder changes, using the labels Eden's add-ons list shows.</summary>
    public static IReadOnlyList<string> Kinds(string modDir)
    {
        var kinds = new List<string>();
        if (Sub(modDir, "exefs") is { } exefs && NonEmpty(exefs))
        {
            var files = Directory.EnumerateFiles(exefs).Select(Path.GetFileName).ToList();
            if (files.Any(f => f!.EndsWith(".ips", StringComparison.OrdinalIgnoreCase)))
            {
                kinds.Add("IPS");
            }
            if (files.Any(f => f!.EndsWith(".pchtxt", StringComparison.OrdinalIgnoreCase)))
            {
                kinds.Add("IPSwitch");
            }
            if (files.Any(f => !f!.EndsWith(".ips", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".pchtxt", StringComparison.OrdinalIgnoreCase)))
            {
                kinds.Add("LayeredExeFS");
            }
        }
        if (Sub(modDir, "romfs") is { } r && NonEmpty(r) || Sub(modDir, "romfslite") is { } rl && NonEmpty(rl))
        {
            kinds.Add("LayeredFS");
        }
        if (Sub(modDir, "romfs_ext") is { } re && NonEmpty(re))
        {
            kinds.Add("ExtLayeredFS");
        }
        if (Sub(modDir, "cheats") is { } c && NonEmpty(c))
        {
            kinds.Add("Cheats");
        }
        return kinds;
    }

    public static HashSet<string> Disabled(string configFile, string titleId) =>
        (DisabledAddOns.Read(QtIni.Load(configFile)).GetValueOrDefault(DisabledAddOns.ParseTitleId(titleId)) ?? []).ToHashSet(StringComparer.Ordinal);

    /// <summary>Turns a mod on or off in Eden's add-ons list for this game.</summary>
    public static void SetEnabled(string configFile, string titleId, string name, bool enabled)
    {
        var ini = QtIni.Load(configFile);
        var all = DisabledAddOns.Read(ini);
        var tid = DisabledAddOns.ParseTitleId(titleId);
        var list = all.GetValueOrDefault(tid) ?? [];
        var changed = enabled ? list.RemoveAll(n => n == name) > 0 : !list.Contains(name);
        if (!changed)
        {
            return;
        }
        if (!enabled)
        {
            list.Add(name);
        }
        all[tid] = list;
        if (list.Count == 0)
        {
            all.Remove(tid);
        }
        DisabledAddOns.Write(ini, all);
        ini.Save(configFile);
    }

    /// <summary>Deletes a mod folder (and forgets its off switch).</summary>
    public static void Remove(EdenPaths paths, string titleId, string name)
    {
        var dir = Path.Combine(paths.ModDir(titleId), SafeName(name));
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
        SetEnabled(paths.GlobalConfigFile, titleId, name, true);
    }

    /// <summary>
    /// Installs the mod(s) in a .zip. Returns the installed folder names. Throws
    /// <see cref="InvalidDataException"/> if there's no mod inside or it's for another game, and
    /// <see cref="IOException"/> if a mod with the same name exists and <paramref name="replace"/> is false.
    /// </summary>
    public static IReadOnlyList<string> InstallZip(EdenPaths paths, string titleId, string zipPath, bool replace = false)
    {
        var staging = Path.Combine(paths.ModDir(titleId), ".scanline-install-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(staging);
            ZipFile.ExtractToDirectory(zipPath, staging);  // rejects entries that escape the folder
            return InstallFrom(paths, titleId, staging, Path.GetFileNameWithoutExtension(zipPath), replace, move: true);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    /// <summary>Installs the mod(s) in a folder (copied, the source stays).</summary>
    public static IReadOnlyList<string> InstallFolder(EdenPaths paths, string titleId, string folder, bool replace = false) =>
        InstallFrom(paths, titleId, folder, Path.GetFileName(folder.TrimEnd('/')), replace, move: false);

    private static IReadOnlyList<string> InstallFrom(EdenPaths paths, string titleId, string source, string fallbackName, bool replace, bool move)
    {
        var roots = FindModRoots(source);
        if (roots.Count == 0)
        {
            throw new InvalidDataException("No mod found inside. A mod has an exefs, romfs or cheats folder.");
        }
        var forOthers = roots.Where(r => r.TitleId != null && !r.TitleId.Equals(titleId, StringComparison.OrdinalIgnoreCase)).ToList();
        roots = roots.Except(forOthers).ToList();
        if (roots.Count == 0)
        {
            throw new InvalidDataException($"This mod is for another game ({forOthers[0].TitleId!.ToUpperInvariant()}).");
        }
        var dest = paths.ModDir(titleId);
        Directory.CreateDirectory(dest);
        // Name each mod; two roots with the same name get a number.
        var named = new List<(string Root, string Name)>();
        foreach (var r in roots)
        {
            var name = SafeName(r.Name ?? fallbackName);
            var unique = name;
            for (var n = 2; named.Any(x => x.Name == unique); n++)
            {
                unique = $"{name} ({n})";
            }
            named.Add((r.Dir, unique));
        }
        var existing = named.Where(x => Directory.Exists(Path.Combine(dest, x.Name))).Select(x => x.Name).ToList();
        if (existing.Count > 0 && !replace)
        {
            throw new IOException($"\"{existing[0]}\" is already installed.");
        }
        foreach (var (root, name) in named)
        {
            var target = Path.Combine(dest, name);
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
            Directory.CreateDirectory(target);
            // Only the parts Eden reads; readme files and the like stay out of the load dir.
            foreach (var part in Directory.EnumerateDirectories(root).Where(d => Parts.Contains(Path.GetFileName(d).ToLowerInvariant())))
            {
                var to = Path.Combine(target, Path.GetFileName(part).ToLowerInvariant());
                if (move)
                {
                    Directory.Move(part, to);
                }
                else
                {
                    CopyDir(part, to);
                }
            }
        }
        return named.Select(x => x.Name).ToList();
    }

    private sealed record Root(string Dir, string? Name, string? TitleId);

    /// <summary>Folders that hold mod parts, with the name and game they imply from where they sit.</summary>
    private static List<Root> FindModRoots(string top)
    {
        var result = new List<Root>();
        void Walk(string dir, int depth)
        {
            var subs = Directory.EnumerateDirectories(dir).ToList();
            if (subs.Any(s => Parts.Contains(Path.GetFileName(s).ToLowerInvariant())))
            {
                var name = Path.GetFileName(dir);
                var parent = Path.GetDirectoryName(dir);
                string? tid = null;
                if (dir == top)
                {
                    name = null;  // parts at the archive's top: named after the archive
                }
                else if (TitleIdName.IsMatch(name))
                {
                    tid = name;   // atmosphere/contents/TITLEID/romfs
                    name = null;
                }
                else if (parent != null && parent != top && TitleIdName.IsMatch(Path.GetFileName(parent)))
                {
                    tid = Path.GetFileName(parent);  // TITLEID/Mod/romfs
                }
                result.Add(new Root(dir, name, tid));
                return;  // a mod's own folders aren't searched for more mods
            }
            if (depth < 6)
            {
                foreach (var s in subs)
                {
                    Walk(s, depth + 1);
                }
            }
        }
        Walk(top, 0);
        return result;
    }

    /// <summary>
    /// Files that more than one enabled mod replaces (exefs and romfs; IPS patches stack, so they
    /// don't count). Eden uses the copy from the mod whose folder name sorts first.
    /// </summary>
    public static IReadOnlyList<ModConflict> Conflicts(IEnumerable<ModInfo> mods)
    {
        var owners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mods.Where(m => m.Enabled).OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            foreach (var (part, label) in new[] { ("exefs", "exefs"), ("romfs", "romfs"), ("romfslite", "romfs") })
            {
                if (Sub(m.Path, part) is not { } dir)
                {
                    continue;
                }
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    if (part == "exefs" && (f.EndsWith(".ips", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".pchtxt", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                    var rel = label + "/" + Path.GetRelativePath(dir, f).Replace('\\', '/');
                    if (!owners.TryGetValue(rel, out var list))
                    {
                        owners[rel] = list = [];
                    }
                    if (!list.Contains(m.Name))
                    {
                        list.Add(m.Name);
                    }
                }
            }
        }
        return owners.Where(kv => kv.Value.Count > 1).OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new ModConflict(kv.Key, kv.Value)).ToList();
    }

    /// <summary>A folder name that's safe on every filesystem Eden runs on.</summary>
    public static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var s = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().Trim('.');
        return s.Length == 0 ? "Mod" : s;
    }

    private static string? Sub(string dir, string name) =>
        Directory.Exists(dir) ? Directory.EnumerateDirectories(dir).FirstOrDefault(d => Path.GetFileName(d).Equals(name, StringComparison.OrdinalIgnoreCase)) : null;

    private static bool NonEmpty(string dir) => Directory.EnumerateFileSystemEntries(dir).Any();

    private static long Size(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from))
        {
            File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        }
        foreach (var d in Directory.EnumerateDirectories(from))
        {
            CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
        }
    }
}
