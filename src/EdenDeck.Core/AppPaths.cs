namespace EdenDeck.Core;

/// <summary>Scanline's own directories (XDG). Never inside the emulator's directories.</summary>
public sealed class AppPaths
{
    public const string Name = "scanline";

    /// <summary>The folder name before the app was renamed (Scanline).</summary>
    public const string LegacyName = "edendeck";

    public string Data { get; }
    public string Config { get; }
    public string Cache { get; }
    public string Runtime { get; }

    public AppPaths(Func<string, string?>? env = null)
    {
        env ??= System.Environment.GetEnvironmentVariable;
        var home = env("HOME") ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        Data = Path.Combine(Xdg(env, "XDG_DATA_HOME", home, ".local/share"), Name);
        Config = Path.Combine(Xdg(env, "XDG_CONFIG_HOME", home, ".config"), Name);
        Cache = Path.Combine(Xdg(env, "XDG_CACHE_HOME", home, ".cache"), Name);
        var runtime = env("XDG_RUNTIME_DIR");
        Runtime = string.IsNullOrEmpty(runtime) ? Path.Combine(Path.GetTempPath(), Name + "-" + System.Environment.UserName) : Path.Combine(runtime, Name);
    }

    public string ProfilesFile => Path.Combine(Config, "games.json");
    public string SettingsFile => Path.Combine(Config, "settings.json");
    public string KeySetsDir => Path.Combine(Data, "keysets");
    public string EmulatorsDir => Path.Combine(Data, "emulators");
    public string LibraryCacheFile => Path.Combine(Cache, "library.json");

    /// <summary>
    /// Moves Eden Deck's data, config and cache folders to Scanline's names (once) and leaves a
    /// link at each old path, so absolute paths saved before the rename (installed Eden, key sets,
    /// Eden's own config) keep working. Folders that already exist under the new name are left alone.
    /// Returns the folders that were moved.
    /// </summary>
    public static IReadOnlyList<string> MigrateFromEdenDeck(Func<string, string?>? env = null)
    {
        env ??= System.Environment.GetEnvironmentVariable;
        var home = env("HOME") ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        var moved = new List<string>();
        foreach (var (name, fallback) in new[] { ("XDG_DATA_HOME", ".local/share"), ("XDG_CONFIG_HOME", ".config"), ("XDG_CACHE_HOME", ".cache") })
        {
            var root = Xdg(env, name, home, fallback);
            var old = Path.Combine(root, LegacyName);
            var target = Path.Combine(root, Name);
            try
            {
                var info = new DirectoryInfo(old);
                if (!info.Exists || info.LinkTarget != null || Directory.Exists(target) || File.Exists(target))
                {
                    continue;
                }
                Directory.Move(old, target);
                Directory.CreateSymbolicLink(old, target);
                moved.Add(target);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        return moved;
    }

    private static string Xdg(Func<string, string?> env, string name, string home, string fallback)
    {
        var v = env(name);
        return string.IsNullOrEmpty(v) ? Path.Combine(home, fallback) : v;
    }
}
