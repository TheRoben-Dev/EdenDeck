using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EdenDeck.Backends.Rpcs3;

/// <summary>
/// RPCS3's game patches (60 fps, resolution, fixes), the PS3 side of mods and cheats. The patch
/// list is RPCS3's own community patch.yml, downloaded only when the player asks, from the same
/// service RPCS3's patch manager uses (checksum-verified), into RPCS3's patches/ folder. Turning a
/// patch on writes RPCS3's own patch_config.yml (hash → patch → title → serial → version →
/// Enabled), so RPCS3's patch manager shows the same switches. Formats: Utilities/bin_patch.cpp.
/// </summary>
public sealed class Rpcs3Patches(Func<Rpcs3Paths> paths, HttpClient http)
{
    public const string EngineVersion = "1.2";
    public const string Feed = "https://rpcs3.net/compatibility?patch&api=v1&v=" + EngineVersion;
    public const string Credit = "Patches from RPCS3's community patch list. All credit goes to each patch's author.";

    public string PatchFile => Path.Combine(paths().ConfigDir, "patches", "patch.yml");
    public string ConfigFile => Path.Combine(paths().ConfigDir, "patch_config.yml");

    /// <summary>One patch for one game, across every executable hash and game version it lists.</summary>
    public sealed record Patch(string Name, string? Notes, string? Author, IReadOnlyList<Target> Targets);

    public sealed record Target(string Hash, string Title, string Serial, string Version);

    /// <summary>Downloads the newest patch.yml. Returns false when the one there is already current.</summary>
    public async Task<bool> UpdateAsync(CancellationToken ct = default)
    {
        var url = Feed;
        if (File.Exists(PatchFile))
        {
            url += "&sha256=" + Sha256(await File.ReadAllTextAsync(PatchFile, ct));
        }
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Scanline", "1.0"));
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var content = ParseFeed(await resp.Content.ReadAsStringAsync(ct));
        if (content == null)
        {
            return false;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(PatchFile)!);
        await File.WriteAllTextAsync(PatchFile + ".tmp", content, ct);
        File.Move(PatchFile + ".tmp", PatchFile, overwrite: true);
        return true;
    }

