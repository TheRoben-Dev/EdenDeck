namespace EdenDeck.Backends.Rpcs3;

/// <summary>
/// Player 1's controller for RPCS3. With no pad settings RPCS3 falls back to the keyboard, so on a
/// Steam Deck nothing responds. Before each launch Scanline points player 1 at the controller the
/// app itself is using, through RPCS3's SDL handler, with RPCS3's own SDL button layout (by
/// position: Cross on the bottom face button, Circle on the right, L1/R1 bumpers, L2/R2 triggers,
/// Select/Start on View/Menu, PS on the guide button or View + Menu).
/// <para>
/// Written to the active global input profile (<c>input_configs/global/&lt;profile&gt;.yml</c>),
/// so RPCS3's Pads window shows it. Only player 1's block changes, and only when there's no setup
/// yet, it's still the keyboard/null default, or it's what Scanline wrote last time (the pad was
/// swapped). A pad the player set up in RPCS3, and per-game pad profiles, are left alone.
/// </para>
/// <para>
/// The device name is SDL's gamepad name plus a number (RPCS3 numbers pads that share a name from
/// 1). Scanline's own UI reads pads through SDL too, so it sees the same names.
/// </para>
/// </summary>
public static class Rpcs3Pads
{
    public const string Handler = "SDL";
    private const string Block = "Player 1 Input:";

    /// <summary>RPCS3's device name for the first connected pad, or null when there's none.</summary>
    public static string? DeviceFor(IReadOnlyList<string> pads)
    {
        var first = pads.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
        return first == null ? null : $"{first} 1";
    }

    public static string ProfileFile(Rpcs3Paths paths)
    {
        var root = Path.Combine(paths.ConfigDir, "input_configs");
        var active = Rpcs3Yaml.Load(Path.Combine(root, "active_input_configurations.yml")).Get("Active Configurations/global");
        var name = string.IsNullOrWhiteSpace(active) ? "Default" : active;
        return Path.Combine(root, "global", name + ".yml");
    }

    /// <summary>Player 1's handler and device in an input profile (nulls when it has none).</summary>
    public static (string? Handler, string? Device) Player1(string yamlText)
    {
        var y = Rpcs3Yaml.Parse(yamlText);
        return (y.Get("Player 1 Input/Handler"), y.Get("Player 1 Input/Device"));
    }

    /// <summary>Points player 1 at <paramref name="pads"/>' first pad unless the player has their own setup. True when it wrote.</summary>
    public static bool Ensure(Rpcs3Paths paths, IReadOnlyList<string> pads, string stampFile)
    {
        if (DeviceFor(pads) is not { } device)
        {
            return false;
        }
        var file = ProfileFile(paths);
        var text = File.Exists(file) ? File.ReadAllText(file) : "";
        var (handler, current) = Player1(text);
        if (handler == Handler && current == device)
        {
            return false;
        }
        var ours = File.Exists(stampFile) && File.ReadAllText(stampFile) == $"{handler}|{current}";
        var untouched = handler is null or "Keyboard" or "Null";
        if (!untouched && !ours)
        {
            return false;  // the player's own pad setup
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, Replace(text, device));
        File.Move(tmp, file, overwrite: true);
        Directory.CreateDirectory(Path.GetDirectoryName(stampFile)!);
        File.WriteAllText(stampFile, $"{Handler}|{device}");
        return true;
    }

    /// <summary>The profile with player 1's block swapped for an SDL one (its old key mapping dropped, so RPCS3's SDL defaults apply).</summary>
    internal static string Replace(string text, string device)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1] == "")
        {
            lines.RemoveAt(lines.Count - 1);
        }
        var block = new[] { Block, $"  Handler: {Handler}", $"  Device: {Quote(device)}" };
        var start = lines.FindIndex(l => l.TrimEnd() == Block);
        if (start < 0)
        {
            lines.InsertRange(0, block);
        }
        else
        {
            var end = start + 1;
            while (end < lines.Count && (lines[end].Length == 0 || lines[end][0] == ' '))
            {
                end++;
            }
            lines.RemoveRange(start, end - start);
            lines.InsertRange(start, block);
        }
        return string.Join('\n', lines) + "\n";
    }

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
