using EdenDeck.Core.Config;

namespace EdenDeck.Backends.Mgba;

/// <summary>
/// Gamepad buttons for mGBA. mGBA only maps face buttons for pads it knows by name, and the Steam
/// Virtual Gamepad (what Steam Input shows games in Game Mode) isn't one of them, so out of the box
/// only the d-pad and stick work. Scanline writes mGBA's own gamepad section
/// (<c>[gba.input.SDLB]</c>, SDL joystick indices) before each launch: A on the right face button
/// and B on the bottom, like a GBA, plus L/R on the bumpers, Select/Start on View/Menu, d-pad and
/// left stick for moving. mGBA's settings window shows the same mapping.
/// <para>
/// A mapping the player changed in mGBA is left alone: Scanline only (re)writes the section when
/// it's missing or still holds exactly what Scanline wrote last time.
/// </para>
/// </summary>
public static class MgbaPad
{
    public const string Section = "gba.input.SDLB";

    // Raw evdev layout of the Steam Virtual Gamepad and xpad pads (same as Eden's defaults):
    // A0 B1 X2 Y3 LB4 RB5 Back6 Start7; axes LX0 LY1; d-pad hat 0. GBA keys: A0 B1 Select2 Start3
    // Right4 Left5 Up6 Down7 R8 L9.
    public static readonly IReadOnlyList<KeyValuePair<string, string>> Defaults =
    [
        new("keyA", "1"),
        new("keyB", "0"),
        new("keySelect", "6"),
        new("keyStart", "7"),
        new("keyL", "4"),
        new("keyR", "5"),
        new("keyUp", "-1"),
        new("keyDown", "-1"),
        new("keyLeft", "-1"),
        new("keyRight", "-1"),
        new("hat0Up", "6"),
        new("hat0Right", "4"),
        new("hat0Down", "7"),
        new("hat0Left", "5"),
        new("axisLeftAxis", "-0"),
        new("axisLeftValue", "-16384"),
        new("axisRightAxis", "+0"),
        new("axisRightValue", "16384"),
        new("axisUpAxis", "-1"),
        new("axisUpValue", "-16384"),
        new("axisDownAxis", "+1"),
        new("axisDownValue", "16384"),
    ];

    /// <summary>Writes the default mapping unless the player has their own. Returns true when it wrote.</summary>
    public static bool Ensure(string configFile, string stampFile, string? device = null)
    {
        var ini = QtIni.Load(configFile, quoting: false);
        var changed = false;
        if (device != null && ini.Get(Section, "device0") != device)
        {
            // mGBA gives player 1 the pad with this SDL name, else the first one it finds.
            ini.Set(Section, "device0", device);
            changed = true;
        }
        var current = Fingerprint(ini.Entries(Section));
        var ours = File.Exists(stampFile) ? File.ReadAllText(stampFile) : null;
        var wanted = Fingerprint(Defaults);
        if (current == wanted || current.Length > 0 && current != ours)
        {
            if (changed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configFile)!);
                ini.Save(configFile);
            }
            return changed;  // already in place, or the player's own mapping
        }
        ini.RemoveWhere(Section, k => !IsDevice(k));
        foreach (var (k, v) in Defaults)
        {
            ini.Set(Section, k, v);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(configFile)!);
        ini.Save(configFile);
        Directory.CreateDirectory(Path.GetDirectoryName(stampFile)!);
        File.WriteAllText(stampFile, Fingerprint(Defaults));
        return true;
    }

    private static bool IsDevice(string key) => key.StartsWith("device", StringComparison.Ordinal);

    private static string Fingerprint(IEnumerable<KeyValuePair<string, string>> entries) =>
        string.Join("\n", entries.Where(e => !IsDevice(e.Key)).OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => e.Key + "=" + e.Value));
}
