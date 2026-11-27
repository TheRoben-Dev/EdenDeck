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

    /// <summary>False when the app's Animations setting is off: the in-game CRT filter holds still too.</summary>
    public bool CrtMotion { get; init; } = true;

    /// <summary>
    /// Console mode for this launch only: true docked (a TV is connected), false handheld, null to
    /// leave Eden's own setting. A per-game console mode set by hand still wins.
    /// </summary>
    public bool? Docked { get; init; }

    /// <summary>Handheld games: draw Scanline's decorative frame in the side bars (a game can override it).</summary>
    public bool HandheldFrame { get; init; } = true;

    /// <summary>Names of the players' controllers as SDL reports them, player 1 first (empty: none / unknown).</summary>
    public IReadOnlyList<string> Gamepads { get; init; } = [];

    /// <summary>
    /// The players' controllers, player 1 first (<see cref="Input.GamepadScan.Players"/>): pads that
    /// are plugged in take player 1, 2, ...; the handheld's own controls only when nothing else is.
    /// </summary>
    public IReadOnlyList<Input.Gamepad> Players { get; init; } = [];

    /// <summary>The app's accent colour as #rrggbb, for things drawn in-game (null: a default).</summary>
    public string? Accent { get; init; }

    /// <summary>Frame generation and Scanline's Vulkan layer, for any Vulkan emulator (null: none).</summary>
    public ILaunchExtras? Vulkan { get; init; }
}

/// <summary>Adds environment, preparation and notes to a launch (frame gen, overlay layer).</summary>
public interface ILaunchExtras
{
    void Apply(Profiles.GameProfile profile, LaunchOptions options, Dictionary<string, string> env, List<Action> prepare, List<string> notes);
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
