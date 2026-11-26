using EdenDeck.Backends.Pcsx2;
using EdenDeck.Core.Backends;
using EdenDeck.Core.Settings;

namespace EdenDeck.Backends.Duckstation;

/// <summary>
/// DuckStation's settings, in DuckStation's own files: global values in settings.ini, per-game
/// values in gamesettings/&lt;SERIAL&gt;.ini, which DuckStation layers over the global ones when the
/// game boots and shows as the game's own settings in its Game Properties. Option ids are
/// <c>Section/Key</c>. A game without a serial (one DuckStation hasn't run yet, from a .chd that
/// isn't named after its Redump entry) has no per-game file to use yet.
/// </summary>
public sealed class DuckstationSettings(Func<DuckstationPaths> paths) : ISettingsProvider
{
    private static OptionChoice C(string value, string label, string? detail = null) => new(value, label, detail);

    public IReadOnlyList<OptionDefinition> Options { get; } =
    [
        new()
        {
            Id = "GPU/ResolutionScale", Group = "Graphics", Label = "Resolution", Type = OptionType.Choice,
            Help = "Renders 3D above the PS1's own resolution. Sharper, but heavier on the GPU.",
            Choices =
            [
                C("1", "Native (PS1)", "The game's own 240p. Lightest, with the original look."),
                C("2", "2x (~480p)", "Cleaner 3D, still very light."),
                C("3", "3x (~720p)", "Sharp. The Steam Deck sweet spot."),
                C("5", "5x (~1080p)", "Very sharp. Fine on a TV and most desktop GPUs."),
                C("6", "6x (~1440p)", "For strong desktop GPUs."),
                C("9", "9x (~4K)", "For powerful desktop GPUs only."),
            ],
            DefaultValue = "1",
        },
        new()
        {
            Id = "GPU/Renderer", Group = "Graphics", Label = "Renderer", Type = OptionType.Choice,
            Help = "How DuckStation draws. Frame generation and the CRT effect need Vulkan.",
            Choices =
            [
                C("Automatic", "Automatic", "DuckStation picks; that's Vulkan on a Steam Deck and most PCs."),
                C("Vulkan", "Vulkan", "Fast, and frame generation and the CRT effect work with it."),
                C("OpenGL", "OpenGL", "A fallback for drivers with Vulkan trouble. No frame generation or CRT effect."),
                C("Software", "Software", "The PS1's own look at its own resolution. For games that glitch on the GPU renderers."),
            ],
            DefaultValue = "Automatic",
        },
        new()
        {
            Id = "GPU/PGXPEnable", Group = "Graphics", Label = "Steady 3D (PGXP)", Type = OptionType.Bool,
            Help = "Fixes the PS1's wobbling, warping polygons and textures. A few games misbehave with it; switch it off for those.",
            OnDetail = "Stable, straight 3D.", OffDetail = "The original wobble, as on the console.",
            DefaultValue = "false",
        },
        new()
        {
            Id = "GPU/TextureFilter", Group = "Graphics", Label = "Texture smoothing", Type = OptionType.Choice,
            Help = "How textures are smoothed when they're scaled up.",
            Choices =
            [
                C("Nearest", "Off", "Sharp, blocky texels, as on the console."),
                C("Bilinear", "Smooth", "Soft textures."),
                C("JINC2", "JINC2", "Smooth with sharper edges."),
                C("xBR", "xBR", "Smooths pixel art and keeps its edges. Heavier."),
            ],
            DefaultValue = "Nearest",
        },
        new()
        {
            Id = "Display/AspectRatio", Group = "Graphics", Label = "Picture shape", Type = OptionType.Choice,
            Help = "The shape the picture is shown in. PS1 games are 4:3; 16:9 goes with widescreen rendering or a widescreen patch.",
            Choices =
            [
                C("Auto (Game Native)", "Automatic (4:3)", "The game's own shape."),
                C("4:3", "4:3"),
                C("16:9", "16:9", "For widescreen rendering or a widescreen patch."),
                C("Stretch To Fill", "Stretch to fill", "Fills the screen; the picture is stretched."),
            ],
            DefaultValue = "Auto (Game Native)",
        },
        new()
        {
            Id = "GPU/WidescreenHack", Group = "Graphics", Label = "Widescreen rendering", Type = OptionType.Bool,
            Help = "Draws 3D games wider so they fill a 16:9 screen. Menus and 2D parts may stretch; a widescreen patch (Patches & cheats) is better where one exists.",
            OnDetail = "16:9 3D. Set the picture shape to 16:9 too.", OffDetail = "The game's own 4:3.",
            DefaultValue = "false",
        },
        new()
        {
            Id = "Display/Scaling", Group = "Graphics", Label = "Screen smoothing", Type = OptionType.Choice,
            Help = "How the picture is scaled up to your screen.",
            Choices =
            [
                C("BilinearSmooth", "Smooth", "Soft edges."),
                C("BilinearSharp", "Sharp", "Crisp but even."),
                C("Nearest", "Off", "Hard pixel edges."),
                C("NearestInteger", "Whole pixels", "Hard edges at a whole-number scale, with borders."),
            ],
            DefaultValue = "BilinearSmooth",
        },
        new()
        {
            Id = "Display/VSync", Group = "Graphics", Label = "VSync", Type = OptionType.Bool,
            Help = "Waits for the screen's refresh to stop tearing.",
            OnDetail = "No tearing; a little more lag.", OffDetail = "Lowest input lag; may tear.",
            DefaultValue = "false",
        },
        new()
        {
            Id = "BIOS/PatchFastBoot", Group = "System", Label = "Skip the PS1 intro", Type = OptionType.Bool,
            Help = "Starts the game straight away instead of showing the PS1 start-up screens first.",
            OnDetail = "Straight into the game.", OffDetail = "Shows the PS1 start-up screens, as on the console.",
            DefaultValue = "false",
        },
    ];

