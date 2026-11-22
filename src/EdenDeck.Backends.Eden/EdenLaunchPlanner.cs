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

        // Frame generation, under the 60 fps rule. Eden only compiles its own Lossless frame gen
        // for Android (ENABLE_LSFG defaults OFF on desktop, CMakeLists.txt), so on Linux the
        // frame_gen keys do nothing and lsfg-vk (Vulkan layer, same Lossless shaders) does the work.
        var decision = FrameGenPolicy.Decide(profile.FrameGen, profile.NativeFps);
        var lsfg = _backend.FindLsfgVk();
        if (decision.Enabled)
        {
            var dll = _backend.EnsureLosslessDll();
            var dllPath = _backend.LosslessDllPath();
            if (lsfg.Kind == LsfgVk.Kind.V2 && dllPath != null)
            {
                foreach (var (k, v) in LsfgVk.EnableEnv(dllPath, decision.Multiplier))
                {
                    env[k] = v;
                }
                notes.Add($"frame gen: lsfg-vk, {decision.Reason}");
            }
            else if (dllPath == null)
            {
                notes.Add($"warning: frame gen ({decision.Reason}) won't run. {dll.Message}");
            }
            else if (lsfg.Kind == LsfgVk.Kind.Legacy)
            {
                notes.Add($"warning: frame gen ({decision.Reason}) won't run: your lsfg-vk is a 1.x version. Update it to 2.x.");
            }
            else
            {
                notes.Add($"warning: frame gen ({decision.Reason}) won't run: lsfg-vk isn't installed.");
            }
        }
        else
        {
            if (lsfg.Kind != LsfgVk.Kind.None)
            {
                foreach (var (k, v) in LsfgVk.DisableEnv())
                {
                    env[k] = v;
                }
            }
            if (profile.FrameGen == FrameGenMode.Builtin)
            {
                notes.Add($"frame gen: {decision.Reason}");
            }
        }

        // Never let Eden's built-in frame gen stack on top of lsfg-vk (and clean up the per-game
        // override older EdenDeck versions wrote).
        if (!string.IsNullOrEmpty(game.TitleId) && _backend.Schema.Find("frame_gen") != null)
        {
            var current = Config.QtIni.Load(_backend.Paths.PerGameConfigFile(game.TitleId));
            if (current.Get("Renderer", "frame_gen\\use_global") == "false")
            {
                prepare.Add(() => _backend.EdenSettings.WritePerGameRaw(game.TitleId, new Dictionary<string, string?>
                {
                    ["frame_gen"] = null, ["frame_gen_multiplier"] = null, ["frame_gen_target_rate"] = null,
                }));
            }
        }

        if (options.LayerDirectory is { } layerDir && File.Exists(Path.Combine(layerDir, "VkLayer_SCANLINE_overlay.json")))
        {
            env["VK_ADD_LAYER_PATH"] = Append(layerDir, Environment.GetEnvironmentVariable("VK_ADD_LAYER_PATH"));
            env["VK_INSTANCE_LAYERS"] = Append("VK_LAYER_SCANLINE_overlay", Environment.GetEnvironmentVariable("VK_INSTANCE_LAYERS"));
            if (options.LayerStatsPath != null)
            {
                env["SCANLINE_LAYER_STATS"] = options.LayerStatsPath;
            }
            if (options.LayerKillSwitch)
            {
                env["DISABLE_SCANLINE_LAYER"] = "1";
            }
            notes.Add(options.LayerKillSwitch ? "layer: loaded but disabled (kill switch)" : "layer: VK_LAYER_SCANLINE_overlay");
            if (!options.LayerKillSwitch)
            {
                // The CRT filter always gets a control file so it can be switched while the game
                // runs (quick menu); the layer reads it twice a second and costs nothing while off.
                var level = Math.Clamp(profile.Crt, 0, 3);
                var control = CrtControlFile(_runtimeDir);
                var text = CrtControlText(level, options.CrtMotion);
                prepare.Add(() =>
                {
                    Directory.CreateDirectory(_runtimeDir);
                    File.WriteAllText(control, text);
                });
                env["SCANLINE_CRT"] = level.ToString(System.Globalization.CultureInfo.InvariantCulture);
                env["SCANLINE_CRT_MOTION"] = options.CrtMotion ? "1" : "0";
                env["SCANLINE_CRT_CONTROL"] = control;
                if (level > 0)
                {
                    notes.Add($"CRT effect: {CrtLevelName(level)}");
                }
            }
            else if (profile.Crt > 0)
            {
                notes.Add("warning: CRT effect is off because the Scanline layer is switched off (kill switch).");
            }
        }
        else if (profile.Crt > 0)
        {
            notes.Add("warning: CRT effect needs Scanline's Vulkan layer, which wasn't found. The game runs without it.");
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

    private static string Append(string value, string? existing) => string.IsNullOrEmpty(existing) ? value : $"{value}:{existing}";
}
