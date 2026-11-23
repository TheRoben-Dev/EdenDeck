using EdenDeck.Backends.Eden.SystemFiles;
using EdenDeck.Core.Launch;
using EdenDeck.Core.Profiles;

namespace EdenDeck.Backends.Eden;

/// <summary>
/// What every Vulkan emulator gets at launch: lsfg-vk frame generation under the 60 fps rule
/// (2x only, never above 60) and Scanline's own Vulkan layer (CRT filter, quick menu, stats).
/// Used by Eden's planner and, through <see cref="ILaunchExtras"/>, by other Vulkan backends.
/// </summary>
public static class VulkanExtras
{
    public static void Apply(EdenBackend backend, string runtimeDir, GameProfile profile, LaunchOptions options,
        Dictionary<string, string> env, List<Action> prepare, List<string> notes)
    {
        // Frame generation, under the 60 fps rule. Eden only compiles its own Lossless frame gen
        // for Android (ENABLE_LSFG defaults OFF on desktop, CMakeLists.txt), so on Linux the
        // frame_gen keys do nothing and lsfg-vk (Vulkan layer, same Lossless shaders) does the work.
        var decision = FrameGenPolicy.Decide(profile.FrameGen, profile.NativeFps);
        var lsfg = backend.FindLsfgVk();
        // Explicit layers for this launch, closest to the game first. The bundled lsfg-vk goes above
        // Scanline's layer so its generated frames pass through ours (CRT filter, stats).
        var layers = new List<string>();
        var layerPaths = new List<string>();
        if (decision.Enabled)
        {
            var dll = backend.EnsureLosslessDll();
            var dllPath = backend.LosslessDllPath();
            if (lsfg.Kind == LsfgVk.Kind.V2 && dllPath != null)
            {
                foreach (var (k, v) in LsfgVk.EnableEnv(dllPath, decision.Multiplier))
                {
                    env[k] = v;
                }
                if (lsfg.Bundled)
                {
                    layers.Add(LsfgVk.LayerName);
                    layerPaths.Add(Path.GetDirectoryName(lsfg.Manifest!)!);
                }
                notes.Add($"frame gen: lsfg-vk{(lsfg.Bundled ? " (bundled)" : "")}, {decision.Reason}");
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


        if (options.LayerDirectory is { } layerDir && File.Exists(Path.Combine(layerDir, "VkLayer_SCANLINE_overlay.json")))
        {
            layers.Add("VK_LAYER_SCANLINE_overlay");
            layerPaths.Add(layerDir);
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
                var control = EdenLaunchPlanner.CrtControlFile(runtimeDir);
                var text = EdenLaunchPlanner.CrtControlText(level, options.CrtMotion);
                prepare.Add(() =>
                {
                    Directory.CreateDirectory(runtimeDir);
                    File.WriteAllText(control, text);
                });
                env["SCANLINE_CRT"] = level.ToString(System.Globalization.CultureInfo.InvariantCulture);
                env["SCANLINE_CRT_MOTION"] = options.CrtMotion ? "1" : "0";
                env["SCANLINE_CRT_CONTROL"] = control;
                if (level > 0)
                {
                    notes.Add($"CRT effect: {EdenLaunchPlanner.CrtLevelName(level)}");
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

        if (layers.Count > 0)
        {
            env["VK_ADD_LAYER_PATH"] = EdenLaunchPlanner.Append(string.Join(':', layerPaths.Distinct()), Environment.GetEnvironmentVariable("VK_ADD_LAYER_PATH"));
            env["VK_INSTANCE_LAYERS"] = EdenLaunchPlanner.Append(string.Join(':', layers), Environment.GetEnvironmentVariable("VK_INSTANCE_LAYERS"));
        }

    }
}

/// <summary>The shared Vulkan extras, for backends that don't know about Eden.</summary>
public sealed class EdenVulkanExtras(EdenBackend backend, string runtimeDir) : ILaunchExtras
{
    public void Apply(GameProfile profile, LaunchOptions options, Dictionary<string, string> env, List<Action> prepare, List<string> notes) =>
        VulkanExtras.Apply(backend, runtimeDir, profile, options, env, prepare, notes);
}
