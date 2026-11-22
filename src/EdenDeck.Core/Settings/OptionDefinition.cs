namespace EdenDeck.Core.Settings;

public enum OptionType
{
    Bool,
    Choice,
    Int,
}

/// <summary>One value of a choice option. Detail explains the tradeoff of picking it.</summary>
public sealed record OptionChoice(string Value, string Label, string? Detail = null);

/// <summary>A curated, user-facing setting with a one-line explanation.</summary>
public sealed record OptionDefinition
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Help { get; init; }
    public required string Group { get; init; }
    public required OptionType Type { get; init; }
    public IReadOnlyList<OptionChoice> Choices { get; init; } = [];
    public int Min { get; init; }
    public int Max { get; init; }
    public int Step { get; init; } = 1;
    public bool PerGame { get; init; } = true;

    /// <summary>Bool options: what "On" / "Off" means in practice (shown in the expanded help).</summary>
    public string? OnDetail { get; init; }
    public string? OffDetail { get; init; }

    /// <summary>The tradeoff of a specific value, or null.</summary>
    public string? DetailFor(string? value) => Type switch
    {
        OptionType.Bool => value == "true" ? OnDetail : value == "false" ? OffDetail : null,
        OptionType.Choice => Choices.FirstOrDefault(c => c.Value == value)?.Detail,
        _ => null,
    };

    /// <summary>Displayed when the emulator default is in effect.</summary>
    public string? DefaultValue { get; init; }
}
