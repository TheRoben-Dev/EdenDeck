using System.Text.Json;
using EdenDeck.Core.Backends;
using EdenDeck.Core.Config;
using EdenDeck.Core.Settings;

namespace EdenDeck.Backends.Mgba;

/// <summary>
/// mGBA's settings. Global values live in mGBA's own config.ini, section [ports.qt], where mGBA
/// writes them itself (booleans as 1/0). mGBA has no per-game config file, so per-game values are
/// kept by Scanline and passed at launch as <c>-C key=value</c> overrides, which mGBA applies for
/// that run without saving them.
/// </summary>
public sealed class MgbaSettings : ISettingsProvider
{
    public const string Section = "ports.qt";

    private readonly Func<string> _configFile;
    private readonly string _overridesFile;

    public MgbaSettings(Func<string> configFile, string overridesFile) => (_configFile, _overridesFile) = (configFile, overridesFile);

    public IReadOnlyList<OptionDefinition> Options { get; } =
    [
        new()
        {
            Id = "lockIntegerScaling", Group = "Graphics", Label = "Pixel-perfect scaling", Type = OptionType.Bool,
            Help = "Scales by whole numbers only, so every pixel is the same size. On a Steam Deck the GBA screen fills the height exactly at 5x.",
            OnDetail = "Crisp, even pixels. Small borders on screens that aren't an exact multiple.",
            OffDetail = "Fills more of the screen, but some pixels end up wider than others.",
            DefaultValue = "false",
        },
        new()
        {
            Id = "lockAspectRatio", Group = "Graphics", Label = "Keep the shape", Type = OptionType.Bool,
            Help = "Keeps the original 3:2 (GBA) or 10:9 (Game Boy) picture shape instead of stretching it.",
            OnDetail = "The picture keeps its shape, with borders at the sides.",
            OffDetail = "Stretched to fill the whole screen.",
            DefaultValue = "true",
        },
        new()
        {
            Id = "resampleVideo", Group = "Graphics", Label = "Smooth pixels", Type = OptionType.Bool,
            Help = "Blends pixel edges when scaling. Softer, less blocky.",
            OnDetail = "Soft, smoothed picture.",
            OffDetail = "Sharp, blocky pixels like the original screen.",
            DefaultValue = "false",
        },
        new()
        {
            Id = "interframeBlending", Group = "Graphics", Label = "LCD ghosting", Type = OptionType.Bool,
            Help = "Mixes each frame with the last one like the slow original LCD. Some games rely on it for transparency and flicker effects.",
            OnDetail = "Flickering effects look solid, as on the real handheld. Fast motion blurs a little.",
            OffDetail = "Sharp motion; some effects flicker.",
            DefaultValue = "false",
        },
        new()
        {
            Id = "rewindEnable", Group = "System", Label = "Rewind", Type = OptionType.Bool,
            Help = "Lets you step back in time with mGBA's rewind button. Uses a little memory and CPU.",
            OnDetail = "Rewind is available while you play.",
            OffDetail = "No rewind.",
            DefaultValue = "false",
        },
        new()
        {
            Id = "useBios", Group = "System", Label = "Use your BIOS", Type = OptionType.Bool,
            Help = "Runs your own GBA BIOS file when one is set. Without it mGBA uses its built-in replacement, which works for almost every game.",
            OnDetail = "Your BIOS runs when one is set (boot logo included unless skipped).",
            OffDetail = "mGBA's built-in BIOS replacement.",
            DefaultValue = "true",
        },
        new()
        {
            Id = "skipBios", Group = "System", Label = "Skip the boot logo", Type = OptionType.Bool,
            Help = "Starts the game straight away instead of showing the BIOS boot animation.",
            OnDetail = "Games start immediately.",
            OffDetail = "The boot animation plays first (only with your own BIOS).",
            DefaultValue = "false",
        },
    ];

