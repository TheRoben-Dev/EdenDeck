using System.Net;
using EdenDeck.Backends.Pcsx2;

namespace EdenDeck.Backends.Duckstation;

/// <summary>
/// A PS1 game's patches and cheats, switched the way DuckStation does it (core/cheats.cpp): named
/// codes in .cht files, enabled per game by <c>[Patches] Enable = &lt;name&gt;</c> and
/// <c>[Cheats] Enable = &lt;name&gt;</c> (with <c>[Cheats] EnableCheats = true</c>) in the game's own
/// settings file, so DuckStation's Game Properties show the same switches.
/// <para>
/// Patches (widescreen, 60 fps, fixes) and database cheats are DuckStation's own community
/// database, which DuckStation already carries inside itself; Scanline fetches the game's files from
/// the same public repository only to show their names. The player's own cheats are .cht files
/// imported into DuckStation's cheats folder. All need the game's serial.
/// </para>
/// </summary>
public sealed class DuckstationCheats(Func<DuckstationPaths> paths, string dataDir, HttpClient http)
{
    public const string Credit = "Patches and cheats from DuckStation's community database (github.com/duckstation/chtdb). All credit goes to each code's author.";
    public const string Source = "https://raw.githubusercontent.com/duckstation/chtdb/master/";

    /// <param name="AspectRatio">The picture shape a widescreen patch sets ("16:9"), which DuckStation switches on by itself.</param>
    public sealed record Code(string Name, string? Description, string? Author, string? AspectRatio = null);

