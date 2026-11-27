namespace EdenDeck.Backends.Pcsx2;

/// <summary>
/// Player 1's controller for PCSX2. Out of the box PCSX2 maps player 1 to the keyboard, so on a
/// Steam Deck nothing responds. Before each launch Scanline maps <c>[Pad1]</c> in PCSX2.ini to the
/// first SDL controller, with PCSX2's own SDL layout (by position: Cross on the bottom face button,
/// Circle on the right, L1/R1 bumpers, L2/R2 triggers, Select/Start on View/Menu, sticks and
/// rumble). Only when player 1 has no buttons yet or is still on the keyboard: a pad the player
/// mapped in PCSX2 is left alone.
/// </summary>
public static class Pcsx2Pads
{
    public const string Device = "SDL-0";

    public static readonly IReadOnlyList<(string Key, string Input)> Layout =
    [
        ("Up", "DPadUp"), ("Right", "DPadRight"), ("Down", "DPadDown"), ("Left", "DPadLeft"),
        ("Triangle", "FaceNorth"), ("Circle", "FaceEast"), ("Cross", "FaceSouth"), ("Square", "FaceWest"),
        ("Select", "Back"), ("Start", "Start"),
        ("L1", "LeftShoulder"), ("L2", "+LeftTrigger"), ("R1", "RightShoulder"), ("R2", "+RightTrigger"),
        ("L3", "LeftStick"), ("R3", "RightStick"),
        ("LUp", "-LeftY"), ("LRight", "+LeftX"), ("LDown", "+LeftY"), ("LLeft", "-LeftX"),
        ("RUp", "-RightY"), ("RRight", "+RightX"), ("RDown", "+RightY"), ("RLeft", "-RightX"),
        ("LargeMotor", "LargeMotor"), ("SmallMotor", "SmallMotor"),
    ];

    /// <summary>True when player 1 has no button mapping, or only keyboard keys.</summary>
    public static bool Untouched(Pcsx2Ini ini) => Free(ini, "Pad1", Layout, t => t == "DualShock2");

    /// <summary>Maps player 1 to the first controller unless the player has their own mapping. True when it wrote.</summary>
    public static bool Ensure(Pcsx2Paths paths) => Ensure(paths, [0]);

    /// <summary>
    /// Maps Pad1, Pad2 to the players' controllers (SDL-N, player 1 first). A pad still on the
    /// keyboard, or on a controller with Scanline's layout, is (re)pointed; anything else is the
    /// player's own. Pad2 goes back to "None" when its controller is gone. True when it wrote.
    /// </summary>
    public static bool Ensure(Pcsx2Paths paths, IReadOnlyList<int> sdlIndices)
    {
        var ini = paths.LoadConfigForWrite();
        if (!EnsurePlayers(ini, sdlIndices, Layout, "DualShock2", t => t == "DualShock2"))
        {
            return false;
        }
        ini.Save(paths.ConfigFile);
        return true;
    }

    internal static void Write(Pcsx2Ini ini) => Write(ini, "Pad1", 0, Layout, "DualShock2");

    public const int MaxPads = 2;  // without a multitap

    /// <summary>Shared with DuckStation, which uses the same ini format and input names.</summary>
    public static bool EnsurePlayers(Pcsx2Ini ini, IReadOnlyList<int> sdlIndices, IReadOnlyList<(string Key, string Input)> layout, string padType, Func<string, bool> ourType)
    {
        var changed = false;
        for (var i = 0; i < MaxPads; i++)
        {
            var section = $"Pad{i + 1}";
            bool TypeOk(string? t) => t == null || ourType(t) || i > 0 && t == "None";
            if (i < sdlIndices.Count)
            {
                var want = Bindings(sdlIndices[i], layout);
                if (layout.All(l => ini.Get(section, l.Key) == want[l.Key]) && ourType(ini.Get(section, "Type") ?? ""))
                {
                    continue;
                }
                if (Free(ini, section, layout, TypeOk) || Ours(ini, section, layout, TypeOk))
                {
                    Write(ini, section, sdlIndices[i], layout, padType);
                    changed = true;
                }
            }
            else if (i > 0 && ourType(ini.Get(section, "Type") ?? "") && Ours(ini, section, layout, TypeOk))
            {
                ini.Set(section, "Type", "None");
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>No buttons, or only keyboard keys.</summary>
    public static bool Free(Pcsx2Ini ini, string section, IReadOnlyList<(string Key, string Input)> layout, Func<string, bool> typeOk)
    {
        var type = ini.Get(section, "Type");
        if (type != null && !typeOk(type))
        {
            return false;  // a guitar, a mouse, a light gun...: the player chose it
        }
        var bound = layout.Select(l => ini.Get(section, l.Key)).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        return bound.All(v => v!.StartsWith("Keyboard/", StringComparison.Ordinal));
    }

    /// <summary>Exactly Scanline's layout on one SDL controller (only the controller number may differ).</summary>
    public static bool Ours(Pcsx2Ini ini, string section, IReadOnlyList<(string Key, string Input)> layout, Func<string, bool> typeOk)
    {
        var type = ini.Get(section, "Type");
        if (type != null && !typeOk(type))
        {
            return false;
        }
        var first = ini.Get(section, layout[0].Key);
        if (first == null || !first.StartsWith("SDL-", StringComparison.Ordinal) || first.IndexOf('/') is var slash && slash < 0 ||
            !int.TryParse(first[4..slash], out var n))
        {
            return false;
        }
        var want = Bindings(n, layout);
        return layout.All(l => ini.Get(section, l.Key) == want[l.Key]);
    }

    private static Dictionary<string, string> Bindings(int sdl, IReadOnlyList<(string Key, string Input)> layout) =>
        layout.ToDictionary(l => l.Key, l => $"SDL-{sdl}/{l.Input}");

    private static void Write(Pcsx2Ini ini, string section, int sdl, IReadOnlyList<(string Key, string Input)> layout, string padType)
    {
        ini.Set("InputSources", "SDL", "true");
        ini.Set(section, "Type", padType);
        foreach (var (key, input) in layout)
        {
            ini.Set(section, key, $"SDL-{sdl}/{input}");
        }
    }
}
