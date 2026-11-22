namespace EdenDeck.Core.Launch;

public sealed record LaunchOptions
{
    /// <summary>Directory containing VkLayer_SCANLINE_overlay.json, or null to not inject the layer.</summary>
    public string? LayerDirectory { get; init; }

    /// <summary>Where the layer writes its stats JSON.</summary>
    public string? LayerStatsPath { get; init; }

    /// <summary>Global kill switch for the layer.</summary>
    public bool LayerKillSwitch { get; init; }

    public bool Fullscreen { get; init; } = true;
}

/// <summary>
/// Everything needed to start an emulator for one game. Built by a backend, executed by
/// <see cref="ProcessLauncher"/>. Preparation steps run right before start; cleanup steps run
/// after the process exits (also when it crashes).
/// </summary>
public sealed class LaunchPlan
{
    public required string Executable { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }
    public required string WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<Action> Prepare { get; init; } = [];
    public IReadOnlyList<Action> Cleanup { get; init; } = [];

    /// <summary>Human-readable notes (what was applied), shown in logs and the UI.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}
