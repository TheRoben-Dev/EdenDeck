namespace EdenDeck.Backends.Eden.SystemFiles;

/// <summary>
/// lsfg-vk: Lossless Scaling frame generation as a Vulkan layer. Eden's own Lossless frame gen is
/// only compiled for Android (ENABLE_LSFG defaults OFF on desktop), so on Linux this is the path.
///
/// lsfg-vk 2.x is an implicit layer (VK_LAYER_LSFGVK_frame_generation) that does nothing unless a
/// profile matches. With <c>LSFGVK_ENV=1</c> it ignores its config file and takes the profile from
/// LSFGVK_* variables (lsfg-vk-common/src/configuration/config.cpp), so EdenDeck enables it for one
/// Eden launch without touching the user's ~/.config/lsfg-vk/conf.toml. <c>DISABLE_LSFGVK=1</c>
/// (the layer's disable_environment) keeps it out of launches without frame gen.
/// </summary>
public static class LsfgVk
{
    public const string LayerName = "VK_LAYER_LSFGVK_frame_generation";
    private const string LegacyLayerName = "VK_LAYER_LS_frame_generation";

    public enum Kind { None, V2, Legacy }

    public sealed record Install(Kind Kind, string? Manifest);

    /// <summary>Vulkan loader implicit layer search dirs (user first).</summary>
    public static IEnumerable<string> ImplicitLayerDirs(Func<string, string?> env)
    {
        var home = env("HOME") ?? "";
        var dataHome = env("XDG_DATA_HOME") is { Length: > 0 } x ? x : Path.Combine(home, ".local", "share");
        yield return Path.Combine(dataHome, "vulkan", "implicit_layer.d");
        yield return Path.Combine(home, ".local", "share", "vulkan", "implicit_layer.d");
        var dataDirs = env("XDG_DATA_DIRS") is { Length: > 0 } d ? d.Split(':', StringSplitOptions.RemoveEmptyEntries) : ["/usr/local/share", "/usr/share"];
        foreach (var dir in dataDirs)
        {
            yield return Path.Combine(dir, "vulkan", "implicit_layer.d");
        }
        yield return "/etc/vulkan/implicit_layer.d";
        yield return "/usr/share/vulkan/implicit_layer.d";
    }

    /// <summary>Finds an installed lsfg-vk implicit layer (e.g. from a package or the Decky plugin).</summary>
    public static Install Find(Func<string, string?> env)
    {
        Install? legacy = null;
        foreach (var dir in ImplicitLayerDirs(env).Distinct())
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*.json").ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var f in files)
            {
                string text;
                try
                {
                    text = File.ReadAllText(f);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                if (text.Contains(LayerName, StringComparison.Ordinal))
                {
                    return new(Kind.V2, f);
                }
                if (text.Contains(LegacyLayerName, StringComparison.Ordinal))
                {
                    legacy ??= new(Kind.Legacy, f);
                }
            }
        }
        return legacy ?? new(Kind.None, null);
    }

    /// <summary>Env for an Eden launch with lsfg-vk frame gen at <paramref name="multiplier"/>.</summary>
    public static Dictionary<string, string> EnableEnv(string dllPath, int multiplier) => new()
    {
        ["LSFGVK_ENV"] = "1",
        ["LSFGVK_DLL_PATH"] = dllPath,
        ["LSFGVK_MULTIPLIER"] = multiplier.ToString(System.Globalization.CultureInfo.InvariantCulture),
        // Handheld-friendly defaults (what the user picked for their own Deck profile).
        ["LSFGVK_FLOW_SCALE"] = "0.75",
        ["LSFGVK_PERFORMANCE_MODE"] = "1",
    };

    /// <summary>Env that keeps an installed lsfg-vk out of this launch.</summary>
    public static Dictionary<string, string> DisableEnv() => new() { ["DISABLE_LSFGVK"] = "1" };
}
