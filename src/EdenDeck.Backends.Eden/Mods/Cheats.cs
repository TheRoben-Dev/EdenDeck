using System.Globalization;
using System.Text;
using System.Text.Json;

namespace EdenDeck.Backends.Eden.Mods;

/// <summary>One cheat in an Atmosphère-style cheat file: <c>[Name]</c> then code lines. <c>{Name}</c> is the master code, always on.</summary>
public sealed record Cheat(string Name, string Code, bool IsMaster = false)
{
    public string ToText() => (IsMaster ? "{" + Name + "}" : "[" + Name + "]") + "\n" + Code.TrimEnd() + "\n";
}

/// <summary>Atmosphère cheat text, the format Eden reads from <c>&lt;mod&gt;/cheats/&lt;BUILDID&gt;.txt</c>.</summary>
public static class CheatText
{
    public static List<Cheat> Parse(string text)
    {
        var result = new List<Cheat>();
        string? name = null;
        var master = false;
        var code = new StringBuilder();
        void Flush()
        {
            if (name != null)
            {
                result.Add(new Cheat(name, code.ToString().Trim(), master));
            }
            code.Clear();
        }
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length >= 2 && (line[0] == '[' && line[^1] == ']' || line[0] == '{' && line[^1] == '}'))
            {
                Flush();
                master = line[0] == '{';
                name = line[1..^1].Trim();
                continue;
            }
            if (name != null && line.Length > 0)
            {
                code.Append(line).Append('\n');
            }
        }
        Flush();
        return result;
    }

    public static string Format(IEnumerable<Cheat> cheats) => string.Join("\n", cheats.Select(c => c.ToText()));

    /// <summary>A cheat has codes (some database entries are just section titles like "[--- Items ---]").</summary>
    public static bool HasCode(Cheat c) => c.Code.Split('\n').Any(l => l.Length > 0 && char.IsAsciiHexDigit(l[0]));
}

/// <summary>Cheats for one game from the database: every known build, and which game versions run which build.</summary>
public sealed record GameCheats(
    string TitleId,
    IReadOnlyDictionary<string, IReadOnlyList<Cheat>> ByBuild,
    IReadOnlyDictionary<uint, string> BuildByVersion)
{
    /// <summary>The build for a game version (0 = no update), or null if the database doesn't know it.</summary>
    public string? BuildFor(uint version) => BuildByVersion.GetValueOrDefault(version);

    public IReadOnlyList<Cheat> For(string? buildId) =>
        buildId != null && ByBuild.TryGetValue(buildId, out var list) ? list : [];

    /// <summary>Game versions with cheats, as the player sees them ("1.0.0" style is unknown here, so update numbers).</summary>
    public IReadOnlyList<uint> CoveredVersions => BuildByVersion.Where(kv => ByBuild.ContainsKey(kv.Value)).Select(kv => kv.Key).Order().ToList();
}

/// <summary>
/// The community cheat database (github.com/HamletDuFromage/switch-cheats-db, mirrored from the
/// GBAtemp cheat collection and NX-60FPS-RES-GFX-Cheats; credit goes to each cheat's author).
/// Fetched only when the player asks, one game at a time: cheats/&lt;TITLEID&gt;.json maps build ids
/// to cheats, versions/&lt;TITLEID&gt;.json maps game versions to build ids. Nothing is bundled.
/// </summary>
public sealed class CheatDatabase(HttpClient http, string baseUrl = CheatDatabase.DefaultBaseUrl)
{
    public const string DefaultBaseUrl = "https://raw.githubusercontent.com/HamletDuFromage/switch-cheats-db/master";
    public const string Credit = "Cheats from the community cheat database (switch-cheats-db by HamletDuFromage, mirrored from GBAtemp and NX-60FPS-RES-GFX-Cheats). All credit goes to each cheat's author.";

    /// <summary>The game's cheats, or null if the database has none for it.</summary>
    public async Task<GameCheats?> Fetch(string titleId, CancellationToken ct = default)
    {
        var tid = titleId.ToUpperInvariant();
        var cheatsJson = await Get($"{baseUrl}/cheats/{tid}.json", ct);
        if (cheatsJson == null)
        {
            return null;
        }
        var versionsJson = await Get($"{baseUrl}/versions/{tid}.json", ct);
        return Parse(tid, cheatsJson, versionsJson);
    }

