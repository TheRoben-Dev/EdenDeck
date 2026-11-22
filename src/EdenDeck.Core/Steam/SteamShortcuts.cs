using System.Text;

namespace EdenDeck.Core.Steam;

/// <summary>One non-Steam game shortcut, as Scanline writes it.</summary>
public sealed record SteamShortcut
{
    public required string AppName { get; init; }

    /// <summary>Program to run, unquoted (written quoted, the way Steam does).</summary>
    public required string Exe { get; init; }

    public required string StartDir { get; init; }

    public string LaunchOptions { get; init; } = "";

    public string Icon { get; init; } = "";

    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// The id Steam gives a non-Steam shortcut: CRC32 of the quoted exe plus the name, top bit set.
    /// Used for artwork file names (grid/&lt;id&gt;p.png) and steam://rungameid links.
    /// </summary>
    public uint AppId => ComputeAppId($"\"{Exe}\"", AppName);

    public static uint ComputeAppId(string quotedExe, string appName) =>
        Crc32(Encoding.UTF8.GetBytes(quotedExe + appName)) | 0x80000000u;

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }
        return ~crc;
    }
}

/// <summary>
/// Reads and writes Steam's non-Steam shortcuts (<c>userdata/&lt;account&gt;/config/shortcuts.vdf</c>).
/// Entries Scanline didn't write are kept as they are. Steam reads this file at start and writes
/// it back when its own shortcuts change, so a change made while Steam runs shows up after Steam
/// restarts (and Steam must not be holding an older copy it will save over ours: see
/// <see cref="SteamRunning"/>).
/// </summary>
public sealed class SteamShortcuts
{
    private readonly string _file;
    private readonly VdfMap _root;

    private SteamShortcuts(string file, VdfMap root)
    {
        _file = file;
        _root = root;
    }

    public string FilePath => _file;

    /// <summary>Steam install roots in the order Steam itself prefers (native, then Flatpak).</summary>
    public static IEnumerable<string> SteamRoots(string home)
    {
        yield return Path.Combine(home, ".local", "share", "Steam");
        yield return Path.Combine(home, ".steam", "steam");
        yield return Path.Combine(home, ".steam", "root");
        yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
    }

