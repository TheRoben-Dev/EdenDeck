namespace EdenDeck.Core.Launch;

/// <summary>
/// Is a TV or monitor plugged in? Read from the kernel's DRM connectors
/// (<c>/sys/class/drm/card0-HDMI-A-1/status</c> and so on), which works the same in Game Mode,
/// on the desktop and before any window exists.
/// </summary>
public static class ScreenDetect
{
    private static readonly string[] Internal = ["eDP", "LVDS", "DSI"];

    /// <summary>
    /// True when an external screen is connected (or the machine has no built-in panel at all, like a
    /// desktop PC), false when only the built-in panel is, null when it can't be told.
    /// </summary>
    public static bool? External(string drmRoot = "/sys/class/drm")
    {
        if (!Directory.Exists(drmRoot))
        {
            return null;
        }
        var connectors = new List<(string Name, bool Connected)>();
        foreach (var dir in Directory.EnumerateDirectories(drmRoot, "card*-*"))
        {
            // card0-eDP-1 -> eDP-1
            var name = Path.GetFileName(dir);
            name = name[(name.IndexOf('-') + 1)..];
            var status = Path.Combine(dir, "status");
            string text;
            try
            {
                text = File.Exists(status) ? File.ReadAllText(status).Trim() : "";
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            connectors.Add((name, text == "connected"));
        }
        if (connectors.Count == 0)
        {
            return null;
        }
        bool IsInternal(string n) => Internal.Any(i => n.StartsWith(i, StringComparison.OrdinalIgnoreCase));
        if (connectors.Any(c => c.Connected && !IsInternal(c.Name)))
        {
            return true;
        }
        if (connectors.Any(c => IsInternal(c.Name)))
        {
            return false;
        }
        return null;  // no panel and nothing plugged in: can't tell
    }
}
