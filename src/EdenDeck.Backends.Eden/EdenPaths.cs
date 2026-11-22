using EdenDeck.Backends.Eden.Config;

namespace EdenDeck.Backends.Eden;

/// <summary>
/// Eden's directories on Linux, resolved exactly like Eden's PathManagerImpl::Reinitialize
/// (src/common/fs/path_util.cpp) plus the [Data%20Storage] overrides from qt-config.ini.
/// See docs/eden-integration.md §3.
/// </summary>
public sealed class EdenPaths
{
    public string DataDir { get; }
    public string ConfigDir { get; }
    public string CacheDir { get; }
    public bool Portable { get; }

    public string NandDir { get; private set; }
    public string SdmcDir { get; private set; }
    public string LoadDir { get; private set; }
    public string DumpDir { get; private set; }

    public string KeysDir => Path.Combine(DataDir, "keys");
    public string LosslessDir => Path.Combine(DataDir, "lossless");
    public string GlobalConfigFile => Path.Combine(ConfigDir, "qt-config.ini");
    public string CustomConfigDir => Path.Combine(ConfigDir, "custom");
    public string InputProfileDir => Path.Combine(ConfigDir, "input");
    public string FirmwareDir => Path.Combine(NandDir, "system", "Contents", "registered");

    public string PerGameConfigFile(string titleId) => Path.Combine(CustomConfigDir, titleId.ToUpperInvariant() + ".ini");
    public string ModDir(string titleId) => Path.Combine(LoadDir, titleId.ToUpperInvariant());
    public string SdModDir(string titleId) => Path.Combine(SdmcDir, "atmosphere", "contents", titleId.ToUpperInvariant());

    private EdenPaths(string data, string config, string cache, bool portable)
    {
        DataDir = data;
        ConfigDir = config;
        CacheDir = cache;
        Portable = portable;
        NandDir = Path.Combine(data, "nand");
        SdmcDir = Path.Combine(data, "sdmc");
        LoadDir = Path.Combine(data, "load");
        DumpDir = Path.Combine(data, "dump");
    }

    /// <param name="workingDirectory">Directory Eden will be started from (portable mode check).</param>
    public static EdenPaths Resolve(string workingDirectory, Func<string, string?>? env = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        EdenPaths paths;
        var portable = Path.Combine(workingDirectory, "user");
        if (Directory.Exists(portable))
        {
            paths = new EdenPaths(portable, Path.Combine(portable, "config"), Path.Combine(portable, "cache"), true);
        }
        else
        {
            var home = env("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            paths = new EdenPaths(
                Path.Combine(Xdg(env, "XDG_DATA_HOME", home, ".local/share"), "eden"),
                Path.Combine(Xdg(env, "XDG_CONFIG_HOME", home, ".config"), "eden"),
                Path.Combine(Xdg(env, "XDG_CACHE_HOME", home, ".cache"), "eden"),
                false);
        }
        paths.ApplyOverrides(QtIni.Load(paths.GlobalConfigFile));
        return paths;
    }

    internal void ApplyOverrides(QtIni globalConfig)
    {
        const string section = "Data%20Storage";
        NandDir = Override(globalConfig, section, "nand_directory") ?? NandDir;
        SdmcDir = Override(globalConfig, section, "sdmc_directory") ?? SdmcDir;
        LoadDir = Override(globalConfig, section, "load_directory") ?? LoadDir;
        DumpDir = Override(globalConfig, section, "dump_directory") ?? DumpDir;
    }

    private static string? Override(QtIni ini, string section, string key)
    {
        // Eden's ReadStringSetting without default: plain value, quotes stripped, "//" -> "/".
        var v = ini.Get(section, key);
        if (string.IsNullOrWhiteSpace(v))
        {
            return null;
        }
        return v.Replace("//", "/").TrimEnd('/');
    }

    private static string Xdg(Func<string, string?> env, string name, string home, string fallback)
    {
        var v = env(name);
        return string.IsNullOrEmpty(v) ? Path.Combine(home, fallback) : v;
    }
}
