using System.Globalization;

namespace EdenDeck.Backends.Eden.Config;

/// <summary>A Switch button as Eden stores it for player 1 ([Controls] player_0_&lt;Key&gt;).</summary>
public sealed record SwitchButton(string Key, string Label);

/// <summary>A button, trigger or d-pad direction on the player's pad, by position (raw SDL layout of known pads).</summary>
public sealed record PadInput(string Id, string Label);

/// <summary>
/// Button mapping for player 1, for games only (the app's own navigation never changes).
/// Works on pads whose raw SDL layout is known (<see cref="EdenControllerDefaults.HasKnownLayout"/>):
/// the Steam Virtual Gamepad that Steam Input presents in Game Mode, and Xbox-style pads. Each
/// Switch button is pointed at one pad input, written in Eden's own param format, so Eden's
/// Controls page shows the same mapping. Anything Scanline can't express (a keyboard key, a pad
/// it doesn't know, motion, an analog modifier) is shown as "Set in Eden" and left alone unless
/// the player picks something else for that button.
/// </summary>
public static class EdenButtonMap
{
    private const string Section = "Controls";

    public const string None = "none";
    public const string Other = "other";

    public static readonly IReadOnlyList<SwitchButton> Buttons =
    [
        new("button_a", "A"),
        new("button_b", "B"),
        new("button_x", "X"),
        new("button_y", "Y"),
        new("button_l", "L"),
        new("button_r", "R"),
        new("button_zl", "ZL"),
        new("button_zr", "ZR"),
        new("button_plus", "+ (Plus)"),
        new("button_minus", "− (Minus)"),
        new("button_lstick", "Left stick press"),
        new("button_rstick", "Right stick press"),
        new("button_dup", "D-pad up"),
        new("button_ddown", "D-pad down"),
        new("button_dleft", "D-pad left"),
        new("button_dright", "D-pad right"),
        new("button_home", "Home"),
        new("button_screenshot", "Capture"),
    ];

    /// <summary>Pad inputs by position. Labels say where the button is, since glyphs differ per pad.</summary>
    public static readonly IReadOnlyList<PadInput> Inputs =
    [
        new("south", "Bottom face button"),
        new("east", "Right face button"),
        new("west", "Left face button"),
        new("north", "Top face button"),
        new("lb", "Left bumper"),
        new("rb", "Right bumper"),
        new("lt", "Left trigger"),
        new("rt", "Right trigger"),
        new("back", "View / Select"),
        new("start", "Menu / Start"),
        new("guide", "Guide / Steam button"),
        new("ls", "Left stick press"),
        new("rs", "Right stick press"),
        new("dup", "D-pad up"),
        new("ddown", "D-pad down"),
        new("dleft", "D-pad left"),
        new("dright", "D-pad right"),
        new(None, "Nothing"),
    ];

    // Raw evdev indices shared by the Steam Virtual Gamepad and xpad (see EdenControllerDefaults.Mapping).
    private static readonly Dictionary<string, int> RawButtons = new()
    {
        ["south"] = 0, ["east"] = 1, ["west"] = 2, ["north"] = 3, ["lb"] = 4, ["rb"] = 5,
        ["back"] = 6, ["start"] = 7, ["guide"] = 8, ["ls"] = 9, ["rs"] = 10,
    };

    private static readonly Dictionary<string, int> RawTriggers = new() { ["lt"] = 2, ["rt"] = 5 };

    private static readonly Dictionary<string, string> Hats = new()
    {
        ["dup"] = "up", ["ddown"] = "down", ["dleft"] = "left", ["dright"] = "right",
    };

    /// <summary>Eden's own auto map: Switch buttons by position (A is the right face button, like on a Switch).</summary>
    public static readonly IReadOnlyDictionary<string, string> PositionLayout = new Dictionary<string, string>
    {
        ["button_a"] = "east", ["button_b"] = "south", ["button_x"] = "north", ["button_y"] = "west",
        ["button_l"] = "lb", ["button_r"] = "rb", ["button_zl"] = "lt", ["button_zr"] = "rt",
        ["button_plus"] = "start", ["button_minus"] = "back", ["button_lstick"] = "ls", ["button_rstick"] = "rs",
        ["button_dup"] = "dup", ["button_ddown"] = "ddown", ["button_dleft"] = "dleft", ["button_dright"] = "dright",
        ["button_home"] = "guide", ["button_screenshot"] = None,
    };

    /// <summary>Switch buttons by the letter printed on an Xbox-style pad (A is the bottom face button).</summary>
    public static readonly IReadOnlyDictionary<string, string> LabelLayout = new Dictionary<string, string>(PositionLayout)
    {
        ["button_a"] = "south", ["button_b"] = "east", ["button_x"] = "north", ["button_y"] = "west",
    };

