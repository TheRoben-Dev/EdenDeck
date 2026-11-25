using System.Globalization;
using EdenDeck.Core.Backends;
using EdenDeck.Core.Settings;

namespace EdenDeck.Backends.Pcsx2;

/// <summary>
/// PCSX2's settings, in PCSX2's own files: global values in inis/PCSX2.ini, per-game values in
/// gamesettings/&lt;SERIAL&gt;_&lt;CRC&gt;.ini, which PCSX2 layers over the global ones when the game
/// boots and shows as the game's own settings in its Game Properties. Option ids are
/// <c>Section/Key</c> (the section can contain a slash: <c>EmuCore/GS/upscale_multiplier</c>).
/// A game whose CRC isn't known yet (a .chd that hasn't run once) has no per-game file to use.
/// </summary>
public sealed class Pcsx2Settings(Func<Pcsx2Paths> paths, Func<string, uint?> crcFor) : ISettingsProvider
{
    private static OptionChoice C(string value, string label, string? detail = null) => new(value, label, detail);

    public IReadOnlyList<OptionDefinition> Options { get; } =
    [
        new()
        {
            Id = "EmuCore/GS/upscale_multiplier", Group = "Graphics", Label = "Resolution", Type = OptionType.Choice,
            Help = "Renders the game above the PS2's own resolution. Sharper, but heavier on the GPU.",
            Choices =
            [
                C("1", "Native (PS2)", "The game's own resolution. Lightest on the GPU and battery."),
                C("2", "2x (~720p)", "Sharper. The Steam Deck sweet spot for most games."),
                C("3", "3x (~1080p)", "Very sharp. Fine on a desktop GPU, heavy on a Steam Deck."),
                C("4", "4x (~1440p)", "Needs a strong GPU."),
                C("6", "6x (~4K)", "For powerful desktop GPUs only."),
            ],
            DefaultValue = "1",
        },
        new()
        {
            Id = "EmuCore/GS/Renderer", Group = "Graphics", Label = "Renderer", Type = OptionType.Choice,
            Help = "How PCSX2 draws. Frame generation and the CRT effect need Vulkan.",
            Choices =
            [
                C("-1", "Automatic", "PCSX2 picks; that's Vulkan on a Steam Deck and most PCs."),
                C("14", "Vulkan", "Fast, and frame generation and the CRT effect work with it."),
                C("12", "OpenGL", "A fallback for drivers with Vulkan trouble. No frame generation or CRT effect."),
                C("13", "Software", "Slow but the most accurate. For games that glitch on the GPU renderers."),
            ],
            DefaultValue = "-1",
        },
        new()
        {
            Id = "EmuCore/GS/MaxAnisotropy", Group = "Graphics", Label = "Texture filtering", Type = OptionType.Choice,
            Help = "Keeps floors and walls sharp at an angle.",
            Choices = [C("0", "Off"), C("2", "2x"), C("4", "4x"), C("8", "8x"), C("16", "16x", "Sharpest. Cheap on most GPUs.")],
            DefaultValue = "0",
        },
        new()
        {
            Id = "EmuCore/GS/AspectRatio", Group = "Graphics", Label = "Picture shape", Type = OptionType.Choice,
            Help = "The shape the picture is shown in. Most PS2 games are 4:3; turn on widescreen patches for 16:9.",
            Choices =
            [
                C("Auto 4:3/3:2", "Automatic (4:3)", "The game's own shape."),
                C("4:3", "4:3"),
                C("16:9", "16:9", "For games running in widescreen (their own option or a widescreen patch)."),
                C("Stretch", "Stretch to fill", "Fills the screen; the picture is stretched."),
            ],
            DefaultValue = "Auto 4:3/3:2",
        },
        new()
        {
            Id = "EmuCore/GS/linear_present_mode", Group = "Graphics", Label = "Screen smoothing", Type = OptionType.Choice,
            Help = "How the picture is scaled up to your screen.",
            Choices = [C("1", "Smooth", "Soft edges."), C("2", "Sharp", "Crisp but even."), C("0", "Off", "Hard pixel edges.")],
            DefaultValue = "1",
        },
        new()
        {
            Id = "EmuCore/GS/VsyncEnable", Group = "Graphics", Label = "VSync", Type = OptionType.Bool,
            Help = "Waits for the screen's refresh to stop tearing.",
            OnDetail = "No tearing; a little more lag.", OffDetail = "Lowest input lag; may tear.",
            DefaultValue = "false",
        },
        new()
        {
            Id = "EmuCore/EnableWideScreenPatches", Group = "Game fixes", Label = "Widescreen patches", Type = OptionType.Bool, PerGame = false,
            Help = "Turns on PCSX2's widescreen patch for every game that has one. Switch it per game under the game's Patches & cheats.",
            OnDetail = "16:9 where a patch exists. Set the picture shape to 16:9 too.", OffDetail = "Games keep their own shape.",
            DefaultValue = "false",
        },
        new()
        {
            Id = "EmuCore/EnableNoInterlacingPatches", Group = "Game fixes", Label = "No-interlacing patches", Type = OptionType.Bool, PerGame = false,
            Help = "Turns on PCSX2's patch that removes interlacing shimmer, for games that have one.",
            OnDetail = "Steadier, sharper picture where a patch exists.", OffDetail = "Off.",
            DefaultValue = "false",
        },
        new()
        {
            Id = "EmuCore/EnableFastBoot", Group = "System", Label = "Skip the PS2 intro", Type = OptionType.Bool,
            Help = "Starts the game straight away instead of showing the PS2 start-up screens first.",
            OnDetail = "Straight into the game.", OffDetail = "Shows the PS2 start-up screens, as on the console.",
            DefaultValue = "true",
        },
    ];

