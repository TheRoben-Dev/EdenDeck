using EdenDeck.Core.Config;
using System.Text.Json;
using EdenDeck.Core.Backends;
using EdenDeck.Core.Settings;

namespace EdenDeck.Backends.Xemu;

/// <summary>
/// xemu's settings. Settings for all games are xemu's own, in xemu.toml. xemu has no per-game
/// settings of its own, so a game's settings live in Scanline (<c>&lt;data&gt;/xemu/games/&lt;title id&gt;.json</c>)
/// and are laid over a copy of xemu.toml that the game is started with (<c>-config_path</c>).
/// Option ids are xemu.toml's dotted paths.
/// </summary>
public sealed class XemuSettings(Func<XemuPaths> paths, string dataDir) : ISettingsProvider
{
    private static OptionChoice C(string value, string label, string? detail = null) => new(value, label, detail);

    /// <summary>Options xemu stores as numbers rather than strings.</summary>
    private static readonly HashSet<string> Numbers = ["display.quality.surface_scale"];

    public IReadOnlyList<OptionDefinition> Options { get; } =
    [
        new()
        {
            Id = "display.quality.surface_scale", Group = "Graphics", Label = "Resolution", Type = OptionType.Choice,
            Help = "Renders above the Xbox's own resolution. Sharper, but heavier on the GPU.",
            Choices =
            [
                C("1", "Native (Xbox)", "The game's own 480p. Lightest on the GPU and battery."),
                C("2", "2x (~960p)", "Sharper. The Steam Deck sweet spot for most games."),
                C("3", "3x (~1440p)", "Very sharp. For a TV and a desktop-class GPU."),
                C("4", "4x (~4K)", "For powerful desktop GPUs only."),
            ],
            DefaultValue = "1",
        },
        new()
        {
            Id = "display.renderer", Group = "Graphics", Label = "Renderer", Type = OptionType.Choice,
            Help = "How xemu draws. Frame generation and the CRT effect need Vulkan.",
            Choices =
            [
                C("VULKAN", "Vulkan", "Frame generation and the CRT effect work with it. Recommended on a Steam Deck."),
                C("OPENGL", "OpenGL", "xemu's long-standing renderer. A fallback for games that glitch on Vulkan. No frame generation or CRT effect."),
            ],
            DefaultValue = "OPENGL",
        },
        new()
        {
            Id = "display.ui.aspect_ratio", Group = "Graphics", Label = "Picture shape", Type = OptionType.Choice,
            Help = "The shape the picture is shown in. Many Xbox games have a widescreen option of their own in the game's or the dashboard's settings.",
            Choices =
            [
                C("auto", "Automatic", "Follows what the game outputs."),
                C("native", "Native", "The game's own pixels, unscaled shape."),
                C("4x3", "4:3"),
                C("16x9", "16:9", "For games set to widescreen."),
            ],
            DefaultValue = "auto",
        },
        new()
        {
            Id = "display.ui.fit", Group = "Graphics", Label = "Fit to screen", Type = OptionType.Choice,
            Help = "How the picture fills your screen.",
            Choices = [C("scale", "Scale", "As large as fits, keeping its shape."), C("stretch", "Stretch", "Fills the screen; the picture is stretched."), C("center", "Center", "Its own size, in the middle.")],
            DefaultValue = "scale",
        },
        new()
        {
            Id = "display.filtering", Group = "Graphics", Label = "Screen smoothing", Type = OptionType.Choice,
            Help = "How the picture is scaled up to your screen.",
            Choices = [C("linear", "Smooth", "Soft edges."), C("nearest", "Off", "Hard pixel edges.")],
            DefaultValue = "linear",
        },
        new()
        {
            Id = "display.window.vsync", Group = "Graphics", Label = "VSync", Type = OptionType.Bool,
            Help = "Waits for the screen's refresh to stop tearing.",
            OnDetail = "No tearing; a little more lag.", OffDetail = "Lowest input lag; may tear.",
            DefaultValue = "true",
        },
        new()
        {
            Id = "general.skip_boot_anim", Group = "System", Label = "Skip the Xbox intro", Type = OptionType.Bool,
            Help = "Starts the game straight away instead of showing the Xbox start-up animation first.",
            OnDetail = "Straight into the game.", OffDetail = "Shows the start-up animation, as on the console.",
            DefaultValue = "false",
        },
        new()
        {
            Id = "sys.mem_limit", Group = "System", Label = "Memory", Type = OptionType.Choice,
            Help = "The Xbox's memory. A few games and mods use the 128 MB of a development kit.",
            Choices = [C("64", "64 MB", "A retail Xbox. Right for nearly every game."), C("128", "128 MB", "A development kit's memory, for games and mods that use it.")],
            DefaultValue = "64",
        },
    ];

    private OptionDefinition Check(string id) =>
        Options.FirstOrDefault(o => o.Id == id) ?? throw new ArgumentException($"Unknown xemu option '{id}'");

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

    public string GameFile(string titleId) => Path.Combine(dataDir, "xemu", "games", titleId.ToUpperInvariant() + ".json");

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

    /// <summary>One press for the heavy graphics setting.</summary>
    public static readonly PresetSet Performance = new("Performance preset",
        "Presets set the resolution for this game in one go.",
        [
            new("battery", "Battery saver", "The Xbox's own 480p. Longest battery life on a Steam Deck.",
                new Dictionary<string, string> { ["display.quality.surface_scale"] = "1" }),
            new("balanced", "Balanced", "2x resolution (~960p). The Steam Deck sweet spot.",
                new Dictionary<string, string> { ["display.quality.surface_scale"] = "2" }),
            new("tv", "TV quality", "3x resolution (~1440p), for a TV and a desktop-class GPU.",
                new Dictionary<string, string> { ["display.quality.surface_scale"] = "3" }),
            new("sharp", "Sharp", "4x resolution (~4K) for powerful desktop GPUs.",
                new Dictionary<string, string> { ["display.quality.surface_scale"] = "4" }),
        ],
        ["display.quality.surface_scale"]);
}
