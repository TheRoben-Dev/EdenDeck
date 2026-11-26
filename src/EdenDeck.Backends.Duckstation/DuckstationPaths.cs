using EdenDeck.Backends.Pcsx2;

namespace EdenDeck.Backends.Duckstation;

/// <summary>
/// Where DuckStation keeps things (core/core.cpp, EmuFolders): its data root is
/// <c>$XDG_CONFIG_HOME/duckstation</c> when that variable is set, otherwise
/// <c>~/.local/share/duckstation</c>; the settings are <c>settings.ini</c> there, and the other
/// folders (bios, memcards, savestates, gamesettings, cheats, patches) are named in it, relative to
/// the data root unless absolute. An old DuckStation Flatpak keeps all of it inside its sandbox home.
/// </summary>
public sealed class DuckstationPaths(Func<string, string?> env, bool flatpak)
{
    private string Home => env("HOME") ?? "";

    public string DataRoot => flatpak
        ? Path.Combine(Home, ".var", "app", DuckstationInstallManager.FlatpakId, "config", "duckstation")
        : env("XDG_CONFIG_HOME") is { Length: > 0 } x && Path.IsPathRooted(x)
            ? Path.Combine(x, "duckstation")
            : Path.Combine(Home, ".local", "share", "duckstation");

    public string ConfigFile => Path.Combine(DataRoot, "settings.ini");

    public string LogFile => Path.Combine(DataRoot, "duckstation.log");

    private string Folder(string section, string key, string fallback)
    {
        var value = Pcsx2Ini.Load(ConfigFile).Get(section, key) is { Length: > 0 } v ? v : fallback;
        return Path.IsPathRooted(value) ? value : Path.Combine(DataRoot, value);
    }

    public string BiosDir => Folder("BIOS", "SearchDirectory", "bios");
    public string MemoryCardsDir => Folder("MemoryCards", "Directory", "memcards");
    public string SaveStatesDir => Folder("Folders", "SaveStates", "savestates");
    public string CheatsDir => Folder("Folders", "Cheats", "cheats");
    public string PatchesDir => Folder("Folders", "Patches", "patches");
    public string GameSettingsDir => Folder("Folders", "GameSettings", "gamesettings");

    /// <summary>A game's own settings: <c>gamesettings/&lt;SERIAL&gt;.ini</c> (System::GetGameSettingsPath).</summary>
    public string GameSettingsFile(string serial) => Path.Combine(GameSettingsDir, Sanitize(serial) + ".ini");

    /// <summary>The player's cheats for a game: <c>cheats/&lt;SERIAL&gt;.cht</c> (Cheats::GetChtFilename without a hash).</summary>
    public string CheatsFile(string serial) => Path.Combine(CheatsDir, serial + ".cht");

    internal static string Sanitize(string s) => new(s.Select(c => c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c).ToArray());

    /// <summary>
    /// settings.ini for writing. A new file gets no setup wizard (<c>[Main] SetupWizardIncomplete = false</c>):
    /// Scanline covers the BIOS and controller.
    /// </summary>
    public Pcsx2Ini LoadConfigForWrite()
    {
        var ini = Pcsx2Ini.Load(ConfigFile);
        if (ini.Get("Main", "SetupWizardIncomplete") == null && ini.Get("Main", "SettingsVersion") == null)
        {
            ini.Set("Main", "SetupWizardIncomplete", "false");
        }
        return ini;
    }
}
