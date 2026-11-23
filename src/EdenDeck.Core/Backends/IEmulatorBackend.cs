using EdenDeck.Core.Launch;
using EdenDeck.Core.Library;
using EdenDeck.Core.Profiles;
using EdenDeck.Core.Settings;

namespace EdenDeck.Core.Backends;

/// <summary>
/// One emulator integration (Eden first; RPCS3, PCSX2, ... later). See docs/backends.md.
/// The UI talks only to this interface. Optional capabilities return null when unsupported,
/// and the UI hides them for that backend.
/// </summary>
public interface IEmulatorBackend
{
    /// <summary>Stable short id used in game ids ("eden:0100ABCD...").</summary>
    string Id { get; }
    string DisplayName { get; }
    string SystemName { get; }
    IReadOnlyCollection<string> FileExtensions { get; }

    IInstallManager Install { get; }
    ILibraryScanner Library { get; }
    ISettingsProvider Settings { get; }
    ILaunchPlanner Launch { get; }

    ISystemFiles? SystemFiles { get; }

    /// <summary>Save backups (null when the emulator's saves can't be found).</summary>
    Saves.ISaveData? SaveData => null;

    /// <summary>The game's own square-ish icon as an image file (read offline from the game), or null.</summary>
    string? IconFor(Library.GameEntry game) => null;

    /// <summary>One-press per-game presets over some of the settings (null when there are none).</summary>
    PresetSet? Presets => null;
}

public sealed record InstalledEmulator(
    string Channel,
    string Version,
    string ExecutablePath,
    bool ManagedByEdenDeck);

public sealed record AvailableRelease(
    string Channel,
    string Version,
    string DownloadUrl,
    long Size,
    string? Sha256,
    DateTimeOffset? PublishedAt);

public interface IInstallManager
{
    IReadOnlyList<string> Channels { get; }

    /// <summary>The emulator EdenDeck will launch, or null if none was found.</summary>
    InstalledEmulator? Current { get; }

    /// <summary>A previous version kept for rollback, if any.</summary>
    InstalledEmulator? Previous { get; }

    IReadOnlyList<InstalledEmulator> DetectExternal();

    Task<AvailableRelease?> CheckForUpdateAsync(string channel, CancellationToken ct);

    Task<InstalledEmulator> InstallAsync(AvailableRelease release, IProgress<double>? progress, CancellationToken ct);

    InstalledEmulator Rollback();

    void UseExternal(InstalledEmulator emulator);
}

public interface ILibraryScanner
{
    /// <summary>Returns null for files this backend does not handle or cannot read.</summary>
    GameEntry? TryRead(string path);

    /// <summary>
    /// For a file whose name has no (or the wrong) extension: the extension its contents show it
    /// should have (e.g. ".xci"), from magic bytes. Null when it isn't this backend's format.
    /// </summary>
    string? DetectExtension(string path) => null;

    /// <summary>Folders the emulator keeps games in itself (scanned along with the player's own).</summary>
    IEnumerable<string> OwnFolders => [];
}

public interface ISettingsProvider
{
    /// <summary>The curated options shown in the UI (only the ones that matter).</summary>
    IReadOnlyList<OptionDefinition> Options { get; }

    /// <summary>Current global values, by option id. Missing = emulator default.</summary>
    IReadOnlyDictionary<string, string> ReadGlobal();

    void WriteGlobal(IReadOnlyDictionary<string, string?> values);

    /// <summary>Per-game overrides (option id -> value). Absent = use global.</summary>
    IReadOnlyDictionary<string, string> ReadPerGame(string titleId);

    /// <summary>Writes overrides; a null value removes the override (back to global).</summary>
    void WritePerGame(string titleId, IReadOnlyDictionary<string, string?> values);
}

public interface ILaunchPlanner
{
    LaunchPlan Plan(GameEntry game, GameProfile profile, LaunchOptions options);
}

public sealed record SystemFileStatus(string Kind, bool Present, string Detail);

/// <summary>Guided import of the user's own BIOS/firmware/keys. Never downloads anything.</summary>
public interface ISystemFiles
{
    IReadOnlyList<SystemFileStatus> Status();
}