    public static GameCheats Parse(string titleId, string cheatsJson, string? versionsJson)
    {
        var byBuild = new Dictionary<string, IReadOnlyList<Cheat>>(StringComparer.OrdinalIgnoreCase);
        using (var doc = JsonDocument.Parse(cheatsJson))
        {
            foreach (var build in doc.RootElement.EnumerateObject())
            {
                if (build.Value.ValueKind != JsonValueKind.Object || build.Name.Length < 16)
                {
                    continue;
                }
                var cheats = new List<Cheat>();
                foreach (var c in build.Value.EnumerateObject())
                {
                    if (c.Value.ValueKind == JsonValueKind.String)
                    {
                        cheats.AddRange(CheatText.Parse(c.Value.GetString()!).Where(CheatText.HasCode));
                    }
                }
                if (cheats.Count > 0)
                {
                    byBuild[build.Name[..16].ToUpperInvariant()] = cheats;
                }
            }
        }
        var byVersion = new Dictionary<uint, string>();
        if (versionsJson != null)
        {
            using var doc = JsonDocument.Parse(versionsJson);
            foreach (var v in doc.RootElement.EnumerateObject())
            {
                if (uint.TryParse(v.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ver) && v.Value.ValueKind == JsonValueKind.String &&
                    v.Value.GetString() is { Length: >= 16 } b)
                {
                    byVersion[ver] = b[..16].ToUpperInvariant();
                }
            }
        }
        return new GameCheats(titleId, byBuild, byVersion);
    }

    private async Task<string?> Get(string url, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }
}

/// <summary>
/// The cheats the player picked, kept in one mod folder of Scanline's own
/// (<c>&lt;load&gt;/&lt;TITLEID&gt;/Cheats (Scanline)/cheats/&lt;BUILDID&gt;.txt</c>), so Eden lists them as
/// one add-on that can be switched off like any mod. The file holds only the picked cheats.
/// </summary>
public static class ScanlineCheats
{
    public const string ModName = "Cheats (Scanline)";

    public static string FileFor(EdenPaths paths, string titleId, string buildId) =>
        Path.Combine(paths.ModDir(titleId), ModName, "cheats", buildId.ToUpperInvariant() + ".txt");

    /// <summary>The picked cheats for a build (empty if none).</summary>
    public static List<Cheat> Read(EdenPaths paths, string titleId, string buildId)
    {
        var f = FileFor(paths, titleId, buildId);
        return File.Exists(f) ? CheatText.Parse(File.ReadAllText(f)) : [];
    }

    /// <summary>Saves the picked cheats for a build; no cheats removes the file (and the folder once empty).</summary>
    public static void Write(EdenPaths paths, string titleId, string buildId, IReadOnlyCollection<Cheat> cheats)
    {
        var f = FileFor(paths, titleId, buildId);
        if (cheats.Count == 0)
        {
            if (File.Exists(f))
            {
                File.Delete(f);
            }
            var mod = Path.Combine(paths.ModDir(titleId), ModName);
            if (Directory.Exists(mod) && !Directory.EnumerateFiles(mod, "*", SearchOption.AllDirectories).Any())
            {
                Directory.Delete(mod, recursive: true);
            }
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f + ".tmp", CheatText.Format(cheats));
        File.Move(f + ".tmp", f, overwrite: true);
    }

    /// <summary>Eden's "Enable cheats by default" ([Core] enable_cheats_by_default, on unless the player turned it off).</summary>
    public static bool EnabledByDefault(string configFile)
    {
        var v = Config.QtIni.Load(configFile).Get("Core", "enable_cheats_by_default");
        return v is null || !v.Equals("false", StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabledByDefault(string configFile, bool on)
    {
        var ini = Config.QtIni.Load(configFile);
        ini.Set("Core", "enable_cheats_by_default\\default", "false");
        ini.Set("Core", "enable_cheats_by_default", on ? "true" : "false");
        ini.Save(configFile);
    }
}
