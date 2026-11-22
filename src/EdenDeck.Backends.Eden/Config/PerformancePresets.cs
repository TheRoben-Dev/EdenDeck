using EdenDeck.Core.Backends;
using EdenDeck.Core.Settings;

namespace EdenDeck.Backends.Eden.Config;

/// <summary>A one-press set of Eden's per-game graphics settings.</summary>
public sealed record PerformancePreset(string Id, string Label, string Detail, IReadOnlyDictionary<string, string> Values);

/// <summary>
/// Ready-made per-game performance presets. Each one writes the same handful of per-game
/// overrides (console mode, resolution, upscaling, anti-aliasing, GPU accuracy, async shaders), so
/// the preset a game is on can be read back from its overrides. Values a given Eden version
/// doesn't have are skipped.
/// </summary>
public static class PerformancePresets
{
    /// <summary>No overrides for any preset setting: the game follows Eden's global settings.</summary>
    public const string Global = "global";

    /// <summary>Overrides that don't match a preset (changed by hand).</summary>
    public const string Custom = "custom";

    public static readonly IReadOnlyList<PerformancePreset> All =
    [
        new("battery", "Battery saver", "Handheld mode at 0.75x with FSR and fast GPU accuracy. Longest battery life; a little soft.",
            V("Handheld", "Res3_4X", "Fsr", "None", "Low", "true")),
        new("balanced", "Balanced", "Handheld mode at native resolution with FSR. The Steam Deck sweet spot.",
            V("Handheld", "Res1X", "Fsr", "None", "High", "true")),
        new("tv", "TV quality", "Docked mode with FSR and FXAA. Sharper on a TV; heavier on the Deck.",
            V("Docked", "Res1X", "Fsr", "Fxaa", "High", "true")),
        new("sharp", "Sharp (desktop GPU)", "Docked mode at 2x with SMAA. For a strong desktop GPU, too heavy for the Deck.",
            V("Docked", "Res2X", "Area", "Smaa", "High", "true")),
    ];

    /// <summary>The settings every preset writes.</summary>
    public static readonly string[] Keys = ["use_docked_mode", "resolution_setup", "scaling_filter", "anti_aliasing", "gpu_accuracy", "use_asynchronous_shaders"];

    private static Dictionary<string, string> V(string mode, string res, string filter, string aa, string gpu, string async) => new()
    {
        ["use_docked_mode"] = mode,
        ["resolution_setup"] = res,
        ["scaling_filter"] = filter,
        ["anti_aliasing"] = aa,
        ["gpu_accuracy"] = gpu,
        ["use_asynchronous_shaders"] = async,
    };

    /// <summary>The preset values this Eden version can take per game.</summary>
    public static Dictionary<string, string> Applicable(PerformancePreset preset, IReadOnlyList<OptionDefinition> options)
    {
        var result = new Dictionary<string, string>();
        foreach (var (id, value) in preset.Values)
        {
            if (options.FirstOrDefault(o => o.Id == id) is { PerGame: true } o
                && (o.Type != OptionType.Choice || o.Choices.Any(c => c.Value == value)))
            {
                result[id] = value;
            }
        }
        return result;
    }

    /// <summary>Which preset a game's overrides match: a preset id, <see cref="Global"/> or <see cref="Custom"/>.</summary>
    public static string Current(IReadOnlyDictionary<string, string> overrides, IReadOnlyList<OptionDefinition> options)
    {
        if (!Keys.Any(overrides.ContainsKey))
        {
            return Global;
        }
        foreach (var p in All)
        {
            var values = Applicable(p, options);
            if (values.Count > 0 && Keys.All(k => values.TryGetValue(k, out var v) ? overrides.GetValueOrDefault(k) == v : !overrides.ContainsKey(k)))
            {
                return p.Id;
            }
        }
        return Custom;
    }

    /// <summary>Puts a game on a preset, or back on Eden's settings with <see cref="Global"/>.</summary>
    public static void Apply(ISettingsProvider settings, string titleId, string presetId)
    {
        var options = settings.Options;
        var writable = Keys.Where(k => options.Any(o => o.Id == k && o.PerGame)).ToList();
        var values = writable.ToDictionary(k => k, _ => (string?)null);
        if (presetId != Global)
        {
            var preset = All.FirstOrDefault(p => p.Id == presetId) ?? throw new ArgumentException($"No preset '{presetId}'");
            foreach (var (k, v) in Applicable(preset, options))
            {
                values[k] = v;
            }
        }
        settings.WritePerGame(titleId, values);
    }
}
