using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EdenDeck.Core.Library;

namespace EdenDeck.Backends.Mgba;

/// <summary>
/// "Mods" for GBA/GB games are ROM patches (IPS, UPS, BPS) applied by mGBA at load time
/// (<c>-p</c>), and cheats are a libretro-format .cht file mGBA reads natively (<c>-c</c>).
/// Patches are the player's own files, kept in &lt;data&gt;/mgba/patches/&lt;id&gt;/; one is
/// active per game. Nothing is ever applied to the ROM file itself.
/// </summary>
public sealed partial class MgbaMods(string dataDir)
{
    public static readonly string[] PatchExtensions = [".ips", ".ups", ".bps"];

    private string PatchDir(string titleId) => Path.Combine(dataDir, "mgba", "patches", Safe(titleId));
    private string StateFile => Path.Combine(dataDir, "mgba", "patches.json");

    public IReadOnlyList<string> Patches(string titleId)
    {
        var dir = PatchDir(titleId);
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir).Where(f => PatchExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())).Order(StringComparer.OrdinalIgnoreCase).ToList()
            : [];
    }

    /// <summary>The patch mGBA applies when the game starts, or null.</summary>
    public string? ActivePatch(string titleId)
    {
        var name = LoadState().GetValueOrDefault(titleId);
        return name == null ? null : Patches(titleId).FirstOrDefault(p => Path.GetFileName(p) == name);
    }

    public void SetActivePatch(string titleId, string? path)
    {
        var state = LoadState();
        if (path == null)
        {
            state.Remove(titleId);
        }
        else
        {
            if (!Patches(titleId).Contains(path))
            {
                throw new FileNotFoundException("That patch isn't installed for this game.", path);
            }
            state[titleId] = Path.GetFileName(path);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
        File.WriteAllText(StateFile, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Copies the player's patch file in (checking it really is IPS/UPS/BPS) and makes it the active one.</summary>
    public string InstallPatch(string titleId, string file)
    {
        var ext = Path.GetExtension(file).ToLowerInvariant();
        if (!PatchExtensions.Contains(ext))
        {
            throw new InvalidDataException("Choose an .ips, .ups or .bps patch file.");
        }
        var magic = new byte[5];
        using (var f = File.OpenRead(file))
        {
            f.ReadExactly(magic, 0, Math.Min(5, (int)f.Length));
        }
        var head = Encoding.ASCII.GetString(magic);
        var ok = ext switch { ".ips" => head == "PATCH", ".ups" => head.StartsWith("UPS1"), _ => head.StartsWith("BPS1") };
        if (!ok)
        {
            throw new InvalidDataException($"{Path.GetFileName(file)} isn't a valid {ext[1..].ToUpperInvariant()} patch.");
        }
        var dir = PatchDir(titleId);
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, Path.GetFileName(file));
        File.Copy(file, target, overwrite: true);
        SetActivePatch(titleId, target);
        return target;
    }

    public void RemovePatch(string titleId, string path)
    {
        if (ActivePatch(titleId) == path)
        {
            SetActivePatch(titleId, null);
        }
        File.Delete(path);
    }

    private Dictionary<string, string> LoadState()
    {
        try
        {
            return File.Exists(StateFile) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(StateFile)) ?? new() : new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    // ---- cheats ----

    public sealed record Cheat(string Name, string Code, bool Enabled);

    public string CheatFile(string titleId) => Path.Combine(dataDir, "mgba", "cheats", Safe(titleId) + ".cht");

    public IReadOnlyList<Cheat> Cheats(string titleId) =>
        File.Exists(CheatFile(titleId)) ? ParseCht(File.ReadAllText(CheatFile(titleId))) : [];

    public void SaveCheats(string titleId, IReadOnlyList<Cheat> cheats)
    {
        var f = CheatFile(titleId);
        if (cheats.Count == 0)
        {
            File.Delete(f);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, FormatCht(cheats));
    }

    /// <summary>The file to pass to mGBA with -c when any cheat is on (written by <see cref="WriteLaunchCheats"/>).</summary>
    public string? LaunchCheatFile(string titleId) =>
        Cheats(titleId).Any(c => c.Enabled) ? Path.Combine(dataDir, "mgba", "cheats", Safe(titleId) + ".on.cht") : null;

    /// <summary>Just the cheats that are on, for mGBA (which turns on everything it's given).</summary>
    public void WriteLaunchCheats(string titleId)
    {
        if (LaunchCheatFile(titleId) is { } f)
        {
            File.WriteAllText(f, FormatCht(Cheats(titleId).Where(c => c.Enabled).Select(c => c with { Enabled = true }).ToList()));
        }
    }

    /// <summary>libretro .cht: cheats = N, then cheatI_desc / cheatI_code / cheatI_enable.</summary>
    public static List<Cheat> ParseCht(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var eq = line.IndexOf('=');
            if (eq > 0)
            {
                var v = line[(eq + 1)..].Trim();
                if (v.Length >= 2 && v[0] == '"' && v[^1] == '"')
                {
                    v = v[1..^1].Replace("\\\"", "\"");
                }
                values[line[..eq].Trim()] = v;
            }
        }
        var count = values.TryGetValue("cheats", out var n) && int.TryParse(n, out var c) ? c : 0;
        var list = new List<Cheat>();
        for (var i = 0; i < count; i++)
        {
            if (values.TryGetValue($"cheat{i}_code", out var code) && code.Length > 0)
            {
                list.Add(new Cheat(values.GetValueOrDefault($"cheat{i}_desc") ?? $"Cheat {i + 1}", code,
                    values.GetValueOrDefault($"cheat{i}_enable") == "true"));
            }
        }
        return list;
    }

    public static string FormatCht(IReadOnlyList<Cheat> cheats)
    {
        var sb = new StringBuilder($"cheats = {cheats.Count}\n");
        for (var i = 0; i < cheats.Count; i++)
        {
            sb.Append($"\ncheat{i}_desc = \"{cheats[i].Name.Replace("\"", "'")}\"\ncheat{i}_code = \"{cheats[i].Code.Replace("\"", "")}\"\ncheat{i}_enable = {(cheats[i].Enabled ? "true" : "false")}\n");
        }
        return sb.ToString();
    }

    private static string Safe(string s) => new(s.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
}