    /// <summary>The named codes in a .cht: <c>[Name]</c> headers, their metadata and at least one code line.</summary>
    public static IReadOnlyList<Code> Parse(string cht)
    {
        var codes = new List<Code>();
        string? name = null, description = null, author = null, aspect = null;
        var hasBody = false;
        void Flush()
        {
            if (name != null && hasBody)
            {
                codes.RemoveAll(c => c.Name == name);  // a later code with the same name wins, as in DuckStation
                codes.Add(new Code(name, description, author, aspect));
            }
        }
        foreach (var raw in cht.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or ';')
            {
                continue;
            }
            if (line[0] == '[')
            {
                if (line.Length >= 3 && line[^1] == ']')
                {
                    Flush();
                    (name, description, author, aspect, hasBody) = (line[1..^1].Trim(), null, null, null, false);
                    if (name.Length == 0)
                    {
                        name = null;
                    }
                }
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq > 0)
            {
                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();
                switch (key)
                {
                    case "Description":
                        description ??= value;
                        break;
                    case "Author":
                        author ??= value;
                        break;
                    case "OverrideAspectRatio":
                        aspect = value;
                        break;
                }
                continue;
            }
            hasBody = true;
        }
        Flush();
        return codes;
    }

    private string CacheFile(string kind, string serial) => Path.Combine(dataDir, "duckstation", "chtdb", kind, DuckstationPaths.Sanitize(serial) + ".cht");

    public bool HasLists(string serial) => File.Exists(CacheFile("patches", serial)) && File.Exists(CacheFile("cheats", serial));

    /// <summary>Gets the game's patch and cheat files from DuckStation's database (empty ones when there are none). True when either has codes.</summary>
    public async Task<bool> FetchAsync(string serial, CancellationToken ct = default)
    {
        var found = false;
        foreach (var kind in new[] { "patches", "cheats" })
        {
            string text;
            using (var resp = await http.GetAsync($"{Source}{kind}/{Uri.EscapeDataString(serial)}.cht", ct))
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
            var file = CacheFile(kind, serial);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, text, ct);
            found |= Parse(text).Count > 0;
        }
        return found;
    }

    private IReadOnlyList<Code> Cached(string kind, string serial) =>
        File.Exists(CacheFile(kind, serial)) ? Parse(File.ReadAllText(CacheFile(kind, serial))) : [];

    /// <summary>The database's patches for the game, plus any in DuckStation's own patches folder.</summary>
    public IReadOnlyList<Code> Patches(string serial) =>
        Cached("patches", serial).Concat(FilesIn(paths().PatchesDir, serial).SelectMany(f => Parse(File.ReadAllText(f)))).DistinctBy(c => c.Name).ToList();

    /// <summary>The database's cheats for the game.</summary>
    public IReadOnlyList<Code> DatabaseCheats(string serial) => Cached("cheats", serial);

    private static IReadOnlyList<string> FilesIn(string dir, string serial) =>
        Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, serial + "*.cht")
                .Where(f => Path.GetFileNameWithoutExtension(f) is var n && (n == serial || n.StartsWith(serial + "_", StringComparison.Ordinal)))
                .Order(StringComparer.Ordinal).ToList()
            : [];

    /// <summary>The player's cheat files for this game in DuckStation's cheats folder (SERIAL.cht, SERIAL_HASH.cht).</summary>
    public IReadOnlyList<string> CheatFiles(string serial) => FilesIn(paths().CheatsDir, serial);

    /// <summary>The player's own cheats for the game.</summary>
    public IReadOnlyList<Code> Cheats(string serial) =>
        CheatFiles(serial).SelectMany(f => Parse(File.ReadAllText(f))).DistinctBy(c => c.Name).ToList();

    /// <summary>Copies the player's own .cht into DuckStation's cheats folder for this game. Returns how many named cheats it has.</summary>
    public int ImportCheats(string serial, string cht)
    {
        var groups = Parse(File.ReadAllText(cht));
        if (groups.Count == 0)
        {
            throw new InvalidDataException("That file has no named cheats ([Name] sections with code lines). Choose a DuckStation .cht cheat file.");
        }
        var target = paths().CheatsFile(serial);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (Path.GetFullPath(target) != Path.GetFullPath(cht))
        {
            if (File.Exists(target))
            {
                File.Copy(target, target + ".bak", overwrite: true);
            }
            File.Copy(cht, target, overwrite: true);
        }
        return groups.Count;
    }

    // ---- switches, in the game's own settings file ----

    private (Pcsx2Ini Global, Pcsx2Ini Game, string File) Load(string serial)
    {
        var p = paths();
        var file = p.GameSettingsFile(serial);
        return (Pcsx2Ini.Load(p.ConfigFile), Pcsx2Ini.Load(file), file);
    }

    private static string? Effective(Pcsx2Ini global, Pcsx2Ini game, string section, string key) => game.Get(section, key) ?? global.Get(section, key);

    /// <summary>
    /// A widescreen patch DuckStation switches on by itself: widescreen rendering is on and the
    /// picture shape is the patch's (Cheats::GetWantedAspectRatio).
    /// </summary>
    private static bool AutoOn(Pcsx2Ini global, Pcsx2Ini game, Code code) =>
        code.AspectRatio is { Length: > 0 } ar &&
        Effective(global, game, "GPU", "WidescreenHack") is { } w && w.Equals("true", StringComparison.OrdinalIgnoreCase) &&
        Effective(global, game, "Display", "AspectRatio") is { } shape && shape != "4:3" && shape == ar;

    public bool IsPatchOn(string serial, Code code)
    {
        var (global, game, _) = Load(serial);
        return game.GetAll("Patches", "Enable").Contains(code.Name) || AutoOn(global, game, code);
    }

    /// <summary>True when the patch is on because of the widescreen settings (and can only be switched off there).</summary>
    public bool IsAutomatic(string serial, Code code)
    {
        var (global, game, _) = Load(serial);
        return AutoOn(global, game, code);
    }

    public void SetPatch(string serial, Code code, bool on)
    {
        var (global, game, file) = Load(serial);
        if (!on && AutoOn(global, game, code))
        {
            throw new InvalidOperationException("DuckStation switches this widescreen patch on by itself while widescreen rendering is on with a 16:9 picture. Turn widescreen rendering off in this game's settings to play without it.");
        }
        var enable = game.GetAll("Patches", "Enable").Where(n => n != code.Name).ToList();
        if (on)
        {
            enable.Add(code.Name);
        }
        game.SetAll("Patches", "Enable", enable);
        Save(game, file);
    }

    public bool IsCheatOn(string serial, string name)
    {
        var (_, game, _) = Load(serial);
        // Cheats are only ever switched on in the game's own file (Cheats::AreCheatsEnabled).
        return game.Get("Cheats", "EnableCheats") is { } v && v.Equals("true", StringComparison.OrdinalIgnoreCase) &&
               game.GetAll("Cheats", "Enable").Contains(name);
    }

    public void SetCheat(string serial, string name, bool on)
    {
        var (_, game, file) = Load(serial);
        var enable = game.GetAll("Cheats", "Enable").Where(n => n != name).ToList();
        if (on)
        {
            enable.Add(name);
        }
        game.SetAll("Cheats", "Enable", enable);
        if (enable.Count > 0)
        {
            game.Set("Cheats", "EnableCheats", "true");
        }
        else
        {
            game.Remove("Cheats", "EnableCheats");
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
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            game.Save(file);
        }
    }

    /// <summary>Patches and cheats switched on for the game (for its card).</summary>
    public (int Patches, int Cheats) EnabledCount(string serial)
    {
        var cheats = DatabaseCheats(serial).Concat(Cheats(serial)).Select(c => c.Name).Distinct();
        return (Patches(serial).Count(c => IsPatchOn(serial, c)), cheats.Count(n => IsCheatOn(serial, n)));
    }
}
