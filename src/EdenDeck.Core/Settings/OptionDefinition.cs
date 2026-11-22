namespace EdenDeck.Core.Settings;

public enum OptionType
{
    Bool,
    Choice,
    Int,
}

public sealed record OptionChoice(string Value, string Label);

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

    /// <summary>Displayed when the emulator default is in effect.</summary>
    public string? DefaultValue { get; init; }
}
