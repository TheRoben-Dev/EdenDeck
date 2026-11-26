using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EdenDeck.Core.Library;

namespace EdenDeck.Backends.MelonDs;

/// <summary>
/// A DS game's cheats. melonDS keeps them in an Action Replay list next to the game
/// (<c>&lt;rom name&gt;.mch</c>, or in its CheatFilePath), each code switched on or off there, and
/// applies the ones that are on while its "cheats" setting is on. Scanline offers the codes from
/// libretro-database's DS cheat list (fetched on request; the file is named in the built-in name
/// list), adds the ones the player switches on to that file under their own category, and shows the
/// player's own codes from it too. Codes stay in melonDS's format, so its cheat window shows the same.
/// </summary>
public sealed partial class MelonDsCheats(Func<MelonDsPaths> paths, string dataDir, HttpClient http)
{
    public const string Credit = "Cheats from libretro-database's DS cheat list (github.com/libretro/libretro-database, CC BY-SA 4.0). All credit goes to each code's author.";
    public const string Source = "https://raw.githubusercontent.com/libretro/libretro-database/master/cht/Nintendo%20-%20Nintendo%20DS/";
    public const string Category = "libretro database";

    /// <summary>A code: its name, and its words as pairs (two 32-bit words per line).</summary>
    public sealed record Code(string Name, IReadOnlyList<uint> Words, string? Description = null);

    // ---- the database list ----

    /// <summary>Codes from a libretro .cht file (cheatN_desc / cheatN_code); entries without code are headings.</summary>
    public static IReadOnlyList<Code> ParseCht(string cht)
    {
        var desc = new Dictionary<int, string>();
        var code = new Dictionary<int, string>();
        foreach (Match m in ChtLine().Matches(cht))
        {
            var i = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            (m.Groups[2].Value == "desc" ? desc : code)[i] = m.Groups[3].Value;
        }
        var list = new List<Code>();
        foreach (var (i, text) in code.OrderBy(kv => kv.Key))
        {
            var words = Words(text);
            if (words == null || words.Count == 0)
            {
                continue;
            }
            var name = desc.GetValueOrDefault(i)?.Trim() is { Length: > 0 } d ? d : $"Code {i + 1}";
            // Names are unique inside a game's list, so they can be looked up again.
            var unique = name;
            for (var n = 2; list.Any(c => c.Name == unique); n++)
            {
                unique = $"{name} ({n})";
            }
            list.Add(new Code(unique, words));
        }
        return list;
    }