    /// <summary>The pad input this param refers to, <see cref="None"/> if unmapped, or <see cref="Other"/> if Scanline can't show it.</summary>
    public static string InputOf(string? param)
    {
        if (string.IsNullOrWhiteSpace(param) || param.Trim() == "[empty]")
        {
            return None;
        }
        var p = Parse(param);
        if (p.GetValueOrDefault("engine") != "sdl" || p.ContainsKey("modifier"))
        {
            return Other;
        }
        if (p.TryGetValue("button", out var b) && int.TryParse(b, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return RawButtons.FirstOrDefault(kv => kv.Value == n).Key ?? Other;
        }
        if (p.TryGetValue("hat", out var hat) && hat == "0" && p.TryGetValue("direction", out var dir))
        {
            return Hats.FirstOrDefault(kv => kv.Value == dir).Key ?? Other;
        }
        if (p.TryGetValue("axis", out var a) && int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out var axis) &&
            p.GetValueOrDefault("invert", "+") == "+")
        {
            return RawTriggers.FirstOrDefault(kv => kv.Value == axis).Key ?? Other;
        }
        return Other;
    }

    /// <summary>Eden's param for a pad input on this pad (empty for <see cref="None"/>).</summary>
    public static string ParamFor(string input, string edenGuid, int port = 0)
    {
        var head = $"engine:sdl,port:{port.ToString(CultureInfo.InvariantCulture)},guid:{edenGuid},";
        if (input == None)
        {
            return "";
        }
        if (RawButtons.TryGetValue(input, out var b))
        {
            return head + $"button:{b.ToString(CultureInfo.InvariantCulture)}";
        }
        if (RawTriggers.TryGetValue(input, out var t))
        {
            return head + $"axis:{t.ToString(CultureInfo.InvariantCulture)},threshold:0.5,invert:+";
        }
        if (Hats.TryGetValue(input, out var d))
        {
            return head + $"hat:0,direction:{d}";
        }
        throw new ArgumentException($"Unknown pad input: {input}");
    }

    /// <summary>Player 1's current mapping, one pad input id per Switch button key.</summary>
    public static Dictionary<string, string> Read(string configFile)
    {
        var ini = QtIni.Load(configFile);
        if (EdenControllerDefaults.NeedsMapping(ini))
        {
            // Nothing set up yet: the first launch writes Eden's auto map, so that's what games get.
            return new Dictionary<string, string>(PositionLayout);
        }
        return Buttons.ToDictionary(b => b.Key, b => InputOf(ini.Get(Section, "player_0_" + b.Key)));
    }

    /// <summary>Which preset the mapping matches, if any ("position", "label" or null for custom).</summary>
    public static string? PresetOf(IReadOnlyDictionary<string, string> map) =>
        Same(map, PositionLayout) ? "position" : Same(map, LabelLayout) ? "label" : null;

    private static bool Same(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
        Buttons.All(x => a.GetValueOrDefault(x.Key) == b.GetValueOrDefault(x.Key));

    /// <summary>
    /// Points one Switch button at a pad input. If player 1 had no pad mapping yet, Eden's auto map
    /// is written first so the other buttons work too. Sticks and motion are never touched.
    /// </summary>
    public static void Set(string configFile, PadInfo pad, string buttonKey, string input) =>
        Apply(configFile, pad, new Dictionary<string, string> { [buttonKey] = input });

    /// <summary>Applies a whole layout (for example <see cref="LabelLayout"/>), button by button.</summary>
    public static void Apply(string configFile, PadInfo pad, IReadOnlyDictionary<string, string> layout)
    {
        if (!EdenControllerDefaults.HasKnownLayout(pad))
        {
            throw new InvalidOperationException($"Scanline doesn't know the button layout of \"{pad.Name}\". Map its buttons in Eden instead.");
        }
        if (layout.Keys.FirstOrDefault(k => Buttons.All(b => b.Key != k)) is { } unknown)
        {
            throw new ArgumentException($"Not a Switch button: {unknown}");
        }
        EdenControllerDefaults.EnsurePlayer1(configFile, pad);
        var ini = QtIni.Load(configFile);
        var guid = EdenControllerDefaults.EdenGuid(pad.Guid);
        foreach (var (key, input) in layout)
        {
            ini.Set(Section, "player_0_" + key + "\\default", "false");
            ini.Set(Section, "player_0_" + key, ParamFor(input, guid));
        }
        ini.Save(configFile);
    }

    private static Dictionary<string, string> Parse(string param)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in param.Trim().Trim('"').Split(','))
        {
            var i = part.IndexOf(':');
            if (i > 0)
            {
                d[part[..i]] = part[(i + 1)..];
            }
        }
        return d;
    }
}
