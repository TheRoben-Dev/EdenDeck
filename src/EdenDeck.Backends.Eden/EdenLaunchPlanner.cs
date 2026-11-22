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

        // Frame generation (Eden Nightly built-in), under the 60 fps rule.
        if (!string.IsNullOrEmpty(game.TitleId) && profile.FrameGen == FrameGenMode.Builtin)
        {
            var decision = FrameGenPolicy.Decide(profile.FrameGen, profile.NativeFps);
            var schema = _backend.Schema;
            if (schema.Find("frame_gen") == null)
            {
                notes.Add("frame gen: not available in this Eden version (Nightly only)");
            }
            else
            {
                var values = decision.Enabled
                    ? new Dictionary<string, string?>
                    {
                        ["frame_gen"] = "true",
                        ["frame_gen_multiplier"] = decision.Multiplier.ToString(),
                        ["frame_gen_target_rate"] = decision.TargetFps.ToString(),
                    }
                    : new Dictionary<string, string?> { ["frame_gen"] = "false" };
                prepare.Add(() => _backend.EdenSettings.WritePerGameRaw(game.TitleId, values));
                if (decision.Enabled)
                {
                    // Eden Nightly only generates frames when it finds the user's own Lossless.dll.
                    // Copy it before the key-set farm links data/ entries (prepare runs in order).
                    var dll = _backend.EnsureLosslessDll();
                    notes.Add(dll.Installed
                        ? $"frame gen: {decision.Reason} (Lossless.dll ready)"
                        : $"warning: frame gen ({decision.Reason}) won't run. {dll.Message}");
                    if (!dll.Installed)
                    {
                        notes.Add("frame gen check: with the Steam performance overlay on, the FPS reads 60 when frame gen runs");
                    }
                }
                else
                {
                    notes.Add($"frame gen: {decision.Reason}");
                }
            }
        }
        else if (!string.IsNullOrEmpty(game.TitleId) && _backend.Schema.Find("frame_gen") != null &&
                 profile.FrameGen == FrameGenMode.Off)
        {
            // Make sure a previous built-in frame-gen override doesn't linger.
            var current = Config.QtIni.Load(_backend.Paths.PerGameConfigFile(game.TitleId));
            if (current.Get("Renderer", "frame_gen\\use_global") == "false")
            {
                prepare.Add(() => _backend.EdenSettings.WritePerGameRaw(game.TitleId, new Dictionary<string, string?> { ["frame_gen"] = null }));
            }
        }

        if (options.LayerDirectory is { } layerDir && File.Exists(Path.Combine(layerDir, "VkLayer_EDENDECK_fsr.json")))
        {
            env["VK_ADD_LAYER_PATH"] = Append(layerDir, Environment.GetEnvironmentVariable("VK_ADD_LAYER_PATH"));
            env["VK_INSTANCE_LAYERS"] = Append("VK_LAYER_EDENDECK_fsr", Environment.GetEnvironmentVariable("VK_INSTANCE_LAYERS"));
            if (options.LayerStatsPath != null)
            {
                env["EDENDECK_LAYER_STATS"] = options.LayerStatsPath;
            }
            if (options.LayerKillSwitch)
            {
                env["DISABLE_EDENDECK_LAYER"] = "1";
            }
            notes.Add(options.LayerKillSwitch ? "layer: loaded but disabled (kill switch)" : "layer: VK_LAYER_EDENDECK_fsr");
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

    private static string Append(string value, string? existing) => string.IsNullOrEmpty(existing) ? value : $"{value}:{existing}";
}