    private OptionDefinition Check(string id) =>
        Options.FirstOrDefault(o => o.Id == id) ?? throw new ArgumentException($"Unknown DuckStation option '{id}'");

    private static (string Section, string Key) Split(string id)
    {
        var slash = id.IndexOf('/');
        return (id[..slash], id[(slash + 1)..]);
    }

    /// <summary>The per-game file, or null for a game without a serial.</summary>
    public string? GameFile(string serial) => string.IsNullOrEmpty(serial) ? null : paths().GameSettingsFile(serial);

    public IReadOnlyDictionary<string, string> ReadGlobal() => Read(Pcsx2Ini.Load(paths().ConfigFile));

    public void WriteGlobal(IReadOnlyDictionary<string, string?> values)
    {
        var p = paths();
        var ini = p.LoadConfigForWrite();
        Apply(ini, values);
        ini.Save(p.ConfigFile);
    }

    public IReadOnlyDictionary<string, string> ReadPerGame(string titleId) =>
        GameFile(titleId) is { } f ? Read(Pcsx2Ini.Load(f)) : new Dictionary<string, string>();

    public void WritePerGame(string titleId, IReadOnlyDictionary<string, string?> values)
    {
        var file = GameFile(titleId) ?? throw new InvalidOperationException("Start this game once first, so Scanline learns which disc it is. Then its own settings work.");
        var ini = Pcsx2Ini.Load(file);
        Apply(ini, values);
        if (ini.IsEmpty)
        {
            File.Delete(file);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        ini.Save(file);
    }

    private Dictionary<string, string> Read(Pcsx2Ini ini)
    {
        var result = new Dictionary<string, string>();
        foreach (var o in Options)
        {
            var (section, key) = Split(o.Id);
            if (ini.Get(section, key) is { } v)
            {
                result[o.Id] = o.Type == OptionType.Bool ? (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1" ? "true" : "false") : v;
            }
        }
        return result;
    }

    private void Apply(Pcsx2Ini ini, IReadOnlyDictionary<string, string?> values)
    {
        foreach (var (id, value) in values)
        {
            Check(id);
            var (section, key) = Split(id);
            if (value == null)
            {
                ini.Remove(section, key);
            }
            else
            {
                ini.Set(section, key, value);
            }
        }
    }

    /// <summary>One press for the heavy graphics settings.</summary>
    public static readonly PresetSet Performance = new("Performance preset",
        "Presets set resolution and steady 3D for this game in one go.",
        [
            new("battery", "Battery saver", "The PS1's own resolution. Longest battery life on a Steam Deck.",
                new Dictionary<string, string> { ["GPU/ResolutionScale"] = "1", ["GPU/PGXPEnable"] = "false" }),
            new("balanced", "Balanced", "3x resolution (~720p) with steady 3D. The Steam Deck sweet spot.",
                new Dictionary<string, string> { ["GPU/ResolutionScale"] = "3", ["GPU/PGXPEnable"] = "true" }),
            new("tv", "TV quality", "5x resolution (~1080p) with steady 3D, for a TV and a desktop-class GPU.",
                new Dictionary<string, string> { ["GPU/ResolutionScale"] = "5", ["GPU/PGXPEnable"] = "true" }),
            new("sharp", "Sharp", "6x resolution (~1440p) with steady 3D, for strong desktop GPUs.",
                new Dictionary<string, string> { ["GPU/ResolutionScale"] = "6", ["GPU/PGXPEnable"] = "true" }),
        ],
        ["GPU/ResolutionScale", "GPU/PGXPEnable"]);
}