    /// <summary>
    /// Every Steam account on this machine that has signed in (has a userdata/&lt;id&gt;/config folder),
    /// most recently used first. Account 0 (anonymous) is skipped.
    /// </summary>
    public static IReadOnlyList<string> AccountConfigDirs(string home)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var dirs = new List<(string Dir, DateTime Used)>();
        foreach (var root in SteamRoots(home))
        {
            var userdata = Path.Combine(root, "userdata");
            if (!Directory.Exists(userdata))
            {
                continue;
            }
            foreach (var acct in Directory.EnumerateDirectories(userdata))
            {
                var name = Path.GetFileName(acct);
                if (name == "0" || !name.All(char.IsAsciiDigit))
                {
                    continue;
                }
                var config = Path.Combine(acct, "config");
                if (!Directory.Exists(config))
                {
                    continue;
                }
                var real = PathUtil.RealPath(config);
                if (!seen.Add(real))
                {
                    continue;  // ~/.steam/steam is usually a link to ~/.local/share/Steam
                }
                var localconfig = Path.Combine(config, "localconfig.vdf");
                dirs.Add((config, File.Exists(localconfig) ? File.GetLastWriteTimeUtc(localconfig) : Directory.GetLastWriteTimeUtc(config)));
            }
        }
        return dirs.OrderByDescending(d => d.Used).Select(d => d.Dir).ToList();
    }

    /// <summary>True while a Steam client is running for this user (its pid file names a live process).</summary>
    public static bool SteamRunning(string home)
    {
        foreach (var pidFile in new[] { Path.Combine(home, ".steam", "steam.pid"), Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".steam", "steam.pid") })
        {
            try
            {
                if (File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile).Trim(), out var pid) && Directory.Exists($"/proc/{pid}"))
                {
                    return true;
                }
            }
            catch (IOException)
            {
            }
        }
        return false;
    }

    public static SteamShortcuts Load(string configDir)
    {
        var file = Path.Combine(configDir, "shortcuts.vdf");
        VdfMap root;
        if (File.Exists(file) && new FileInfo(file).Length > 0)
        {
            root = BinaryVdf.Read(File.ReadAllBytes(file));
        }
        else
        {
            root = new VdfMap();
        }
        if (root.Map("shortcuts") == null)
        {
            root["shortcuts"] = new VdfMap();
        }
        return new SteamShortcuts(file, root);
    }

    private VdfMap List => _root.Map("shortcuts")!;

    public IEnumerable<VdfMap> Entries => List.Items.Select(kv => kv.Value).OfType<VdfMap>();

    /// <summary>The entry Steam would treat as this shortcut (same app id), if present.</summary>
    public VdfMap? Find(uint appId) => Entries.FirstOrDefault(e => (uint)(e.Int("appid") ?? 0) == appId ||
        SteamShortcut.ComputeAppId(e.Str("Exe") ?? e.Str("exe") ?? "", e.Str("AppName") ?? e.Str("appname") ?? "") == appId);

    /// <summary>Adds the shortcut, or updates the one with the same exe and name in place. Returns true if it was new.</summary>
    public bool AddOrUpdate(SteamShortcut s)
    {
        var entry = Find(s.AppId);
        var added = entry == null;
        if (entry == null)
        {
            entry = new VdfMap();
            var next = Entries.Any() ? List.Items.Select(kv => int.TryParse(kv.Key, out var n) ? n : -1).Max() + 1 : 0;
            List[next.ToString(System.Globalization.CultureInfo.InvariantCulture)] = entry;
            entry["LastPlayTime"] = 0;
        }
        entry["appid"] = unchecked((int)s.AppId);
        entry["AppName"] = s.AppName;
        entry["Exe"] = $"\"{s.Exe}\"";
        entry["StartDir"] = $"\"{s.StartDir}\"";
        entry["icon"] = s.Icon;
        entry["ShortcutPath"] = entry.Str("ShortcutPath") ?? "";
        entry["LaunchOptions"] = s.LaunchOptions;
        entry["IsHidden"] = entry.Int("IsHidden") ?? 0;
        entry["AllowDesktopConfig"] = entry.Int("AllowDesktopConfig") ?? 1;
        entry["AllowOverlay"] = entry.Int("AllowOverlay") ?? 1;
        entry["OpenVR"] = entry.Int("OpenVR") ?? 0;
        entry["Devkit"] = entry.Int("Devkit") ?? 0;
        entry["DevkitGameID"] = entry.Str("DevkitGameID") ?? "";
        entry["DevkitOverrideAppID"] = entry.Int("DevkitOverrideAppID") ?? 0;
        var tags = new VdfMap();
        for (var i = 0; i < s.Tags.Count; i++)
        {
            tags[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = s.Tags[i];
        }
        entry["tags"] = tags;
        return added;
    }

    /// <summary>Removes the shortcut with this app id. Returns true if one was removed.</summary>
    public bool Remove(uint appId)
    {
        var entry = Find(appId);
        if (entry == null)
        {
            return false;
        }
        var kept = Entries.Where(e => e != entry).ToList();
        var list = new VdfMap();
        for (var i = 0; i < kept.Count; i++)
        {
            list[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = kept[i];
        }
        _root["shortcuts"] = list;
        return true;
    }

    /// <summary>The file's bytes as <see cref="Save"/> would write them.</summary>
    public byte[] ToBytes() => BinaryVdf.Write(_root);

    /// <summary>Writes the file atomically, keeping one backup of the previous version (shortcuts.vdf.scanline-bak).</summary>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        if (File.Exists(_file))
        {
            File.Copy(_file, _file + ".scanline-bak", overwrite: true);
        }
        var tmp = _file + ".scanline.tmp";
        using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write))
        {
            f.Write(ToBytes());
            f.Flush(flushToDisk: true);  // may be written moments before a reboot
        }
        File.Move(tmp, _file, overwrite: true);
    }

    /// <summary>Where Steam looks for this shortcut's artwork (portrait capsule, hero, logo, wide capsule).</summary>
    public static string GridDir(string configDir) => Path.Combine(configDir, "grid");
}