    private OptionDefinition Check(string id) =>
        Options.FirstOrDefault(o => o.Id == id) ?? throw new ArgumentException($"Unknown PCSX2 option '{id}'");

    private static (string Section, string Key) Split(string id)
    {
        var slash = id.LastIndexOf('/');
        return (id[..slash], id[(slash + 1)..]);
    }

    /// <summary>The per-game file, or null while the game's CRC isn't known.</summary>
    public string? GameFile(string serial) => crcFor(serial) is { } crc ? paths().GameSettingsFile(serial, crc) : null;

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
                result[o.Id] = Normalize(o, v);
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

    /// <summary>"2.000000" -> "2", "True" -> "true", the way the choices are written.</summary>
    private static string Normalize(OptionDefinition o, string v)
    {
        if (o.Type == OptionType.Bool)
        {
            return v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1" ? "true" : "false";
        }
        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d == Math.Floor(d) && Math.Abs(d) < 1e6)
        {
            return ((long)d).ToString(CultureInfo.InvariantCulture);
        }
        return v;
    }

    /// <summary>One press for the heavy graphics settings.</summary>
    public static readonly PresetSet Performance = new("Performance preset",
        "Presets set resolution and texture filtering for this game in one go.",
        [
            new("battery", "Battery saver", "The PS2's own resolution. Longest battery life on a Steam Deck.",
                new Dictionary<string, string> { ["EmuCore/GS/upscale_multiplier"] = "1", ["EmuCore/GS/MaxAnisotropy"] = "0" }),
            new("balanced", "Balanced", "2x resolution (~720p) with sharp textures. The Steam Deck sweet spot.",
                new Dictionary<string, string> { ["EmuCore/GS/upscale_multiplier"] = "2", ["EmuCore/GS/MaxAnisotropy"] = "16" }),
            new("tv", "TV quality", "3x resolution (~1080p) with sharp textures, for a TV and a desktop-class GPU.",
                new Dictionary<string, string> { ["EmuCore/GS/upscale_multiplier"] = "3", ["EmuCore/GS/MaxAnisotropy"] = "16" }),
            new("sharp", "Sharp", "4x resolution (~1440p) for strong desktop GPUs.",
                new Dictionary<string, string> { ["EmuCore/GS/upscale_multiplier"] = "4", ["EmuCore/GS/MaxAnisotropy"] = "16" }),
        ],
        ["EmuCore/GS/upscale_multiplier", "EmuCore/GS/MaxAnisotropy"]);
}
