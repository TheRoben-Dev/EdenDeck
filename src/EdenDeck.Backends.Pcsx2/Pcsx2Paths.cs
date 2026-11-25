namespace EdenDeck.Backends.Pcsx2;

/// <summary>
/// Where PCSX2 keeps things (pcsx2/Pcsx2Config.cpp, EmuFolders): its data root is
/// <c>$XDG_CONFIG_HOME/PCSX2</c> (or <c>~/.config/PCSX2</c>), the main settings are
/// <c>inis/PCSX2.ini</c>, and every other folder (bios, memcards, sstates, gamesettings, cheats,
/// cache, logs) is named in its <c>[Folders]</c> section, relative to the data root unless absolute.
/// The Flatpak keeps all of it inside its sandbox home.
/// </summary>
public sealed class Pcsx2Paths(Func<string, string?> env, bool flatpak)
{
    private string Home => env("HOME") ?? "";

    public string DataRoot => flatpak
        ? Path.Combine(Home, ".var", "app", Pcsx2InstallManager.FlatpakId, "config", "PCSX2")
        : Path.Combine(env("XDG_CONFIG_HOME") is { Length: > 0 } x && Path.IsPathRooted(x) ? x : Path.Combine(Home, ".config"), "PCSX2");

    public string ConfigFile => Path.Combine(DataRoot, "inis", "PCSX2.ini");

    private string Folder(string key, string fallback)
    {
        var value = Pcsx2Ini.Load(ConfigFile).Get("Folders", key) is { Length: > 0 } v ? v : fallback;
        return Path.IsPathRooted(value) ? value : Path.Combine(DataRoot, value);
    }

    public string BiosDir => Folder("Bios", "bios");
    public string MemoryCardsDir => Folder("MemoryCards", "memcards");
    public string SaveStatesDir => Folder("Savestates", "sstates");
    public string CheatsDir => Folder("Cheats", "cheats");
    public string PatchesDir => Folder("Patches", "patches");
    public string GameSettingsDir => Folder("GameSettings", "gamesettings");
    public string CacheDir => Folder("Cache", "cache");
    public string LogsDir => Folder("Logs", "logs");

    /// <summary>A game's own settings: <c>gamesettings/&lt;SERIAL&gt;_&lt;CRC&gt;.ini</c> (VMManager::GetGameSettingsPath).</summary>
    public string GameSettingsFile(string serial, uint crc) => Path.Combine(GameSettingsDir, $"{Sanitize(serial)}_{crc:X8}.ini");

    /// <summary>The player's cheats for a game: <c>cheats/&lt;SERIAL&gt;_&lt;CRC&gt;.pnach</c> (Patch::GetPnachFilename).</summary>
    public string CheatsFile(string serial, uint crc) => Path.Combine(CheatsDir, $"{serial}_{crc:X8}.pnach");

    private static string Sanitize(string s) => new(s.Select(c => c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c).ToArray());

    /// <summary>
    /// PCSX2.ini for writing. A new file gets <c>[UI] SettingsVersion = 1</c>, or PCSX2 would think
    /// it's broken and reset it, and no setup wizard (Scanline covers the BIOS and controller).
    /// </summary>
    public Pcsx2Ini LoadConfigForWrite()
    {
        var ini = Pcsx2Ini.Load(ConfigFile);
        if (ini.Get("UI", "SettingsVersion") == null)
        {
            ini.Set("UI", "SettingsVersion", "1");
            ini.Set("UI", "SetupWizardIncomplete", "false");
        }
        return ini;
    }
}
