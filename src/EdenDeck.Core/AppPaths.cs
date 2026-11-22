namespace EdenDeck.Core;

/// <summary>EdenDeck's own directories (XDG). Never inside the emulator's directories.</summary>
public sealed class AppPaths
{
    public string Data { get; }
    public string Config { get; }
    public string Cache { get; }
    public string Runtime { get; }

    public AppPaths(Func<string, string?>? env = null)
    {
        env ??= System.Environment.GetEnvironmentVariable;
        var home = env("HOME") ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        Data = Path.Combine(Xdg(env, "XDG_DATA_HOME", home, ".local/share"), "edendeck");
        Config = Path.Combine(Xdg(env, "XDG_CONFIG_HOME", home, ".config"), "edendeck");
        Cache = Path.Combine(Xdg(env, "XDG_CACHE_HOME", home, ".cache"), "edendeck");
        var runtime = env("XDG_RUNTIME_DIR");
        Runtime = string.IsNullOrEmpty(runtime) ? Path.Combine(Path.GetTempPath(), "edendeck-" + System.Environment.UserName) : Path.Combine(runtime, "edendeck");
    }

    public string ProfilesFile => Path.Combine(Config, "games.json");
    public string SettingsFile => Path.Combine(Config, "settings.json");
    public string KeySetsDir => Path.Combine(Data, "keysets");
    public string EmulatorsDir => Path.Combine(Data, "emulators");
    public string LibraryCacheFile => Path.Combine(Cache, "library.json");

    private static string Xdg(Func<string, string?> env, string name, string home, string fallback)
    {
        var v = env(name);
        return string.IsNullOrEmpty(v) ? Path.Combine(home, fallback) : v;
    }
}
