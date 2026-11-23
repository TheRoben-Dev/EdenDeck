using EdenDeck.Core.Backends;

namespace EdenDeck.Core.Settings;

/// <summary>A one-press set of per-game settings.</summary>
public sealed record PerformancePreset(string Id, string Label, string Detail, IReadOnlyDictionary<string, string> Values);

/// <summary>
/// Ready-made per-game presets for one emulator. Every preset writes the same keys, so the preset
/// a game is on can be read back from its overrides. Values an emulator version doesn't have are
/// skipped.
/// </summary>
public sealed class PresetSet(string caption, string intro, IReadOnlyList<PerformancePreset> all, IReadOnlyList<string> keys)
{
    /// <summary>No overrides for any preset setting: the game follows the global settings.</summary>
    public const string Global = "global";

    /// <summary>Overrides that don't match a preset (changed by hand).</summary>
    public const string Custom = "custom";

    /// <summary>The row's name on the game card ("Performance preset", "Look").</summary>
    public string Caption => caption;

    /// <summary>The hint shown while the game follows the global settings.</summary>
    public string Intro => intro;

    public IReadOnlyList<PerformancePreset> All => all;
    public IReadOnlyList<string> Keys => keys;

    public Dictionary<string, string> Applicable(PerformancePreset preset, IReadOnlyList<OptionDefinition> options)
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

    public string Current(IReadOnlyDictionary<string, string> overrides, IReadOnlyList<OptionDefinition> options)
    {
        if (!keys.Any(overrides.ContainsKey))
        {
            return Global;
        }
        foreach (var p in all)
        {
            var values = Applicable(p, options);
            if (values.Count > 0 && keys.All(k => values.TryGetValue(k, out var v) ? overrides.GetValueOrDefault(k) == v : !overrides.ContainsKey(k)))
            {
                return p.Id;
            }
        }
        return Custom;
    }

    public void Apply(ISettingsProvider settings, string titleId, string presetId)
    {
        var options = settings.Options;
        var values = keys.Where(k => options.Any(o => o.Id == k && o.PerGame)).ToDictionary(k => k, _ => (string?)null);
        if (presetId != Global)
        {
            var preset = all.FirstOrDefault(p => p.Id == presetId) ?? throw new ArgumentException($"No preset '{presetId}'");
            foreach (var (k, v) in Applicable(preset, options))
            {
                values[k] = v;
            }
        }
        settings.WritePerGame(titleId, values);
    }
}
