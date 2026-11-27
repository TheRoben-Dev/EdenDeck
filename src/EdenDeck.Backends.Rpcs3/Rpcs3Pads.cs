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

    /// <summary>RPCS3's device names for the players' pads, player 1 first.</summary>
    public static IReadOnlyList<string> DevicesFor(IReadOnlyList<Core.Input.Gamepad> players) =>
        players.Take(MaxPlayers).Select(p => $"{p.Name} {p.NameNumber}").ToList();

    public const int MaxPlayers = 7;

    /// <summary>Points player 1 at <paramref name="pads"/>' first pad unless the player has their own setup. True when it wrote.</summary>
    public static bool Ensure(Rpcs3Paths paths, IReadOnlyList<string> pads, string stampFile) =>
        DeviceFor(pads) is { } device && EnsurePlayers(paths, [device], stampFile);

    /// <summary>
    /// Points player N at <paramref name="devices"/>[N-1] (player 1 first) unless that player has
    /// their own setup. True when it wrote.
    /// </summary>
    public static bool EnsurePlayers(Rpcs3Paths paths, IReadOnlyList<string> devices, string stampFile)
    {
        if (devices.Count == 0)
        {
            return false;
        }
        var file = ProfileFile(paths);
        var text = File.Exists(file) ? File.ReadAllText(file) : "";
        var stamps = ReadStamps(stampFile);
        var changed = false;
        for (var i = 0; i < devices.Count && i < MaxPlayers; i++)
        {
            var y = Rpcs3Yaml.Parse(text);
            var block = $"Player {i + 1} Input";
            var (handler, current) = (y.Get(block + "/Handler"), y.Get(block + "/Device"));
            if (handler == Handler && current == devices[i])
            {
                stamps[i] = $"{Handler}|{devices[i]}";
                continue;
            }
            var ours = stamps.GetValueOrDefault(i) == $"{handler}|{current}";
            var untouched = handler is null or "Keyboard" or "Null";
            if (!untouched && !ours)
            {
                continue;  // the player's own pad setup
            }
            text = Replace(text, devices[i], block + ":");
            stamps[i] = $"{Handler}|{devices[i]}";
            changed = true;
        }
        if (changed)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, file, overwrite: true);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(stampFile)!);
        File.WriteAllText(stampFile, string.Join("\n", stamps.OrderBy(k => k.Key).Select(k => k.Key == 0 ? k.Value : $"{k.Key}|{k.Value}")));
        return changed;
    }

    /// <summary>Stamp file: player 1's "handler|device" on the first line (the old format), "N|handler|device" for the others.</summary>
    private static Dictionary<int, string> ReadStamps(string stampFile)
    {
        var d = new Dictionary<int, string>();
        if (!File.Exists(stampFile))
        {
            return d;
        }
        foreach (var line in File.ReadAllText(stampFile).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var bar = line.IndexOf('|');
            if (bar > 0 && int.TryParse(line[..bar], out var n) && line.IndexOf('|', bar + 1) > 0)
            {
                d[n] = line[(bar + 1)..];
            }
            else
            {
                d[0] = line;
            }
        }
        return d;
    }

    /// <summary>The profile with player 1's block swapped for an SDL one (its old key mapping dropped, so RPCS3's SDL defaults apply).</summary>
    internal static string Replace(string text, string device, string blockHeader = Block)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1] == "")
        {
            lines.RemoveAt(lines.Count - 1);
        }
        var block = new[] { blockHeader, $"  Handler: {Handler}", $"  Device: {Quote(device)}" };
        var start = lines.FindIndex(l => l.TrimEnd() == blockHeader);
        if (start < 0)
        {
            // After the last player block before this one, so the players stay in order.
            var after = lines.FindLastIndex(l => l.StartsWith("Player ", StringComparison.Ordinal) && string.CompareOrdinal(l.TrimEnd(), blockHeader) < 0);
            var at = 0;
            if (after >= 0)
            {
                at = after + 1;
                while (at < lines.Count && (lines[at].Length == 0 || lines[at][0] == ' '))
                {
                    at++;
                }
            }
            lines.InsertRange(at, block);
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