    /// <summary>Ready-made looks for a game: how the picture is scaled and drawn.</summary>
    public static readonly PresetSet Looks = new("Look",
        "Looks set scaling, smoothing and LCD ghosting for this game in one go.",
        [
            new("pixel", "Pixel perfect", "Whole-number scaling with sharp, even pixels.",
                new Dictionary<string, string> { ["lockIntegerScaling"] = "true", ["lockAspectRatio"] = "true", ["resampleVideo"] = "false", ["interframeBlending"] = "false" }),
            new("lcd", "Original LCD", "Sharp pixels plus the original screen's ghosting, so flicker effects look solid.",
                new Dictionary<string, string> { ["lockIntegerScaling"] = "true", ["lockAspectRatio"] = "true", ["resampleVideo"] = "false", ["interframeBlending"] = "true" }),
            new("smooth", "Smooth", "Bigger picture with softened pixel edges.",
                new Dictionary<string, string> { ["lockIntegerScaling"] = "false", ["lockAspectRatio"] = "true", ["resampleVideo"] = "true", ["interframeBlending"] = "false" }),
            new("full", "Full screen", "Stretched to fill the whole screen.",
                new Dictionary<string, string> { ["lockIntegerScaling"] = "false", ["lockAspectRatio"] = "false", ["resampleVideo"] = "true", ["interframeBlending"] = "false" }),
        ],
        ["lockIntegerScaling", "lockAspectRatio", "resampleVideo", "interframeBlending"]);

    private static readonly HashSet<string> Bools = ["lockIntegerScaling", "lockAspectRatio", "resampleVideo", "interframeBlending", "rewindEnable", "useBios", "skipBios"];

    public string ConfigFile => _configFile();

    public IReadOnlyDictionary<string, string> ReadGlobal()
    {
        var ini = QtIni.Load(ConfigFile, quoting: false);
        var result = new Dictionary<string, string>();
        foreach (var o in Options)
        {
            if (ini.Get(Section, o.Id) is { } raw)
            {
                result[o.Id] = Decode(o.Id, raw);
            }
        }
        return result;
    }

    public void WriteGlobal(IReadOnlyDictionary<string, string?> values)
    {
        var ini = QtIni.Load(ConfigFile, quoting: false);
        foreach (var (id, value) in values)
        {
            Check(id);
            if (value == null)
            {
                ini.Remove(Section, id);
            }
            else
            {
                ini.Set(Section, id, Encode(id, value));
            }
        }
        ini.Save(ConfigFile);
    }

    /// <summary>A raw mGBA key in [ports.qt] (e.g. gba.bios), outside the curated list.</summary>
    public string? GetRaw(string key) => QtIni.Load(ConfigFile, quoting: false).Get(Section, key);

    public void SetRaw(string key, string? value)
    {
        var ini = QtIni.Load(ConfigFile, quoting: false);
        if (value == null)
        {
            ini.Remove(Section, key);
        }
        else
        {
            ini.Set(Section, key, value);
        }
        ini.Save(ConfigFile);
    }

    public IReadOnlyDictionary<string, string> ReadPerGame(string titleId) =>
        LoadOverrides().TryGetValue(titleId, out var v) ? v : new Dictionary<string, string>();

    public void WritePerGame(string titleId, IReadOnlyDictionary<string, string?> values)
    {
        var all = LoadOverrides();
        var mine = all.TryGetValue(titleId, out var v) ? v : new Dictionary<string, string>();
        foreach (var (id, value) in values)
        {
            Check(id);
            if (value == null)
            {
                mine.Remove(id);
            }
            else
            {
                mine[id] = value;
            }
        }
        if (mine.Count == 0)
        {
            all.Remove(titleId);
        }
        else
        {
            all[titleId] = mine;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(_overridesFile)!);
        var tmp = _overridesFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _overridesFile, overwrite: true);
    }

    /// <summary>The per-game values as mGBA <c>-C key=value</c> arguments.</summary>
    public IEnumerable<string> OverrideArgs(string titleId) =>
        ReadPerGame(titleId).OrderBy(kv => kv.Key, StringComparer.Ordinal).SelectMany(kv => new[] { "-C", $"{kv.Key}={Encode(kv.Key, kv.Value)}" });

    private Dictionary<string, Dictionary<string, string>> LoadOverrides()
    {
        try
        {
            if (File.Exists(_overridesFile))
            {
                return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(_overridesFile)) ?? new();
            }
        }
        catch (JsonException)
        {
        }
        return new();
    }

    private void Check(string id)
    {
        if (Options.All(o => o.Id != id))
        {
            throw new ArgumentException($"mGBA has no Scanline setting '{id}'");
        }
    }

    private static string Encode(string id, string value) => Bools.Contains(id) ? (value == "true" ? "1" : "0") : value;

    private static string Decode(string id, string raw) =>
        Bools.Contains(id) ? (long.TryParse(raw, out var n) && n != 0 ? "true" : "false") : raw;
}
