using System.Globalization;

namespace EdenDeck.Backends.Eden.Config;

/// <summary>A gamepad as the frontend sees it (Godot reports the same SDL-style GUID Eden uses).</summary>
public sealed record PadInfo(string Guid, string Name, int Vendor, int Product);

/// <summary>
/// Eden maps player 1 to the keyboard until someone configures a controller in its Qt UI, so a
/// game launched from Game Mode gets no pad input. When player 1 still has the keyboard (or
/// nothing) mapped and the active pad is one whose raw SDL layout is known (Steam Virtual
/// Gamepad, Xbox-style xpad pads), this writes the same Pro Controller mapping Eden's own
/// "auto map" would. A mapping the user made in Eden is never touched.
/// </summary>
public static class EdenControllerDefaults
{
    private const string Section = "Controls";

    /// <summary>Steam Virtual Gamepad (Steam Input, Game Mode) and Xbox 360/One pads on xpad share one layout.</summary>
    /// <remarks>
    /// Match on the IDs inside the SDL GUID too: that is what Eden's SDL sees. On a Steam Deck in
    /// Game Mode the frontend reported "#SettingsController_SteamController" [28de:12fd] while the
    /// GUID (and evdev device) is the Steam Virtual Gamepad 28de:11ff.
    /// </remarks>
    public static bool HasKnownLayout(PadInfo pad)
    {
        var ids = GuidIds(pad.Guid);
        return IsKnown(pad.Vendor, pad.Product) ||
               (ids is var (v, p) && IsKnown(v, p)) ||
               pad.Name.Contains("Steam Virtual Gamepad", StringComparison.OrdinalIgnoreCase) ||
               pad.Name.Contains("Xbox", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKnown(int vendor, int product) =>
        (vendor == 0x28de && product == 0x11ff) || vendor == 0x045e;

    /// <summary>Vendor/product from an SDL GUID (bus 16-bit LE, crc, vendor LE, 0, product LE, 0, version LE, ...).</summary>
    public static (int Vendor, int Product)? GuidIds(string sdlGuid)
    {
        var g = sdlGuid.Trim().ToLowerInvariant();
        if (g.Length != 32 || !g.All(Uri.IsHexDigit) || g[12..16] != "0000" || g[20..24] != "0000")
        {
            return null;
        }
        int Le16(int at) => int.Parse(g.Substring(at + 2, 2) + g.Substring(at, 2), NumberStyles.HexNumber);
        return (Le16(8), Le16(16));
    }

    /// <summary>Eden clears the name CRC (bytes 2-3) of the SDL GUID; do the same.</summary>
    public static string EdenGuid(string sdlGuid)
    {
        var g = sdlGuid.Trim().ToLowerInvariant();
        if (g.Length != 32 || !g.All(Uri.IsHexDigit))
        {
            throw new ArgumentException($"not an SDL GUID: {sdlGuid}");
        }
        return g[..4] + "0000" + g[8..];
    }

    /// <summary>True when player 1 has no controller mapping yet (keyboard defaults or empty).</summary>
    public static bool NeedsMapping(QtIni ini)
    {
        var a = ini.Get(Section, "player_0_button_a");
        return string.IsNullOrEmpty(a) || a.Contains("engine:keyboard", StringComparison.Ordinal);
    }

    /// <summary>Writes the default mapping if needed. Returns true if the config changed.</summary>
    public static bool EnsurePlayer1(string configFile, PadInfo pad)
    {
        if (!HasKnownLayout(pad))
        {
            return false;
        }
        var ini = QtIni.Load(configFile);
        if (!NeedsMapping(ini))
        {
            return false;
        }
        foreach (var (key, value) in Mapping(EdenGuid(pad.Guid), port: 0))
        {
            ini.Set(Section, key + "\\default", "false");
            ini.Set(Section, key, value);
        }
        ini.Set(Section, "player_0_connected\\default", "false");
        ini.Set(Section, "player_0_connected", "true");
        ini.Save(configFile);
        return true;
    }

    /// <summary>Raw evdev layout (SDL joystick indices): A0 B1 X2 Y3 LB4 RB5 Back6 Start7 Guide8 LS9 RS10; axes LX0 LY1 LT2 RX3 RY4 RT5; d-pad hat 0.</summary>
    internal static IEnumerable<(string Key, string Value)> Mapping(string guid, int port)
    {
        string P(string rest) => $"engine:sdl,port:{port},guid:{guid},{rest}";
        string Button(int b) => P($"button:{b}");
        string Hat(string dir) => P($"hat:0,direction:{dir}");
        string Trigger(int axis) => P($"axis:{axis},threshold:0.5,invert:+");
        string Stick(int x, int y) => P(string.Create(CultureInfo.InvariantCulture,
            $"axis_x:{x},axis_y:{y},offset_x:0,offset_y:0,invert_x:+,invert_y:+,deadzone:0.15,range:1"));

        // Switch buttons by position, like Eden's auto map: A = east, B = south, X = north, Y = west.
        yield return ("player_0_button_a", Button(1));
        yield return ("player_0_button_b", Button(0));
        yield return ("player_0_button_x", Button(3));
        yield return ("player_0_button_y", Button(2));
        yield return ("player_0_button_lstick", Button(9));
        yield return ("player_0_button_rstick", Button(10));
        yield return ("player_0_button_l", Button(4));
        yield return ("player_0_button_r", Button(5));
        yield return ("player_0_button_zl", Trigger(2));
        yield return ("player_0_button_zr", Trigger(5));
        yield return ("player_0_button_plus", Button(7));
        yield return ("player_0_button_minus", Button(6));
        yield return ("player_0_button_dleft", Hat("left"));
        yield return ("player_0_button_dup", Hat("up"));
        yield return ("player_0_button_dright", Hat("right"));
        yield return ("player_0_button_ddown", Hat("down"));
        yield return ("player_0_button_home", Button(8));
        yield return ("player_0_lstick", Stick(0, 1));
        yield return ("player_0_rstick", Stick(3, 4));
    }
}