    /// <summary>The patch file from the service's answer, or null when ours is current. Throws on anything off.</summary>
    public static string? ParseFeed(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var code = root.TryGetProperty("return_code", out var rc) && rc.TryGetInt32(out var c) ? c : -255;
        switch (code)
        {
            case 1:
                return null;
            case 0:
                break;
            case -1:
                throw new InvalidDataException("RPCS3's patch service has no patches for this patch format.");
            case -2:
                throw new InvalidDataException("RPCS3's patch service is down for maintenance. Try again later.");
            default:
                throw new InvalidDataException($"RPCS3's patch service answered with an error ({code}).");
        }
        var version = root.TryGetProperty("version", out var v) ? v.GetString() : null;
        var hash = root.TryGetProperty("sha256", out var h) ? h.GetString() : null;
        var patch = root.TryGetProperty("patch", out var p) ? p.GetString() : null;
        if (version != EngineVersion || string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(patch))
        {
            throw new InvalidDataException("RPCS3's patch service sent an answer Scanline can't use.");
        }
        if (!hash.Equals(Sha256(patch), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The downloaded patches don't match their checksum. Try again.");
        }
        return patch;
    }

    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public bool HasPatchList => File.Exists(PatchFile);

    // ---- reading patch.yml ----

    private static int Indent(string line) => line.Length - line.TrimStart(' ').Length;

    private static string Unquote(string s)
    {
        s = s.Trim();
        return s.Length >= 2 && (s[0] == '"' && s[^1] == '"' || s[0] == '\'' && s[^1] == '\'') ? s[1..^1].Replace("\\\"", "\"") : s;
    }

    /// <summary>"key: value" with the key possibly quoted (and containing ": " inside the quotes).</summary>
    private static (string Key, string Value)? KeyValue(string line)
    {
        var t = line.Trim();
        if (t.Length == 0 || t[0] == '#' || t[0] == '-')
        {
            return null;
        }
        int colon;
        if (t[0] is '"' or '\'')
        {
            var close = t.IndexOf(t[0], 1);
            while (close > 0 && t[close - 1] == '\\')
            {
                close = t.IndexOf(t[0], close + 1);
            }
            if (close < 0 || close + 1 >= t.Length || t[close + 1] != ':')
            {
                return null;
            }
            colon = close + 1;
        }
        else
        {
            colon = t.IndexOf(':');
            while (colon >= 0 && colon + 1 < t.Length && t[colon + 1] != ' ')
            {
                colon = t.IndexOf(':', colon + 1);
            }
            if (colon < 0)
            {
                return null;
            }
        }
        return (Unquote(t[..colon]), colon + 1 < t.Length ? t[(colon + 1)..].Trim() : "");
    }

    private static IEnumerable<string> List(string value)
    {
        var v = value.Trim();
        if (v.StartsWith('[') && v.EndsWith(']'))
        {
            return v[1..^1].Split(',').Select(Unquote).Where(x => x.Length > 0);
        }
        return v.Length == 0 ? [] : [Unquote(v)];
    }

    /// <summary>The patches in patch.yml that list this game's serial.</summary>
    public IReadOnlyList<Patch> ForGame(string serial) => File.Exists(PatchFile) ? ParsePatches(File.ReadAllText(PatchFile), serial) : [];

    public static IReadOnlyList<Patch> ParsePatches(string yaml, string serial)
    {
        var byName = new Dictionary<string, (string? Notes, string? Author, List<Target> Targets)>(StringComparer.Ordinal);
        string? hash = null, name = null, title = null, section = null;
        int nameIndent = -1, sectionIndent = -1, titleIndent = -1;
        string? notes = null, author = null;
        var targets = new List<Target>();

        void Flush()
        {
            if (hash != null && name != null && targets.Count > 0)
            {
                if (!byName.TryGetValue(name, out var e))
                {
                    byName[name] = e = (notes, author, []);
                }
                e.Targets.AddRange(targets);
                byName[name] = (e.Notes ?? notes, e.Author ?? author, e.Targets);
            }
            targets = [];
            notes = author = null;
        }

        foreach (var raw in yaml.Replace("\r\n", "\n").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith('#'))
            {
                continue;
            }
            var indent = Indent(raw);
            var kv = KeyValue(raw);
            if (indent == 0)
            {
                Flush();
                name = null;
                hash = kv is { } top && (top.Key.StartsWith("PPU-", StringComparison.Ordinal) || top.Key.StartsWith("SPU-", StringComparison.Ordinal)) && top.Value.Length == 0 ? top.Key : null;
                nameIndent = -1;
                continue;
            }
            if (hash == null || kv is not { } p)
            {
                continue;
            }
            if (nameIndent < 0 || indent <= nameIndent)
            {
                Flush();
                name = p.Key;
                nameIndent = indent;
                section = null;
                continue;
            }
            if (section == null || indent <= sectionIndent)
            {
                section = p.Key;
                sectionIndent = indent;
                title = null;
                if (section == "Notes")
                {
                    notes = Unquote(p.Value);
                }
                else if (section == "Author")
                {
                    author = Unquote(p.Value);
                }
                continue;
            }
            if (section != "Games")
            {
                continue;
            }
            if (title == null || indent <= titleIndent)
            {
                title = p.Key;
                titleIndent = indent;
                continue;
            }
            if (p.Key.Equals(serial, StringComparison.OrdinalIgnoreCase))
            {
                targets.AddRange(List(p.Value).Select(ver => new Target(hash, title, p.Key, ver)));
            }
        }
        Flush();
        return byName.Select(kv => new Patch(kv.Key, kv.Value.Notes, kv.Value.Author, kv.Value.Targets)).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ---- patch_config.yml ----

    /// <summary>One game version's entry: on or off, plus anything else RPCS3 stored there (its configurable values), kept as is.</summary>
    private sealed class Entry
    {
        public bool Enabled;
        public List<string> Extra = [];
    }

    /// <summary>hash → patch → title → serial → version → entry, as RPCS3 stores it.</summary>
    private sealed class Config : SortedDictionary<string, SortedDictionary<string, SortedDictionary<string, SortedDictionary<string, SortedDictionary<string, Entry>>>>>
    {
        public Config() : base(StringComparer.Ordinal)
        {
        }
    }

    private Config LoadConfig()
    {
        var config = new Config();
        if (!File.Exists(ConfigFile))
        {
            return config;
        }
        var keys = new string?[5];
        foreach (var raw in File.ReadAllText(ConfigFile).Replace("\r\n", "\n").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }
            var level = Indent(raw) / 2;
            if (level < 5)
            {
                if (KeyValue(raw) is not { } kv)
                {
                    continue;
                }
                keys[level] = kv.Key;
                for (var i = level + 1; i < 5; i++)
                {
                    keys[i] = null;
                }
                if (level == 4)
                {
                    Get(config, keys[0]!, keys[1]!, keys[2]!, keys[3]!, keys[4]!);
                }
            }
            else if (keys.All(k => k != null))
            {
                var entry = Get(config, keys[0]!, keys[1]!, keys[2]!, keys[3]!, keys[4]!);
                if (level == 5 && KeyValue(raw) is { Key: "Enabled" } en)
                {
                    entry.Enabled = en.Value.Trim() == "true";
                }
                else
                {
                    entry.Extra.Add(raw);
                }
            }
        }
        return config;
    }

