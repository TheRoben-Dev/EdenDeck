using EdenDeck.Core.Config;
using System.Text.Json;
using EdenDeck.Core.Backends;
using EdenDeck.Core.Settings;

namespace EdenDeck.Backends.MelonDs;

/// <summary>
/// melonDS's settings. Settings for all games are melonDS's own, in melonDS.toml. melonDS has no
/// per-game settings of its own, so a game's settings live in Scanline
/// (<c>&lt;data&gt;/melonds/games/&lt;title id&gt;.json</c>) and are laid over a copy of melonDS.toml
/// that the game is started with (see <see cref="MelonDsPaths.RunRoot"/>). Option ids are
/// melonDS.toml's dotted paths; the window ones belong to the first instance's first window.
/// </summary>
public sealed class MelonDsSettings(Func<MelonDsPaths> paths, string dataDir) : ISettingsProvider
{
    private static OptionChoice C(string value, string label, string? detail = null) => new(value, label, detail);

    /// <summary>Options xemu stores as numbers rather than strings.</summary>
    private static readonly HashSet<string> Numbers = [Renderer, Scale, Layout, Sizing];

    public const string Renderer = "3D.Renderer";
    public const string Scale = "3D.GL.ScaleFactor";
    public const string Layout = "Instance0.Window0.ScreenLayout";
    public const string Sizing = "Instance0.Window0.ScreenSizing";
    public const string Swap = "Instance0.Window0.ScreenSwap";
    public const string Jit = "JIT.Enable";

    public IReadOnlyList<OptionDefinition> Options { get; } =
    [
        new()
        {
            Id = Renderer, Group = "Graphics", Label = "3D renderer", Type = OptionType.Choice,
            Help = "How melonDS draws the DS's 3D. Only OpenGL can render above the DS's own resolution.",
            Choices =
            [
                C("0", "Software", "Exactly like a DS, at its own 256x192. Lightest on the GPU."),
                C("1", "OpenGL", "Can render at higher resolutions (Resolution below)."),
                C("2", "OpenGL (compute)", "The newer OpenGL renderer: more accurate at high resolutions, needs a recent GPU."),
            ],
            DefaultValue = "0",
        },
        new()
        {
            Id = Scale, Group = "Graphics", Label = "Resolution", Type = OptionType.Choice,
            Help = "Renders the 3D above the DS's own resolution. Needs an OpenGL renderer.",
            Choices =
            [
                C("1", "Native (DS)", "The DS's own 256x192 per screen."),
                C("2", "2x", "Sharper 3D. The Steam Deck sweet spot."),
                C("3", "3x", "Very sharp. For a TV."),
                C("4", "4x", "For a TV and a desktop-class GPU."),
                C("6", "6x", "For powerful desktop GPUs only."),
            ],
            DefaultValue = "1",
        },
        new()
        {
            Id = Layout, Group = "Screens", Label = "Screen layout", Type = OptionType.Choice,
            Help = "Where the two screens go. On a Steam Deck, side by side fills the wide screen best.",
            Choices =
            [
                C("0", "Natural", "Top screen above the bottom one, as on a DS (turned with the window)."),
                C("1", "Stacked", "Top screen above the bottom one."),
                C("2", "Side by side", "Both screens next to each other. Fills a wide screen."),
                C("3", "Hybrid", "One big screen with both small beside it."),
            ],
            DefaultValue = "0",
        },
        new()
        {
            Id = Sizing, Group = "Screens", Label = "Screen sizes", Type = OptionType.Choice,
            Help = "How big each screen is.",
            Choices =
            [
                C("0", "Even", "Both the same size."),
                C("1", "Top bigger", "The top screen as large as fits."),
                C("2", "Bottom bigger", "The touch screen as large as fits."),
                C("3", "Automatic", "Follows what the game is doing."),
                C("4", "Top only", "Just the top screen."),
                C("5", "Bottom only", "Just the touch screen."),
            ],
            DefaultValue = "0",
        },
        new()
        {
            Id = Swap, Group = "Screens", Label = "Swap screens", Type = OptionType.Bool,
            Help = "Puts the touch screen where the top screen goes. Right stick click swaps them while playing.",
            OnDetail = "Touch screen first.", OffDetail = "Top screen first, as on a DS.",
            DefaultValue = "false",
        },
        new()
        {
            Id = "Instance0.Window0.IntegerScaling", Group = "Screens", Label = "Whole-pixel scaling", Type = OptionType.Bool,
            Help = "Scales the screens only by whole steps, so every DS pixel is the same size.",
            OnDetail = "Even pixels; smaller picture with borders.", OffDetail = "As large as fits.",
            DefaultValue = "false",
        },
        new()
        {
            Id = "Screen.Filter", Group = "Screens", Label = "Screen smoothing", Type = OptionType.Bool,
            Help = "Smooths the screens as they're scaled up.",
            OnDetail = "Soft edges.", OffDetail = "Hard pixel edges.",
            DefaultValue = "true",
        },
        new()
        {
            Id = "Screen.VSync", Group = "Screens", Label = "VSync", Type = OptionType.Bool,
            Help = "Waits for the screen's refresh to stop tearing.",
            OnDetail = "No tearing; a little more lag.", OffDetail = "Lowest input lag; may tear.",
            DefaultValue = "false",
        },
        new()
        {
            Id = Jit, Group = "System", Label = "Fast mode (JIT)", Type = OptionType.Bool,
            Help = "Translates the DS's code for your CPU instead of interpreting it. Much faster; a few games misbehave with it.",
            OnDetail = "Much faster. Recommended on a Steam Deck.", OffDetail = "Slower, most compatible.",
            DefaultValue = "false",
        },
    ];