    /// <summary>"0200D0E0+00000063" or "0200D0E0 00000063": pairs of 8-digit hex words, or null when it isn't one.</summary>
    internal static List<uint>? Words(string text)
    {
        var tokens = text.Split(new[] { '+', ' ', '\t', '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length % 2 != 0)
        {
            return null;
        }
        var words = new List<uint>();
        foreach (var t in tokens)
        {
            if (t.Length != 8 || !uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var w))
            {
                return null;
            }
            words.Add(w);
        }
        return words;
    }

    private string CacheFile(string gameCode) => Path.Combine(dataDir, "melonds", "cheats", gameCode.ToUpperInvariant() + ".cht");

    /// <summary>Whether the database has a list for this game at all.</summary>
    public static bool InDatabase(NdsHeader header) => header.HasGameCode && DsNames.CheatFile(header.GameCode) != null;

    public bool HasList(string gameCode) => File.Exists(CacheFile(gameCode));

    /// <summary>Downloads the game's list (only the text file of codes). False when the database has none.</summary>
    public async Task<bool> FetchAsync(string gameCode, CancellationToken ct = default)
    {
        if (DsNames.CheatFile(gameCode) is not { } name)
        {
            return false;
        }
        using var resp = await http.GetAsync(Source + Uri.EscapeDataString(name) + ".cht", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        resp.EnsureSuccessStatusCode();
        var text = await resp.Content.ReadAsStringAsync(ct);
        var file = CacheFile(gameCode);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
        return true;
    }

    public IReadOnlyList<Code> DatabaseCodes(string gameCode) =>
        File.Exists(CacheFile(gameCode)) ? ParseCht(File.ReadAllText(CacheFile(gameCode))) : [];

    // ---- melonDS's .mch file ----

    /// <summary>The game's cheat file, where melonDS looks for it.</summary>
    public string MchFile(GameEntry game)
    {
        var configured = paths().Load().Get("Instance0.CheatFilePath");
        var dir = string.IsNullOrWhiteSpace(configured) ? Path.GetDirectoryName(game.Path)! : configured;
        return Path.Combine(dir, Path.GetFileNameWithoutExtension(game.Path) + ".mch");
    }

    /// <summary>Every code in the game's cheat file: the player's own and the ones Scanline added.</summary>
    public IReadOnlyList<(string Category, string Name, bool On)> FileCodes(GameEntry game) =>
        Mch.Load(MchFile(game)).Cats.SelectMany(c => c.Codes.Select(x => (c.Name, x.Name, x.On))).ToList();

    public bool IsOn(GameEntry game, string name) => Mch.Load(MchFile(game)).Find(name)?.On == true;

    /// <summary>Switches a code on or off; a database code that isn't in the file yet is added.</summary>
    public void Set(GameEntry game, string name, bool on, Code? fromDatabase = null)
    {
        var file = MchFile(game);
        var mch = Mch.Load(file);
        if (mch.Find(name) is { } existing)
        {
            existing.On = on;
        }
        else if (on && fromDatabase != null)
        {
            var cat = mch.Cats.FirstOrDefault(c => c.Name == Category);
            if (cat == null)
            {
                cat = new Mch.Cat { Header = "CAT " + Category, Name = Category };
                mch.Cats.Add(cat);
            }
            cat.Codes.Add(new Mch.Entry { Name = name, On = true, Words = [.. fromDatabase.Words] });
        }
        else
        {
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, mch.ToString());
    }

    public int EnabledCount(GameEntry game) => FileCodes(game).Count(c => c.On);

    [GeneratedRegex(@"^\s*cheat(\d+)_(desc|code)\s*=\s*""([^""]*)""", RegexOptions.Multiline)]
    private static partial Regex ChtLine();

    /// <summary>
    /// melonDS's cheat file (ARCodeFile.cpp): "CAT name" categories (newer melonDS writes
    /// "CAT 0|1 name"), each with "CODE 0|1 name" codes, optional "DESC" lines, and "XXXXXXXX YYYYYYYY"
    /// data lines. Category headers are kept as they were.
    /// </summary>
    internal sealed class Mch
    {
        public sealed class Entry
        {
            public string Name = "";
            public bool On;
            public string? Description;
            public List<uint> Words = [];
        }

        public sealed class Cat
        {
            public string Header = "";
            public string Name = "";
            public List<string> Descriptions = [];
            public List<Entry> Codes = [];
        }

        public List<Cat> Cats { get; } = [];

        public Entry? Find(string name) => Cats.SelectMany(c => c.Codes).FirstOrDefault(c => c.Name == name);

        public static Mch Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : new Mch();

        public static Mch Parse(string text)
        {
            var mch = new Mch();
            Cat? cat = null;
            Entry? code = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }
                if (line.StartsWith("ROOT", StringComparison.OrdinalIgnoreCase))
                {
                    cat = new Cat { Header = "ROOT", Name = "" };
                    mch.Cats.Add(cat);
                    code = null;
                }
                else if (line.StartsWith("CAT", StringComparison.OrdinalIgnoreCase))
                {
                    var rest = line[3..].Trim();
                    var name = rest.Length > 2 && rest[0] is '0' or '1' && rest[1] == ' ' ? rest[2..] : rest;
                    cat = new Cat { Header = line, Name = name };
                    mch.Cats.Add(cat);
                    code = null;
                }
                else if (line.StartsWith("CODE", StringComparison.OrdinalIgnoreCase))
                {
                    var rest = line[4..].Trim();
                    if (cat == null)
                    {
                        cat = new Cat { Header = "ROOT", Name = "" };
                        mch.Cats.Add(cat);
                    }
                    code = new Entry { On = rest.StartsWith('1'), Name = rest.Length > 2 ? rest[2..].Trim() : "" };
                    cat.Codes.Add(code);
                }
                else if (line.StartsWith("DESC", StringComparison.OrdinalIgnoreCase))
                {
                    var d = line[4..].Trim();
                    if (code != null)
                    {
                        code.Description = d;
                    }
                    else
                    {
                        cat?.Descriptions.Add(d);
                    }
                }
                else if (code != null && Words(line) is { } words)
                {
                    code.Words.AddRange(words);
                }
            }
            return mch;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            // melonDS reads root codes only before the first category.
            foreach (var cat in Cats.OrderBy(c => c.Header == "ROOT" ? 0 : 1))
            {
                sb.Append(cat.Header).Append('\n');
                foreach (var d in cat.Descriptions)
                {
                    sb.Append("DESC ").Append(d).Append('\n');
                }
                sb.Append('\n');
                foreach (var c in cat.Codes)
                {
                    sb.Append("CODE ").Append(c.On ? '1' : '0').Append(' ').Append(c.Name).Append('\n');
                    if (!string.IsNullOrEmpty(c.Description))
                    {
                        sb.Append("DESC ").Append(c.Description).Append('\n');
                    }
                    for (var i = 0; i + 1 < c.Words.Count; i += 2)
                    {
                        sb.Append(c.Words[i].ToString("X8", CultureInfo.InvariantCulture)).Append(' ')
                          .Append(c.Words[i + 1].ToString("X8", CultureInfo.InvariantCulture)).Append('\n');
                    }
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }
    }
}
