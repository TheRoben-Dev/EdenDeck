using EdenDeck.Backends.Eden.Config;
using EdenDeck.Backends.Eden.SystemFiles;
using EdenDeck.Core.Backends;
using EdenDeck.Core.Launch;
using EdenDeck.Core.Library;
using EdenDeck.Core.Profiles;

namespace EdenDeck.Backends.Eden;

/// <summary>
/// Builds the Eden launch: <c>eden -f -g &lt;path&gt;</c> (the only flags that mean the same on
/// Stable and Nightly, docs/eden-integration.md §2), per-game frame-gen settings under the 60 fps
/// rule, the EdenDeck Vulkan layer env, and a key-set farm when the game has a key set.
/// </summary>
public sealed class EdenLaunchPlanner : ILaunchPlanner
{
    private readonly EdenBackend _backend;
    private readonly string _runtimeDir;

    public EdenLaunchPlanner(EdenBackend backend, string runtimeDir)
    {
        _backend = backend;
        _runtimeDir = runtimeDir;
    }

    public LaunchPlan Plan(GameEntry game, GameProfile profile, LaunchOptions options)
    {
        var eden = _backend.Install.Current ?? throw new InvalidOperationException("No Eden install found. Install one in the Eden manager.");
        var notes = new List<string> { $"Eden {eden.Channel} {eden.Version}: {eden.ExecutablePath}" };
        var prepare = new List<Action>();
        var cleanup = new List<Action>();
        var env = new Dictionary<string, string>();

        // A neutral working directory: never one containing "user/" (that would switch Eden to
        // portable mode with an empty data dir), unless it's our key-set farm.
        var workDir = Path.Combine(_runtimeDir, "cwd");
        prepare.Add(() => Directory.CreateDirectory(workDir));

        if (profile.KeySet is { } keySetId)
        {
            var set = _backend.KeySets.Get(keySetId) ?? throw new InvalidOperationException($"Key set '{keySetId}' no longer exists.");
            var tag = string.IsNullOrEmpty(game.TitleId) ? "game" : game.TitleId;
            KeySetFarm? farm = null;
            var farmRoot = Path.Combine(_runtimeDir, "launch", $"{tag}-{Environment.ProcessId}");
            workDir = farmRoot;
            prepare.Add(() =>
            {
                farm = KeySetFarm.Build(_backend.Paths, set, _runtimeDir, tag);
                foreach (var w in farm.Warnings)
                {
                    notes.Add("warning: " + w);
                }
            });
            cleanup.Add(() => farm?.Remove());
            notes.Add($"key set: {set.Label}{(set.HasFirmware ? " (with firmware)" : "")}");
        }

        // Frame generation (lsfg-vk under the 60 fps rule) and Scanline's Vulkan layer (CRT, overlay).
        // Eden only compiles its own Lossless frame gen for Android (ENABLE_LSFG defaults OFF on
        // desktop, CMakeLists.txt), so on Linux its frame_gen keys do nothing.
        VulkanExtras.Apply(_backend, _runtimeDir, profile, options, env, prepare, notes);

        // Never let Eden's built-in frame gen stack on top of lsfg-vk (and clean up the per-game
        // override older EdenDeck versions wrote).
        if (!string.IsNullOrEmpty(game.TitleId) && _backend.Schema.Find("frame_gen") != null)
        {
            var current = QtIni.Load(_backend.Paths.PerGameConfigFile(game.TitleId));
            if (current.Get("Renderer", "frame_gen\\use_global") == "false")
            {
                prepare.Add(() => _backend.EdenSettings.WritePerGameRaw(game.TitleId, new Dictionary<string, string?>
                {
                    ["frame_gen"] = null, ["frame_gen_multiplier"] = null, ["frame_gen_target_rate"] = null,
                }));
            }
        }

        // Console mode follows the screen: docked on a TV, handheld on the built-in panel. Written to
        // Eden's global setting for this launch and put back afterwards (Eden saves its config on
        // exit, so the restore runs after that).
        if (options.Docked is { } docked && _backend.Schema.Find("use_docked_mode") != null)
        {
            var want = docked ? "Docked" : "Handheld";
            var perGame = string.IsNullOrEmpty(game.TitleId) ? null : _backend.EdenSettings.ReadPerGame(game.TitleId).GetValueOrDefault("use_docked_mode");
            if (perGame != null)
            {
                notes.Add($"console mode: {perGame} (set for this game)");
            }
            else
            {
                var before = _backend.EdenSettings.ReadGlobal().GetValueOrDefault("use_docked_mode");
                notes.Add($"console mode: {want} ({(docked ? "TV connected" : "built-in screen")})");
                if ((before ?? "Docked") != want)
                {
                    prepare.Add(() => _backend.EdenSettings.WriteGlobal(new Dictionary<string, string?> { ["use_docked_mode"] = want }));
                    cleanup.Add(() => _backend.EdenSettings.WriteGlobal(new Dictionary<string, string?> { ["use_docked_mode"] = before }));
                }
            }
        }

        var args = new List<string>();
        if (options.Fullscreen)
        {
            args.Add("-f");
        }
        args.Add("-g");
        args.Add(game.Path);

        return new LaunchPlan
        {
            Executable = eden.ExecutablePath,
            Arguments = args,
            WorkingDirectory = workDir,
            Environment = env,
            Prepare = prepare,
            Cleanup = cleanup,
            Notes = notes,
        };
    }

    /// <summary>The file the layer follows for the CRT setting while a game runs.</summary>
    public static string CrtControlFile(string runtimeDir) => Path.Combine(runtimeDir, "crt-control");

    public static string CrtControlText(int level, bool motion) => $"level={Math.Clamp(level, 0, 3)}\nmotion={(motion ? 1 : 0)}\n";

    public static string CrtLevelName(int level) => level switch { 1 => "Subtle", 2 => "Medium", 3 => "Strong", _ => "Off" };

    internal static string Append(string value, string? existing) => string.IsNullOrEmpty(existing) ? value : $"{value}:{existing}";
}