    private OptionDefinition Check(string id) =>
        Options.FirstOrDefault(o => o.Id == id) ?? throw new ArgumentException($"Unknown melonDS option '{id}'");

    public IReadOnlyDictionary<string, string> ReadGlobal() => Read(paths().Load());

    private Dictionary<string, string> Read(TomlFile toml)
    {
        var result = new Dictionary<string, string>();
        foreach (var o in Options)
        {
            if (toml.Get(o.Id) is { } v)
            {
                result[o.Id] = o.Type == OptionType.Bool ? (v.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false") : v;
            }
        }
        return result;
    }

    public void WriteGlobal(IReadOnlyDictionary<string, string?> values)
    {
        var p = paths();
        var toml = p.Load();
        Apply(toml, values);
        toml.Save(p.ConfigFile);
    }

    /// <summary>Writes values into a config (null removes, back to xemu's default).</summary>
    public void Apply(TomlFile toml, IReadOnlyDictionary<string, string?> values)
    {
        foreach (var (id, value) in values)
        {
            var o = Check(id);
            if (value == null)
            {
                toml.Remove(id);
            }
            else if (o.Type == OptionType.Bool)
            {
                toml.SetBool(id, value == "true");
            }
            else if (Numbers.Contains(id) && long.TryParse(value, out var n))
            {
                toml.SetInt(id, n);
            }
            else
            {
                toml.SetString(id, value);
            }
        }
    }

    // ---- per game, in Scanline ----

    public string GameFile(string titleId) => Path.Combine(dataDir, "melonds", "games", titleId.ToUpperInvariant() + ".json");

    public IReadOnlyDictionary<string, string> ReadPerGame(string titleId)
    {
        if (string.IsNullOrEmpty(titleId) || !File.Exists(GameFile(titleId)))
        {
            return new Dictionary<string, string>();
        }
        try
        {
            var all = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(GameFile(titleId))) ?? [];
            return all.Where(kv => Options.Any(o => o.Id == kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    public void WritePerGame(string titleId, IReadOnlyDictionary<string, string?> values)
    {
        if (string.IsNullOrEmpty(titleId))
        {
            throw new InvalidOperationException("This file has no title id, so it can't have its own settings.");
        }
        var current = ReadPerGame(titleId).ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var (id, value) in values)
        {
            Check(id);
            if (value == null)
            {
                current.Remove(id);
            }
            else
            {
                current[id] = value;
            }
        }
        var file = GameFile(titleId);
        if (current.Count == 0)
        {
            File.Delete(file);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(current, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>One press for the heavy graphics settings.</summary>
    public static readonly PresetSet Performance = new("Performance preset",
        "Presets set the 3D renderer and resolution for this game in one go.",
        [
            new("battery", "Battery saver", "The DS's own resolution with the software renderer and fast mode. Longest battery life.",
                new Dictionary<string, string> { [Renderer] = "0", [Scale] = "1", [Jit] = "true" }),
            new("balanced", "Balanced", "OpenGL at 2x with fast mode. The Steam Deck sweet spot.",
                new Dictionary<string, string> { [Renderer] = "1", [Scale] = "2", [Jit] = "true" }),
            new("tv", "TV quality", "OpenGL at 4x with fast mode, for a TV and a desktop-class GPU.",
                new Dictionary<string, string> { [Renderer] = "1", [Scale] = "4", [Jit] = "true" }),
        ],
        [Renderer, Scale, Jit]);
}
