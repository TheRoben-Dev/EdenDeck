using EdenDeck.App.Ui;
using EdenDeck.Core.Settings;

namespace EdenDeck.App.Screens;

/// <summary>Builds a Stepper for a curated emulator option (global or per-game).</summary>
public static class OptionRows
{
    /// <summary>Sentinel value for "use the global setting" in per-game rows.</summary>
    public const string UseGlobal = "";

    public static IEnumerable<(string Value, string Label)> Values(OptionDefinition o) => o.Type switch
    {
        OptionType.Bool => [("true", "On"), ("false", "Off")],
        OptionType.Choice => o.Choices.Select(c => (c.Value, c.Label)),
        OptionType.Int => Enumerable.Range(0, (o.Max - o.Min) / Math.Max(1, o.Step) + 1)
            .Select(i => (o.Min + i * o.Step).ToString()).Select(v => (v, v)),
        _ => [],
    };

    public static string LabelFor(OptionDefinition o, string? value)
    {
        value ??= o.DefaultValue;
        if (value == null)
        {
            return "default";
        }
        return Values(o).FirstOrDefault(v => v.Value == value).Label ?? value;
    }

    /// <summary>Global row: current value, falling back to the emulator default.</summary>
    public static Stepper Global(OptionDefinition o, string? current, Action<string> changed)
    {
        var s = new Stepper(o.Label, Values(o), current ?? o.DefaultValue, o.Help) { Wrap = o.Type != OptionType.Int };
        s.Changed += changed;
        return s;
    }

    /// <summary>Per-game row: first entry is "Global (value)", which removes the override.</summary>
    public static Stepper PerGame(OptionDefinition o, string? overrideValue, string? globalValue, Action<string?> changed)
    {
        var values = new List<(string, string)> { (UseGlobal, $"Global ({LabelFor(o, globalValue)})") };
        values.AddRange(Values(o));
        var s = new Stepper(o.Label, values, overrideValue ?? UseGlobal, o.Help) { Wrap = o.Type != OptionType.Int };
        s.Changed += v => changed(v == UseGlobal ? null : v);
        return s;
    }
}
