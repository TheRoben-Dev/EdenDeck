namespace EdenDeck.Backends.Rpcs3;

/// <summary>
/// Where RPCS3 keeps things (rpcs3/Utilities/File.cpp, rpcs3/Emu/vfs_config.h): config.yml,
/// custom_configs/ and vfs.yml in <c>$XDG_CONFIG_HOME/rpcs3/</c>; dev_hdd0 and dev_flash under
/// the emulator dir (the config dir unless vfs.yml moves them); caches in
/// <c>$XDG_CACHE_HOME/rpcs3/</c>. The Flatpak keeps all of it inside its sandbox home.
/// </summary>
public sealed class Rpcs3Paths(Func<string, string?> env, bool flatpak)
{
    private string Home => env("HOME") ?? "";

    private string FlatpakRoot => Path.Combine(Home, ".var", "app", Rpcs3InstallManager.FlatpakId);

    public string ConfigDir => flatpak
        ? Path.Combine(FlatpakRoot, "config", "rpcs3")
        : Path.Combine(env("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(Home, ".config"), "rpcs3");

    public string CacheDir => flatpak
        ? Path.Combine(FlatpakRoot, "cache", "rpcs3")
        : Path.Combine(env("XDG_CACHE_HOME") is { Length: > 0 } x ? x : env("XDG_CONFIG_HOME") is { Length: > 0 } c ? c : Path.Combine(Home, ".cache"), "rpcs3");  // RPCS3's own fallback order

    public string ConfigFile => Path.Combine(ConfigDir, "config.yml");
    public string VfsFile => Path.Combine(ConfigDir, "vfs.yml");
    public string CustomConfigFile(string titleId) => Path.Combine(ConfigDir, "custom_configs", $"config_{titleId}.yml");

    private string Vfs(string key, string fallback)
    {
        var vfs = Rpcs3Yaml.Load(VfsFile);
        var emuDir = vfs.GetRoot("$(EmulatorDir)") is { Length: > 0 } e ? e : ConfigDir + "/";
        if (!emuDir.EndsWith('/'))
        {
            emuDir += "/";
        }
        var path = vfs.GetRoot(key) is { Length: > 0 } p ? p : fallback;
        return Path.TrimEndingDirectorySeparator(path.Replace("$(EmulatorDir)", emuDir));
    }

    public string DevHdd0 => Vfs("/dev_hdd0/", "$(EmulatorDir)dev_hdd0/");
    public string DevFlash => Vfs("/dev_flash/", "$(EmulatorDir)dev_flash/");

    /// <summary>Installed (PSN and disc-update) games: one folder per title id.</summary>
    public string GamesDir => Path.Combine(DevHdd0, "game");

    /// <summary>Per-game PPU/SPU and shader caches: cache/&lt;TITLEID&gt;/.</summary>
    public string GameCacheDir(string titleId) => Path.Combine(CacheDir, "cache", titleId);

    /// <summary>A file that's only there once a firmware is installed.</summary>
    public string FirmwareMarker => Path.Combine(DevFlash, "vsh", "module", "vsh.self");
}
