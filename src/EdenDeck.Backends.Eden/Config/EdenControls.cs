using System.Globalization;

namespace EdenDeck.Backends.Eden.Config;

/// <summary>Switch controller styles Eden can emulate for a player (Settings::ControllerType values).</summary>
public static class ControllerStyle
{
    public const int ProController = 0;
    public const int DualJoycons = 1;
    public const int Handheld = 4;
}

/// <summary>
/// Player 1's controller style and input profiles, in Eden's own keys
/// (src/frontend_common/config.cpp ReadPlayerValues / SavePlayerValues):
/// [Controls] player_0_type (Handheld also forces console mode to Handheld when Eden loads
/// the config), player_0_vibration_enabled, and per game [Controls] player_0_profile_name in
/// custom/&lt;TITLEID&gt;.ini pointing at config/input/&lt;name&gt;.ini.
/// </summary>
public static class EdenControls
{
    private const string Section = "Controls";

    public static int PlayerStyle(string configFile) =>
        int.TryParse(QtIni.Load(configFile).Get(Section, "player_0_type"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v : ControllerStyle.ProController;

    public static void SetPlayerStyle(string configFile, int style)
    {
        var ini = QtIni.Load(configFile);
        Set(ini, "player_0_type", style.ToString(CultureInfo.InvariantCulture), style == ControllerStyle.ProController);
        ini.Save(configFile);
    }

    public static bool PlayerVibration(string configFile) => QtIni.Load(configFile).Get(Section, "player_0_vibration_enabled") is not ("false" or "0");

    public static void SetPlayerVibration(string configFile, bool on)
    {
        var ini = QtIni.Load(configFile);
        Set(ini, "player_0_vibration_enabled", on ? "true" : "false", on);
        ini.Save(configFile);
    }

    /// <summary>Input profiles made in Eden's Controls page (or saved by Eden Deck).</summary>
    public static IReadOnlyList<string> Profiles(string inputDir) =>
        Directory.Exists(inputDir)
            ? Directory.EnumerateFiles(inputDir, "*.ini").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList()
            : [];

    /// <summary>
    /// Saves player 1's current mapping and style as an input profile (overwrites a profile of
    /// that name). Profile files hold the player keys without the "player_0_" prefix.
    /// </summary>
    public static void SaveProfile(string configFile, string inputDir, string name)
    {
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/'))
        {
            throw new ArgumentException($"Not a valid profile name: {name}");
        }
        var global = QtIni.Load(configFile);
        var profile = new QtIni();
        foreach (var (key, value) in global.Entries(Section).Where(e => e.Key.StartsWith("player_0_", StringComparison.Ordinal)).ToList())
        {
            var bare = key["player_0_".Length..];
            if (bare.StartsWith("profile_name", StringComparison.Ordinal) || bare.StartsWith("connected", StringComparison.Ordinal))
            {
                continue;
            }
            profile.Set(Section, bare, value);
        }
        Directory.CreateDirectory(inputDir);
        profile.Save(Path.Combine(inputDir, name + ".ini"));
    }

    public static string? GameProfile(string perGameFile)
    {
        var v = QtIni.Load(perGameFile).Get(Section, "player_0_profile_name");
        return string.IsNullOrEmpty(v) ? null : v;
    }

    /// <summary>Per game: use this input profile for player 1, or null for the global setup.</summary>
    public static void SetGameProfile(string perGameFile, string? name)
    {
        var ini = QtIni.Load(perGameFile);
        Set(ini, "player_0_profile_name", name ?? "", name == null);
        Directory.CreateDirectory(Path.GetDirectoryName(perGameFile)!);
        ini.Save(perGameFile);
    }

    private static void Set(QtIni ini, string key, string value, bool isDefault)
    {
        ini.Set(Section, key + "\\default", isDefault ? "true" : "false");
        ini.Set(Section, key, value);
    }
}