    private static Entry Get(Config c, string hash, string name, string title, string serial, string version)
    {
        var versions = Ensure(c, hash, name, title, serial);
        if (!versions.TryGetValue(version, out var e))
        {
            versions[version] = e = new Entry();
        }
        return e;
    }

    private static SortedDictionary<string, Entry> Ensure(Config c, string hash, string name, string title, string serial)
    {
        if (!c.TryGetValue(hash, out var a))
        {
            c[hash] = a = new(StringComparer.Ordinal);
        }
        if (!a.TryGetValue(name, out var b))
        {
            a[name] = b = new(StringComparer.Ordinal);
        }
        if (!b.TryGetValue(title, out var d))
        {
            b[title] = d = new(StringComparer.Ordinal);
        }
        if (!d.TryGetValue(serial, out var e))
        {
            d[serial] = e = new(StringComparer.Ordinal);
        }
        return e;
    }

    private static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private void SaveConfig(Config config)
    {
        var sb = new StringBuilder();
        foreach (var (hash, names) in config)
        {
            var hashLines = new StringBuilder();
            foreach (var (name, titles) in names)
            {
                var nameLines = new StringBuilder();
                foreach (var (title, serials) in titles)
                {
                    var titleLines = new StringBuilder();
                    foreach (var (serial, versions) in serials)
                    {
                        var kept = versions.Where(v => v.Value.Enabled || v.Value.Extra.Count > 0).ToList();
                        if (kept.Count == 0)
                        {
                            continue;
                        }
                        titleLines.Append($"      {Q(serial)}:\n");
                        foreach (var (ver, entry) in kept)
                        {
                            titleLines.Append($"        {Q(ver)}:\n");
                            if (entry.Enabled)
                            {
                                titleLines.Append("          Enabled: true\n");
                            }
                            foreach (var line in entry.Extra)
                            {
                                titleLines.Append(line).Append('\n');
                            }
                        }
                    }
                    if (titleLines.Length > 0)
                    {
                        nameLines.Append($"    {Q(title)}:\n").Append(titleLines);
                    }
                }
                if (nameLines.Length > 0)
                {
                    hashLines.Append($"  {Q(name)}:\n").Append(nameLines);
                }
            }
            if (hashLines.Length > 0)
            {
                sb.Append($"{hash}:\n").Append(hashLines);
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile)!);
        File.WriteAllText(ConfigFile + ".tmp", sb.ToString());
        File.Move(ConfigFile + ".tmp", ConfigFile, overwrite: true);
    }

    public bool IsEnabled(Patch patch)
    {
        var config = LoadConfig();
        return patch.Targets.Any(t => config.TryGetValue(t.Hash, out var a) && a.TryGetValue(patch.Name, out var b) && b.TryGetValue(t.Title, out var c) &&
                                      c.TryGetValue(t.Serial, out var d) && d.TryGetValue(t.Version, out var e) && e.Enabled);
    }

    /// <summary>Switches a patch on or off for every executable and game version it lists.</summary>
    public void SetEnabled(Patch patch, bool on)
    {
        var config = LoadConfig();
        foreach (var t in patch.Targets)
        {
            Get(config, t.Hash, patch.Name, t.Title, t.Serial, t.Version).Enabled = on;
        }
        SaveConfig(config);
    }

    public int EnabledCount(string serial) => ForGame(serial).Count(IsEnabled);
}
