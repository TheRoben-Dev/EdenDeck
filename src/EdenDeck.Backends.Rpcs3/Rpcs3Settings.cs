using EdenDeck.Core.Backends;
using EdenDeck.Core.Settings;

namespace EdenDeck.Backends.Rpcs3;

/// <summary>
/// RPCS3's settings, in RPCS3's own files: global values in config.yml, per-game values in
/// custom_configs/config_&lt;TITLEID&gt;.yml, which RPCS3 applies on top of the global config when
/// the game boots (rpcs3/Emu/System.cpp) and shows as the game's custom config in its own UI.
/// Option ids are the YAML paths; values are written the way RPCS3 writes them.
/// </summary>
public sealed class Rpcs3Settings(Func<Rpcs3Paths> paths) : ISettingsProvider
{
    private static OptionChoice C(string value, string label, string? detail = null) => new(value, label, detail);

    public IReadOnlyList<OptionDefinition> Options { get; } =
    [
        new()
        {
            Id = "Video/Resolution Scale", Group = "Graphics", Label = "Resolution", Type = OptionType.Choice,
            Help = "Renders the game above the PS3's own 720p. Sharper, but much heavier on the GPU.",
            Choices =
            [
                C("100", "720p (PS3)", "The game's own resolution. Lightest on the GPU and battery."),
                C("150", "1080p", "Noticeably sharper. Fine on a desktop GPU, heavy on a Steam Deck."),
                C("200", "1440p", "Very sharp on a TV. Needs a strong GPU."),
                C("300", "4K", "For powerful desktop GPUs only."),
            ],
            DefaultValue = "100",
        },
        new()
        {
            Id = "Video/Output Scaling Mode", Group = "Graphics", Label = "Upscaling", Type = OptionType.Choice,
            Help = "How the picture is stretched to your screen.",
            Choices =
            [
                C("Bilinear", "Bilinear", "Smooth and cheap."),
                C("Nearest", "Nearest", "Hard pixel edges."),
                C("FidelityFX Super Resolution", "FSR", "Sharper edges when the screen is bigger than the game's resolution. Costs a little GPU."),
            ],
            DefaultValue = "Bilinear",
        },
        new()
        {
            Id = "Video/Anisotropic Filter Override", Group = "Graphics", Label = "Texture filtering", Type = OptionType.Choice,
            Help = "Keeps floors and walls sharp at an angle.",
            Choices = [C("0", "Game's own"), C("2", "2x"), C("4", "4x"), C("8", "8x"), C("16", "16x", "Sharpest. Cheap on most GPUs.")],
            DefaultValue = "0",
        },
        new()
        {
            Id = "Video/Frame limit", Group = "Graphics", Label = "Frame limit", Type = OptionType.Choice,
            Help = "Caps the frame rate. Auto follows the game, which is right for almost every game.",
            Choices =
            [
                C("Auto", "Auto", "Follows the game."),
                C("30", "30 fps", "Steadier frame pacing for games that can't hold 60."),
                C("60", "60 fps"),
                C("Off", "Off", "No cap. Some games then run too fast."),
            ],
            DefaultValue = "Auto",
        },
        new()
        {
            Id = "Video/Shader Mode", Group = "Graphics", Label = "Shader compiling", Type = OptionType.Choice,
            Help = "What happens while RPCS3 builds a new shader.",
            Choices =
            [
                C("Async Recompiler with Shader Interpreter", "No stutter", "Draws with a slower interpreter until the shader is ready. Best on a Steam Deck."),
                C("Async Recompiler (multi-threaded)", "Skip until ready", "Objects can pop in for a moment while shaders build."),
            ],
            DefaultValue = "Async Recompiler with Shader Interpreter",
        },
        new()
        {
            Id = "Video/VSync Mode", Group = "Graphics", Label = "VSync", Type = OptionType.Choice,
            Help = "Waits for the screen's refresh to stop tearing.",
            Choices = [C("Disabled", "Off", "Lowest input lag; may tear."), C("Adaptive", "Adaptive", "Syncs only while the game keeps up."), C("Full", "On", "No tearing; a little more lag.")],
            DefaultValue = "Disabled",
        },
        new()
        {
            Id = "Video/Aspect ratio", Group = "Graphics", Label = "Picture shape", Type = OptionType.Choice,
            Help = "The shape the game is told the TV has.",
            Choices = [C("16:9", "16:9 widescreen"), C("4:3", "4:3")],
            DefaultValue = "16:9",
        },
        new()
        {
            Id = "Video/Stretch To Display Area", Group = "Graphics", Label = "Stretch to fill", Type = OptionType.Bool,
            Help = "Fills the whole screen instead of keeping the picture's shape.",
            OnDetail = "No borders, but the picture is stretched (the Steam Deck is 16:10).",
            OffDetail = "The picture keeps its shape, with thin borders on a 16:10 screen.",
            DefaultValue = "false",
        },
        new()
        {
            Id = "Video/Write Color Buffers", Group = "Game fixes", Label = "Write color buffers", Type = OptionType.Bool,
            Help = "A fix some games need for missing or flickering effects. Slower; leave it off unless a game needs it.",
            OnDetail = "Fixes effects in games that need it, at a performance cost.",
            OffDetail = "Faster. Right for most games.",
            DefaultValue = "false",
        },
        new()
        {
            Id = "Miscellaneous/Show trophy popups", Group = "System", Label = "Trophy popups", Type = OptionType.Bool,
            Help = "RPCS3's popup when you earn a trophy.",
            OnDetail = "Popups show while you play.", OffDetail = "No popups.",
            DefaultValue = "true",
        },
    ];

