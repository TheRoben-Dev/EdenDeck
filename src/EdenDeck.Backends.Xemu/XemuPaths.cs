namespace EdenDeck.Backends.Xemu;

/// <summary>
/// Where xemu keeps things (ui/xemu-settings.cc): SDL's preference folder for "xemu"/"xemu", that is
/// <c>$XDG_DATA_HOME/xemu/xemu</c> or <c>~/.local/share/xemu/xemu</c>, holding <c>xemu.toml</c> and
/// the generated <c>eeprom.bin</c>. The Flatpak keeps it inside its sandbox home. The boot ROM, flash
/// BIOS and hard disk image can be anywhere; xemu.toml names them under <c>[sys.files]</c>.
/// </summary>
public sealed class XemuPaths(Func<string, string?> env, bool flatpak)
{
    private string Home => env("HOME") ?? "";

    public string DataRoot => flatpak
        ? Path.Combine(Home, ".var", "app", XemuInstallManager.FlatpakId, "data", "xemu", "xemu")
        : Path.Combine(env("XDG_DATA_HOME") is { Length: > 0 } x && Path.IsPathRooted(x) ? x : Path.Combine(Home, ".local", "share"), "xemu", "xemu");

    public string ConfigFile => Path.Combine(DataRoot, "xemu.toml");

    /// <summary>Where Scanline copies the player's boot ROM and flash BIOS.</summary>
    public string SystemDir => Path.Combine(DataRoot, "scanline");

    public XemuToml Load() => XemuToml.Load(ConfigFile);
}
