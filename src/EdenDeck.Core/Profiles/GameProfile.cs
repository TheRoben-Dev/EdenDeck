using System.Text.Json;
using System.Text.Json.Serialization;

namespace EdenDeck.Core.Profiles;

public enum FrameGenMode
{
    Off,
    /// <summary>Lossless Scaling frame gen (lsfg-vk on Linux). Name kept for saved profiles.</summary>
    Builtin,
    LsfgVk,
    /// <summary>EdenDeck layer FSR 3.1 frame gen (experimental, phase 5).</summary>
    LayerFsr3,
}

/// <summary>
/// EdenDeck's own per-game state. Stored in EdenDeck's config, never in the emulator's files
/// (Eden rewrites its INIs and drops unknown keys).
/// </summary>
public sealed record GameProfile
{
    /// <summary>Key set id to use for this game, or null for the default keys.</summary>
    public string? KeySet { get; init; }

    public FrameGenMode FrameGen { get; init; } = FrameGenMode.Off;

    /// <summary>Native frame rate of the title, if known (used for the 60 fps frame-gen rule).</summary>
    public int? NativeFps { get; init; }

    public string? ControllerProfile { get; init; }

    /// <summary>Let Eden load this game's update and DLC files from the library (no NAND install).</summary>
    public bool LoadAddOns { get; init; } = true;

    /// <summary>In-game CRT filter drawn by Scanline's Vulkan layer: 0 off, 1 subtle, 2 medium, 3 strong.</summary>
    public int Crt { get; init; }

    public DateTimeOffset? LastPlayed { get; init; }
}

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _file;
    private Dictionary<string, GameProfile> _profiles;

    public ProfileStore(string file)
    {
        _file = file;
        _profiles = Load(file);
    }

    public GameProfile Get(string gameId) => _profiles.TryGetValue(gameId, out var p) ? p : new GameProfile();

    public void Set(string gameId, GameProfile profile)
    {
        _profiles[gameId] = profile;
        Save();
    }

    public IReadOnlyDictionary<string, GameProfile> All => _profiles;

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var tmp = _file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_profiles, Json));
        File.Move(tmp, _file, overwrite: true);
    }

    private static Dictionary<string, GameProfile> Load(string file)
    {
        if (!File.Exists(file))
        {
            return new();
        }
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, GameProfile>>(File.ReadAllText(file), Json) ?? new();
        }
        catch (JsonException)
        {
            // Keep the broken file for inspection, start fresh.
            File.Copy(file, file + ".broken", overwrite: true);
            return new();
        }
    }
}
