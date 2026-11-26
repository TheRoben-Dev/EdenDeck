using EdenDeck.Core.Config;

namespace EdenDeck.Backends.MelonDs;

/// <summary>
/// Gamepad buttons for melonDS. melonDS maps nothing for a gamepad out of the box, so Scanline
/// writes its first instance's joystick table (SDL joystick indices, as melonDS stores them) before
/// each launch: A on the right face button and B on the bottom, X on top and Y on the left, like a
/// DS; L/R on the bumpers, Select/Start on View/Menu, d-pad and left stick for moving; right stick
/// click swaps the screens and left stick click blows into the microphone. melonDS's own input
/// settings show the same mapping.
/// <para>
/// A mapping the player changed in melonDS is left alone: Scanline only (re)writes it when it's
/// empty or still holds exactly what Scanline wrote last time.
/// </para>
/// </summary>
public static class MelonDsPad
{
    public const string Table = "Instance0.Joystick";

    // Raw layout of the Steam Virtual Gamepad and xpad pads: A0 B1 X2 Y3 LB4 RB5 Back6 Start7
    // LS9 RS10; axes LX0 LY1; d-pad hat 0. melonDS packs a hat direction as 0x100 | hat << 4 | dir
    // (1 up, 2 right, 4 down, 8 left) and an axis as 0x10000 | axis << 24 | dir << 20 (0 plus,
    // 1 minus) in the same number, so the d-pad and the stick both move.
    private const int HatUp = 0x101, HatRight = 0x102, HatDown = 0x104, HatLeft = 0x108;
    private static int Axis(int axis, bool minus) => 0x10000 | axis << 24 | (minus ? 1 : 0) << 20;

    public static readonly IReadOnlyList<KeyValuePair<string, int>> Defaults =
    [
        new("A", 1),
        new("B", 0),
        new("X", 3),
        new("Y", 2),
        new("L", 4),
        new("R", 5),
        new("Select", 6),
        new("Start", 7),
        new("Up", HatUp | Axis(1, true)),
        new("Down", HatDown | Axis(1, false)),
        new("Left", HatLeft | Axis(0, true)),
        new("Right", HatRight | Axis(0, false)),
        new("HK_SwapScreens", 10),
        new("HK_Mic", 9),
    ];

    /// <summary>Writes the default mapping unless the player has their own. Returns true when it wrote.</summary>
    public static bool Ensure(TomlFile toml, string stampFile)
    {
        var current = Fingerprint(Defaults.Select(d => new KeyValuePair<string, int>(d.Key, int.TryParse(toml.Get(Table + "." + d.Key), out var v) ? v : -1)));
        var wanted = Fingerprint(Defaults);
        var ours = File.Exists(stampFile) ? File.ReadAllText(stampFile) : null;
        var empty = Defaults.All(d => toml.Get(Table + "." + d.Key) is null or "-1");
        if (current == wanted || !empty && current != ours)
        {
            return false;  // already in place, or the player's own mapping
        }
        foreach (var (k, v) in Defaults)
        {
            toml.SetInt(Table + "." + k, v);
        }
        toml.SetInt("Instance0.JoystickID", 0);
        Directory.CreateDirectory(Path.GetDirectoryName(stampFile)!);
        File.WriteAllText(stampFile, wanted);
        return true;
    }

    private static string Fingerprint(IEnumerable<KeyValuePair<string, int>> entries) =>
        string.Join("\n", entries.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => e.Key + "=" + e.Value));
}
