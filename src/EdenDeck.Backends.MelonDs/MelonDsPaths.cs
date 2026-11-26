using EdenDeck.Core.Config;

namespace EdenDeck.Backends.MelonDs;

/// <summary>
/// Where melonDS keeps things (frontend/qt_sdl/main.cpp): Qt's config location plus "melonDS", that
/// is <c>$XDG_CONFIG_HOME/melonDS</c> or <c>~/.config/melonDS</c>, holding <c>melonDS.toml</c>. The
/// Flatpak keeps it inside its sandbox home. Saves, save states and cheat files sit next to each
/// game unless melonDS's path settings say otherwise.
/// </summary>
public sealed class MelonDsPaths(Func<string, string?> env, bool flatpak)
{
    private string Home => env("HOME") ?? "";

    /// <summary>What melonDS sees as XDG_CONFIG_HOME.</summary>
    public string ConfigRoot => flatpak
        ? Path.Combine(Home, ".var", "app", MelonDsInstallManager.FlatpakId, "config")
        : env("XDG_CONFIG_HOME") is { Length: > 0 } x && Path.IsPathRooted(x) ? x : Path.Combine(Home, ".config");

    public string ConfigDir => Path.Combine(ConfigRoot, "melonDS");

    public string ConfigFile => Path.Combine(ConfigDir, "melonDS.toml");

    /// <summary>Where Scanline copies the player's BIOS and firmware.</summary>
    public string SystemDir => Path.Combine(ConfigDir, "scanline");

    /// <summary>
    /// The XDG_CONFIG_HOME a game with its own settings is started with: melonDS then reads (and
    /// writes back) <c>&lt;this&gt;/melonDS/melonDS.toml</c>, a copy with that game's settings, and
    /// the player's own config stays as it was. Inside melonDS's folder, so the Flatpak can read it.
    /// </summary>
    public string RunRoot => Path.Combine(ConfigDir, "scanline-run");

    public string RunConfigFile => Path.Combine(RunRoot, "melonDS", "melonDS.toml");

    public TomlFile Load() => TomlFile.Load(ConfigFile);
}
