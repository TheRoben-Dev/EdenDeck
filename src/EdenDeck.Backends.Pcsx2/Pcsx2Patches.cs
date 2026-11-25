using System.Net;

namespace EdenDeck.Backends.Pcsx2;

/// <summary>
/// A PS2 game's patches and cheats, switched the way PCSX2 does it (pcsx2/Patch.cpp): named groups
/// in .pnach files, enabled per game by <c>[Patches] Enable = &lt;name&gt;</c> and
/// <c>[Cheats] Enable = &lt;name&gt;</c> in the game's own settings file, so PCSX2's Game Properties
/// show the same switches.
/// <para>
/// Patches (widescreen, no-interlacing, 60 fps…) are PCSX2's own community patches, which PCSX2
/// already carries inside itself; Scanline fetches the game's file from the same public repository
/// only to show their names. Cheats are the player's own .pnach files, imported into PCSX2's cheats
/// folder. Both need the game's CRC.
/// </para>
/// </summary>
public sealed class Pcsx2Patches(Func<Pcsx2Paths> paths, string dataDir, HttpClient http)
{
    public const string Credit = "Patches from PCSX2's community patch collection (github.com/PCSX2/pcsx2_patches). All credit goes to each patch's author.";
    public const string Source = "https://raw.githubusercontent.com/PCSX2/pcsx2_patches/main/patches/";

    /// <summary>Names PCSX2 switches on for every game while the global widescreen / no-interlacing options are on.</summary>
    public const string Widescreen = "Widescreen 16:9";
    public const string NoInterlacing = "No-Interlacing";

    public sealed record Group(string Name, string? Description, string? Author);

