using EdenDeck.Core.Backends;
using EdenDeck.Core.Settings;

namespace EdenDeck.Backends.Eden.Config;

/// <summary>
/// Ready-made per-game performance presets. Each one writes the same handful of per-game
/// overrides (resolution, upscaling, anti-aliasing, GPU accuracy, async shaders), so
/// the preset a game is on can be read back from its overrides. Values a given Eden version
/// doesn't have are skipped. Console mode is left out on purpose: it follows the screen (TV or
/// built-in) unless set per game by hand.
/// </summary>
public static class PerformancePresets
{
    /// <summary>No overrides for any preset setting: the game follows Eden's global settings.</summary>
    public const string Global = PresetSet.Global;

    /// <summary>Overrides that don't match a preset (changed by hand).</summary>
    public const string Custom = PresetSet.Custom;

    public static readonly IReadOnlyList<PerformancePreset> All =
    [
        new("battery", "Battery saver", "0.75x with FSR and fast GPU accuracy. Longest battery life; a little soft.",
            V("Res3_4X", "Fsr", "None", "Low", "true")),
        new("balanced", "Balanced", "Native resolution with FSR sharpening and accurate graphics. The Steam Deck sweet spot.",
            V("Res1X", "Fsr", "None", "High", "true")),
        new("tv", "TV quality", "Native resolution with FSR and FXAA for smooth edges on a big screen.",
            V("Res1X", "Fsr", "Fxaa", "High", "true")),
        new("sharp", "Sharp (desktop GPU)", "2x resolution with SMAA. For a strong desktop GPU, too heavy for the Deck.",
            V("Res2X", "Area", "Smaa", "High", "true")),
    ];

    /// <summary>The settings every preset writes.</summary>
    public static readonly string[] Keys = ["resolution_setup", "scaling_filter", "anti_aliasing", "gpu_accuracy", "use_asynchronous_shaders"];

    private static Dictionary<string, string> V(string res, string filter, string aa, string gpu, string async) => new()
    {
        ["resolution_setup"] = res,
        ["scaling_filter"] = filter,
        ["anti_aliasing"] = aa,
        ["gpu_accuracy"] = gpu,
        ["use_asynchronous_shaders"] = async,
    };

    public static readonly PresetSet Set = new("Performance preset",
        "Presets set resolution, upscaling, anti-aliasing and GPU accuracy for this game in one go.", All, Keys);

    public static Dictionary<string, string> Applicable(PerformancePreset preset, IReadOnlyList<OptionDefinition> options) => Set.Applicable(preset, options);

    public static string Current(IReadOnlyDictionary<string, string> overrides, IReadOnlyList<OptionDefinition> options) => Set.Current(overrides, options);

    public static void Apply(ISettingsProvider settings, string titleId, string presetId) => Set.Apply(settings, titleId, presetId);
}
