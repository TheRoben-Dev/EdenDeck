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
public static partial class EdenControllerDefaults
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
        WriteDefaults(ini, 0, EdenGuid(pad.Guid), port: 0);
        ini.Save(configFile);
        return true;
    }

    public const int MaxPlayers = 8;

    /// <summary>
    /// Points Eden's players at <paramref name="players"/> (player 1 first), so the controller
    /// plugged into the dock is player 1 instead of whatever SDL listed first. Each player keeps
    /// its button choices: only the device (guid and port) in its SDL params changes. A player
    /// still on the keyboard gets player 1's layout (or Eden's auto map). A player mapped in Eden
    /// to a pad Scanline doesn't know is left alone. Players past the connected pads that point at
    /// a known pad are disconnected. Returns what changed, one line per player (empty: nothing).
    /// </summary>
    public static IReadOnlyList<string> AssignPlayers(string configFile, IReadOnlyList<Core.Input.Gamepad> players)
    {
        var changes = new List<string>();
        if (players.Count == 0)
        {
            return changes;
        }
        var ini = QtIni.Load(configFile);
        for (var i = 0; i < MaxPlayers; i++)
        {
            var prefix = $"player_{i}_";
            if (i >= players.Count)
            {
                if (i > 0 && ini.Get(Section, prefix + "connected") == "true" && IsKnownSdl(ini.Get(Section, prefix + "button_a")))
                {
                    SetValue(ini, prefix + "connected", "false");
                    changes.Add($"player {i + 1}: disconnected");
                }
                continue;
            }
            var pad = players[i];
            var info = new PadInfo(pad.Guid, pad.Name, pad.Vendor, pad.Product);
            if (!HasKnownLayout(info) && !HasKnownLayout(info with { Vendor = pad.RawVendor, Product = pad.RawProduct }))
            {
                changes.Add($"player {i + 1}: {pad.Name} has a button layout Scanline doesn't know; map it once in Eden → Controls");
                continue;
            }
            var guid = EdenGuid(pad.Guid);
            var a = ini.Get(Section, prefix + "button_a");
            var before = Snapshot(ini, prefix);
            if (string.IsNullOrEmpty(a) || a.Contains("engine:keyboard", StringComparison.Ordinal))
            {
                if (i > 0 && IsKnownSdl(ini.Get(Section, "player_0_button_a")))
                {
                    CopyPlayer(ini, 0, i);  // same buttons as player 1 (including remaps from Settings → Buttons)
                }
                else
                {
                    WriteDefaults(ini, i, guid, pad.GuidPort);
                }
            }
            else if (!IsKnownSdl(a))
            {
                continue;  // the player's own mapping to another device
            }
            Retarget(ini, prefix, guid, pad.GuidPort);
            SetValue(ini, prefix + "connected", "true");
            if (Snapshot(ini, prefix) != before)
            {
                changes.Add($"player {i + 1}: {pad.Name}");
            }
        }
        if (changes.Count > 0)
        {
            ini.Save(configFile);
        }
        return changes;
    }

    /// <summary>
    /// Points an input profile (config/input/&lt;name&gt;.ini, player keys without "player_0_") at
    /// player 1's pad: Eden loads a game's profile over player 1 when the game starts.
    /// </summary>
    public static bool RetargetProfile(string profileFile, Core.Input.Gamepad pad)
    {
        if (!File.Exists(profileFile))
        {
            return false;
        }
        var ini = QtIni.Load(profileFile);
        if (!IsKnownSdl(ini.Get(Section, "button_a")))
        {
            return false;
        }
        var before = Snapshot(ini, "");
        Retarget(ini, "", EdenGuid(pad.Guid), pad.GuidPort);
        if (Snapshot(ini, "") == before)
        {
            return false;
        }
        ini.Save(profileFile);
        return true;
    }

    /// <summary>An SDL param for a pad with the Steam Virtual Gamepad / xpad layout (what Scanline writes).</summary>
    private static bool IsKnownSdl(string? param)
    {
        if (string.IsNullOrEmpty(param) || !param.Contains("engine:sdl", StringComparison.Ordinal))
        {
            return false;
        }
        var guid = GuidPart().Match(param);
        return guid.Success && GuidIds(guid.Groups[1].Value) is var (v, p) && IsKnown(v, p);
    }

    private static string Snapshot(QtIni ini, string prefix) =>
        string.Join("\n", ini.Entries(Section).Where(e => e.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(e => e.Key + "=" + e.Value));

    /// <summary>Swaps the device in every SDL param of a player, leaving which button/axis it reads alone.</summary>
    private static void Retarget(QtIni ini, string prefix, string guid, int port)
    {
        foreach (var (key, value) in ini.Entries(Section).Where(e => e.Key.StartsWith(prefix, StringComparison.Ordinal) && !e.Key.EndsWith("\\default", StringComparison.Ordinal)).ToList())
        {
            if (!value.Contains("engine:sdl", StringComparison.Ordinal))
            {
                continue;
            }
            var swapped = PortPart().Replace(GuidPart().Replace(value, "${pre}guid:" + guid), "${pre}port:" + port.ToString(CultureInfo.InvariantCulture));
            if (swapped != value)
            {
                ini.Set(Section, key, swapped);
            }
        }
    }

    private static void CopyPlayer(QtIni ini, int from, int to)
    {
        var src = $"player_{from}_";
        foreach (var (key, value) in ini.Entries(Section).Where(e => e.Key.StartsWith(src, StringComparison.Ordinal)).ToList())
        {
            var bare = key[src.Length..];
            if (bare.StartsWith("button_", StringComparison.Ordinal) || bare.StartsWith("lstick", StringComparison.Ordinal) || bare.StartsWith("rstick", StringComparison.Ordinal))
            {
                ini.Set(Section, $"player_{to}_" + bare, value);
            }
        }
    }

    private static void WriteDefaults(QtIni ini, int player, string guid, int port)
    {
        foreach (var (key, value) in Mapping(guid, port, player))
        {
            SetValue(ini, key, value);
        }
        SetValue(ini, $"player_{player}_connected", "true");
    }

    private static void SetValue(QtIni ini, string key, string value)
    {
        ini.Set(Section, key + "\\default", "false");
        ini.Set(Section, key, value);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(?<pre>^|,|"")guid:([0-9a-fA-F]{32})")]
    private static partial System.Text.RegularExpressions.Regex GuidPart();

    [System.Text.RegularExpressions.GeneratedRegex(@"(?<pre>^|,|"")port:\d+")]
    private static partial System.Text.RegularExpressions.Regex PortPart();

    /// <summary>Raw evdev layout (SDL joystick indices): A0 B1 X2 Y3 LB4 RB5 Back6 Start7 Guide8 LS9 RS10; axes LX0 LY1 LT2 RX3 RY4 RT5; d-pad hat 0.</summary>
    internal static IEnumerable<(string Key, string Value)> Mapping(string guid, int port, int player = 0)
    {
        var n = $"player_{player}_";
        string P(string rest) => $"engine:sdl,port:{port},guid:{guid},{rest}";
        string Button(int b) => P($"button:{b}");
        string Hat(string dir) => P($"hat:0,direction:{dir}");
        string Trigger(int axis) => P($"axis:{axis},threshold:0.5,invert:+");
        string Stick(int x, int y) => P(string.Create(CultureInfo.InvariantCulture,
            $"axis_x:{x},axis_y:{y},offset_x:0,offset_y:0,invert_x:+,invert_y:+,deadzone:0.15,range:1"));

        // Switch buttons by position, like Eden's auto map: A = east, B = south, X = north, Y = west.
        yield return (n + "button_a", Button(1));
        yield return (n + "button_b", Button(0));
        yield return (n + "button_x", Button(3));
        yield return (n + "button_y", Button(2));
        yield return (n + "button_lstick", Button(9));
        yield return (n + "button_rstick", Button(10));
        yield return (n + "button_l", Button(4));
        yield return (n + "button_r", Button(5));
        yield return (n + "button_zl", Trigger(2));
        yield return (n + "button_zr", Trigger(5));
        yield return (n + "button_plus", Button(7));
        yield return (n + "button_minus", Button(6));
        yield return (n + "button_dleft", Hat("left"));
        yield return (n + "button_dup", Hat("up"));
        yield return (n + "button_dright", Hat("right"));
        yield return (n + "button_ddown", Hat("down"));
        yield return (n + "button_home", Button(8));
        yield return (n + "lstick", Stick(0, 1));
        yield return (n + "rstick", Stick(3, 4));
    }
}