    /// <summary>The named groups in a .pnach: <c>[Name]</c> headers with <c>description=</c> and <c>author=</c>.</summary>
    public static IReadOnlyList<Group> Parse(string pnach)
    {
        var groups = new List<Group>();
        string? name = null, description = null, author = null;
        var hasPatch = false;
        void Flush()
        {
            if (name != null && hasPatch && groups.All(g => g.Name != name))
            {
                groups.Add(new Group(name, description, author));
            }
        }
        foreach (var raw in pnach.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Flush();
                (name, description, author, hasPatch) = (line[1..^1].Trim(), null, null, false);
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0 || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }
            var key = line[..eq].Trim().ToLowerInvariant();
            var value = line[(eq + 1)..].Trim();
            switch (key)
            {
                case "description" or "comment":
                    description ??= value;
                    break;
                case "author":
                    author ??= value;
                    break;
                case "patch" or "dpatch":
                    hasPatch = true;
                    break;
            }
        }
        Flush();
        return groups;
    }

    private string CacheFile(string serial, uint crc) => Path.Combine(dataDir, "pcsx2", "patches", $"{serial}_{crc:X8}.pnach");

    public bool HasPatchList(string serial, uint crc) => File.Exists(CacheFile(serial, crc));

    /// <summary>Gets the game's community patch file (an empty one when there's none). True when it has patches.</summary>
    public async Task<bool> FetchAsync(string serial, uint crc, CancellationToken ct = default)
    {
        string text;
        using (var resp = await http.GetAsync($"{Source}{Uri.EscapeDataString(serial)}_{crc:X8}.pnach", ct))
        {
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                text = "";
            }
            else
            {
                resp.EnsureSuccessStatusCode();
                text = await resp.Content.ReadAsStringAsync(ct);
            }
        }
        var file = CacheFile(serial, crc);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, text, ct);
        return Parse(text).Count > 0;
    }

    public IReadOnlyList<Group> Community(string serial, uint crc) =>
        File.Exists(CacheFile(serial, crc)) ? Parse(File.ReadAllText(CacheFile(serial, crc))) : [];

    /// <summary>The player's cheat files for this game in PCSX2's cheats folder (SERIAL_CRC*.pnach).</summary>
    public IReadOnlyList<string> CheatFiles(string serial, uint crc)
    {
        var dir = paths().CheatsDir;
        return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, $"{serial}_{crc:X8}*.pnach").Order(StringComparer.Ordinal).ToList() : [];
    }

    public IReadOnlyList<Group> Cheats(string serial, uint crc) =>
        CheatFiles(serial, crc).SelectMany(f => Parse(File.ReadAllText(f))).DistinctBy(g => g.Name).ToList();

    /// <summary>Copies the player's own .pnach into PCSX2's cheats folder for this game. Returns how many named cheats it has.</summary>
    public int ImportCheats(string serial, uint crc, string pnach)
    {
        var text = File.ReadAllText(pnach);
        var groups = Parse(text);
        if (groups.Count == 0)
        {
            throw new InvalidDataException("That file has no named cheats ([Name] sections with patch= lines). Choose a PCSX2 .pnach cheat file.");
        }
        var target = paths().CheatsFile(serial, crc);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target) && Path.GetFullPath(target) != Path.GetFullPath(pnach))
        {
            File.Copy(target, target + ".bak", overwrite: true);
        }
        if (Path.GetFullPath(target) != Path.GetFullPath(pnach))
        {
            File.Copy(pnach, target, overwrite: true);
        }
        return groups.Count;
    }

    // ---- switches, in the game's own settings file ----

    private (Pcsx2Ini Global, Pcsx2Ini Game, string File) Load(string serial, uint crc)
    {
        var p = paths();
        var file = p.GameSettingsFile(serial, crc);
        return (Pcsx2Ini.Load(p.ConfigFile), Pcsx2Ini.Load(file), file);
    }

    private static bool On(Pcsx2Ini ini, string section, string key) => ini.Get(section, key) is { } v && v.Equals("true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Patches PCSX2 switches on by itself for this game (the global widescreen / no-interlacing options).</summary>
    private static bool AutoOn(Pcsx2Ini global, string name) =>
        (name == Widescreen && On(global, "EmuCore", "EnableWideScreenPatches")) ||
        (name == NoInterlacing && On(global, "EmuCore", "EnableNoInterlacingPatches"));

    public bool IsPatchOn(string serial, uint crc, string name)
    {
        var (global, game, _) = Load(serial, crc);
        var enabled = game.GetAll("Patches", "Enable").Contains(name) || AutoOn(global, name);
        return enabled && !game.GetAll("Patches", "Disable").Contains(name);
    }

    public void SetPatch(string serial, uint crc, string name, bool on)
    {
        var (global, game, file) = Load(serial, crc);
        var enable = game.GetAll("Patches", "Enable").Where(n => n != name).ToList();
        var disable = game.GetAll("Patches", "Disable").Where(n => n != name).ToList();
        if (on && !AutoOn(global, name))
        {
            enable.Add(name);
        }
        else if (!on && AutoOn(global, name))
        {
            disable.Add(name);
        }
        game.SetAll("Patches", "Enable", enable);
        game.SetAll("Patches", "Disable", disable);
        Save(game, file);
    }

    public bool IsCheatOn(string serial, uint crc, string name)
    {
        var (global, game, _) = Load(serial, crc);
        var cheats = game.Get("EmuCore", "EnableCheats") is { } v ? v.Equals("true", StringComparison.OrdinalIgnoreCase) : On(global, "EmuCore", "EnableCheats");
        return cheats && game.GetAll("Cheats", "Enable").Contains(name);
    }

    public void SetCheat(string serial, uint crc, string name, bool on)
    {
        var (_, game, file) = Load(serial, crc);
        var enable = game.GetAll("Cheats", "Enable").Where(n => n != name).ToList();
        if (on)
        {
            enable.Add(name);
        }
        game.SetAll("Cheats", "Enable", enable);
        if (enable.Count > 0)
        {
            game.Set("EmuCore", "EnableCheats", "true");
        }
        else
        {
            game.Remove("EmuCore", "EnableCheats");
        }
        Save(game, file);
    }

    private static void Save(Pcsx2Ini game, string file)
    {
        if (game.IsEmpty)
        {
            File.Delete(file);
        }
        else
        {
            game.Save(file);
        }
    }

    /// <summary>Patches and cheats switched on for the game (for its card).</summary>
    public (int Patches, int Cheats) EnabledCount(string serial, uint crc) =>
        (Community(serial, crc).Count(g => IsPatchOn(serial, crc, g.Name)), Cheats(serial, crc).Count(g => IsCheatOn(serial, crc, g.Name)));
}