    private void Check(string id)
    {
        if (Options.All(o => o.Id != id))
        {
            throw new ArgumentException($"Unknown RPCS3 option '{id}'");
        }
    }

    public IReadOnlyDictionary<string, string> ReadGlobal() => Read(paths().ConfigFile);

    public void WriteGlobal(IReadOnlyDictionary<string, string?> values) => Write(paths().ConfigFile, values, deleteWhenEmpty: false);

    public IReadOnlyDictionary<string, string> ReadPerGame(string titleId) => Read(paths().CustomConfigFile(titleId));

    public void WritePerGame(string titleId, IReadOnlyDictionary<string, string?> values) => Write(paths().CustomConfigFile(titleId), values, deleteWhenEmpty: true);

    private Dictionary<string, string> Read(string file)
    {
        var yaml = Rpcs3Yaml.Load(file);
        var result = new Dictionary<string, string>();
        foreach (var o in Options)
        {
            if (yaml.Get(o.Id) is { } v)
            {
                result[o.Id] = v;
            }
        }
        return result;
    }

    private void Write(string file, IReadOnlyDictionary<string, string?> values, bool deleteWhenEmpty)
    {
        var yaml = Rpcs3Yaml.Load(file);
        foreach (var (id, value) in values)
        {
            Check(id);
            if (value == null)
            {
                yaml.Remove(id);
            }
            else
            {
                yaml.Set(id, value);
            }
        }
        if (deleteWhenEmpty && yaml.IsEmpty)
        {
            File.Delete(file);
            return;
        }
        yaml.Save(file);
    }

    /// <summary>One press for the heavy graphics settings.</summary>
    public static readonly PresetSet Performance = new("Performance preset",
        "Presets set resolution, upscaling and texture filtering for this game in one go.",
        [
            new("battery", "Battery saver", "The PS3's own 720p with cheap scaling. Longest battery life on a Steam Deck.",
                new Dictionary<string, string> { ["Video/Resolution Scale"] = "100", ["Video/Output Scaling Mode"] = "Bilinear", ["Video/Anisotropic Filter Override"] = "0" }),
            new("balanced", "Balanced", "720p, sharpened with FSR, with sharp textures. The Steam Deck sweet spot.",
                new Dictionary<string, string> { ["Video/Resolution Scale"] = "100", ["Video/Output Scaling Mode"] = "FidelityFX Super Resolution", ["Video/Anisotropic Filter Override"] = "16" }),
            new("tv", "TV quality", "1080p with sharp textures, for a TV and a desktop-class GPU.",
                new Dictionary<string, string> { ["Video/Resolution Scale"] = "150", ["Video/Output Scaling Mode"] = "FidelityFX Super Resolution", ["Video/Anisotropic Filter Override"] = "16" }),
            new("sharp", "Sharp", "1440p and up for strong desktop GPUs.",
                new Dictionary<string, string> { ["Video/Resolution Scale"] = "200", ["Video/Output Scaling Mode"] = "Bilinear", ["Video/Anisotropic Filter Override"] = "16" }),
        ],
        ["Video/Resolution Scale", "Video/Output Scaling Mode", "Video/Anisotropic Filter Override"]);
}