/// <summary>
/// GBA / GB / GBC cheats from the libretro cheat database (CC BY-SA 4.0), downloaded only when
/// the player asks, matched to the ROM by its file name (No-Intro naming).
/// </summary>
public sealed partial class MgbaCheatDatabase(HttpClient http)
{
    public const string Credit = "Cheats from the libretro cheat database (libretro-database, CC BY-SA 4.0). All credit goes to each cheat's author.";
    private const string Api = "https://api.github.com/repos/libretro/libretro-database/contents/cht/";
    private const string Raw = "https://raw.githubusercontent.com/libretro/libretro-database/master/cht/";

    public sealed record Source(string Name, string Url);

    public static string Folder(string system) => system switch
    {
        "Game Boy Color" => "Nintendo - Game Boy Color",
        "Game Boy" => "Nintendo - Game Boy",
        _ => "Nintendo - Game Boy Advance",
    };

    /// <summary>The cheat files that fit this ROM (one per cheat device, e.g. Code Breaker, GameShark).</summary>
    public async Task<IReadOnlyList<Source>> Find(GameEntry game, string system, CancellationToken ct = default)
    {
        var folder = Folder(system);
        using var req = new HttpRequestMessage(HttpMethod.Get, Api + Uri.EscapeDataString(folder));
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Scanline", "1.0"));
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var names = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("name").GetString()!).Where(n => n.EndsWith(".cht", StringComparison.OrdinalIgnoreCase));
        return Match(names, Path.GetFileNameWithoutExtension(game.Path), game.Title)
            .Select(n => new Source(Device(n) ?? "Cheats", Raw + Uri.EscapeDataString(folder) + "/" + Uri.EscapeDataString(n)))
            .ToList();
    }

    public async Task<List<MgbaMods.Cheat>> Download(Source source, CancellationToken ct = default) =>
        MgbaMods.ParseCht(await http.GetStringAsync(source.Url, ct)).Select(c => c with { Enabled = false }).ToList();

    /// <summary>Files whose name, minus its "(Code Breaker)"-style device tag, matches the ROM's file name; else its clean title.</summary>
    public static IEnumerable<string> Match(IEnumerable<string> files, string romStem, string title)
    {
        var list = files.ToList();
        var exact = list.Where(f => Norm(StripDevice(Path.GetFileNameWithoutExtension(f))) == Norm(romStem)).ToList();
        if (exact.Count > 0)
        {
            return exact;
        }
        var t = Norm(title);
        return list.Where(f => Norm(MgbaRom.CleanTitle(StripDevice(Path.GetFileNameWithoutExtension(f)) + ".x")) == t);
    }

    private static readonly string[] Devices = ["Code Breaker", "CodeBreaker", "GameShark", "Gameshark", "Action Replay", "Game Genie", "Xploder"];

    private static string? Device(string file)
    {
        var m = DeviceTag().Match(Path.GetFileNameWithoutExtension(file));
        return m.Success && Devices.Any(d => d.Equals(m.Groups[1].Value, StringComparison.OrdinalIgnoreCase)) ? m.Groups[1].Value : null;
    }

    private static string StripDevice(string name) => Device(name + ".cht") != null ? DeviceTag().Replace(name, "") : name;

    private static string Norm(string s) => new(s.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());

    [GeneratedRegex(@"\s*\(([^()]+)\)$")]
    private static partial Regex DeviceTag();
}
