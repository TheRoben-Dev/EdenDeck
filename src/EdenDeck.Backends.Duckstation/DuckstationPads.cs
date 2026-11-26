using EdenDeck.Backends.Pcsx2;

namespace EdenDeck.Backends.Duckstation;

/// <summary>
/// Player 1's controller for DuckStation. Out of the box DuckStation maps player 1 to the keyboard,
/// so on a Steam Deck nothing responds. Before each launch Scanline maps <c>[Pad1]</c> in
/// settings.ini to the first SDL controller as a DualShock (DuckStation's "AnalogController"),
/// by position: Cross on the bottom face button, Circle on the right, L1/R1 bumpers, L2/R2
/// triggers, Select/Start on View/Menu, both sticks and rumble. Only when player 1 has no buttons
/// yet or is still on the keyboard: a pad the player mapped in DuckStation is left alone.
/// </summary>
public static class DuckstationPads
{
    public const string Device = "SDL-0";
    public const string PadType = "AnalogController";

    public static readonly IReadOnlyList<(string Key, string Input)> Layout =
    [
        ("Up", "DPadUp"), ("Right", "DPadRight"), ("Down", "DPadDown"), ("Left", "DPadLeft"),
        ("Triangle", "Y"), ("Circle", "B"), ("Cross", "A"), ("Square", "X"),
        ("Select", "Back"), ("Start", "Start"),
        ("L1", "LeftShoulder"), ("L2", "+LeftTrigger"), ("R1", "RightShoulder"), ("R2", "+RightTrigger"),
        ("L3", "LeftStick"), ("R3", "RightStick"),
        ("LUp", "-LeftY"), ("LRight", "+LeftX"), ("LDown", "+LeftY"), ("LLeft", "-LeftX"),
        ("RUp", "-RightY"), ("RRight", "+RightX"), ("RDown", "+RightY"), ("RLeft", "-RightX"),
        ("LargeMotor", "LargeMotor"), ("SmallMotor", "SmallMotor"),
    ];

    /// <summary>True when player 1 has no button mapping, or only keyboard keys.</summary>
    public static bool Untouched(Pcsx2Ini ini)
    {
        var type = ini.Get("Pad1", "Type");
        if (type != null && type is not (PadType or "DigitalController"))
        {
            return false;  // a mouse, a light gun, a jogcon...: the player chose it
        }
        var bound = Layout.Select(l => ini.Get("Pad1", l.Key)).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        return bound.All(v => v!.StartsWith("Keyboard/", StringComparison.Ordinal));
    }

    /// <summary>Maps player 1 to the first controller unless the player has their own mapping. True when it wrote.</summary>
    public static bool Ensure(DuckstationPaths paths)
    {
        var ini = paths.LoadConfigForWrite();
        if (!Untouched(ini))
        {
            return false;
        }
        Write(ini);
        ini.Save(paths.ConfigFile);
        return true;
    }

    internal static void Write(Pcsx2Ini ini)
    {
        ini.Set("InputSources", "SDL", "true");
        ini.Set("Pad1", "Type", PadType);
        foreach (var (key, input) in Layout)
        {
            ini.Set("Pad1", key, $"{Device}/{input}");
        }
    }
}
