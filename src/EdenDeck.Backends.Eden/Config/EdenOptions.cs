using EdenDeck.Core.Settings;

namespace EdenDeck.Backends.Eden.Config;

/// <summary>
/// The curated Eden options EdenDeck shows, with one-line explanations. Option ids are Eden's
/// own INI keys. Choice values are Eden enum *names*; they are translated to the installed
/// version's integer index when written (indices differ between versions).
/// An option is hidden when the installed Eden's schema doesn't have the key.
/// </summary>
public static class EdenOptions
{
    private static OptionChoice C(string v, string l) => new(v, l);

    public static readonly IReadOnlyList<OptionDefinition> All =
    [
        new()
        {
            Id = "resolution_setup", Group = "Graphics", Label = "Resolution scale", Type = OptionType.Choice,
            Help = "How sharp games render. Higher looks better but costs GPU time; 1x is the Switch's own resolution.",
            DefaultValue = "Res1X",
            Choices = [C("Res1_2X", "0.5x"), C("Res3_4X", "0.75x"), C("Res1X", "1x (native)"), C("Res3_2X", "1.5x"),
                       C("Res2X", "2x"), C("Res3X", "3x"), C("Res4X", "4x")],
        },
        new()
        {
            Id = "scaling_filter", Group = "Graphics", Label = "Upscaling filter", Type = OptionType.Choice,
            Help = "How the image is stretched to your screen. FSR sharpens; SGSR is an alternative (Nightly).",
            DefaultValue = "NearestNeighbor",
            Choices = [C("NearestNeighbor", "Nearest"), C("Bilinear", "Bilinear"), C("Bicubic", "Bicubic"),
                       C("ScaleForce", "ScaleForce"), C("Fsr", "AMD FSR"), C("Sgsr", "SGSR"), C("Area", "Area")],
        },
        new()
        {
            Id = "fsr_sharpening_slider", Group = "Graphics", Label = "FSR sharpness", Type = OptionType.Int,
            Help = "Only used with the FSR filter. Higher is sharper; too high adds halos.",
            Min = 0, Max = 200, Step = 5, DefaultValue = "25",
        },
        new()
        {
            Id = "anti_aliasing", Group = "Graphics", Label = "Anti-aliasing", Type = OptionType.Choice,
            Help = "Smooths jagged edges. FXAA is cheap, SMAA looks better.",
            DefaultValue = "None", Choices = [C("None", "Off"), C("Fxaa", "FXAA"), C("Smaa", "SMAA")],
        },
        new()
        {
            Id = "use_vsync", Group = "Graphics", Label = "VSync", Type = OptionType.Choice,
            Help = "FIFO avoids tearing (recommended on Steam Deck). Mailbox lowers latency; Immediate may tear.",
            DefaultValue = "Fifo",
            Choices = [C("Fifo", "On (FIFO)"), C("Mailbox", "Mailbox"), C("FifoRelaxed", "Relaxed"), C("Immediate", "Off")],
        },
        new()
        {
            Id = "backend", Group = "Graphics", Label = "Renderer", Type = OptionType.Choice,
            Help = "Vulkan is fastest and required for the EdenDeck overlay. OpenGL is a fallback.",
            DefaultValue = "Vulkan", Choices = [C("Vulkan", "Vulkan"), C("OpenGL_GLSL", "OpenGL")],
        },
        new()
        {
            Id = "gpu_accuracy", Group = "Graphics", Label = "GPU accuracy", Type = OptionType.Choice,
            Help = "High fixes more graphical bugs; Low is faster but some games glitch.",
            DefaultValue = "High", Choices = [C("Low", "Low"), C("Medium", "Medium (Stable only)"), C("High", "High")],
        },
        new()
        {
            Id = "use_disk_shader_cache", Group = "Graphics", Label = "Shader cache", Type = OptionType.Bool,
            Help = "Saves compiled shaders so stutter goes away on later runs. Leave on.",
            DefaultValue = "true",
        },
        new()
        {
            Id = "use_asynchronous_shaders", Group = "Graphics", Label = "Async shaders", Type = OptionType.Bool,
            Help = "Less stutter while new shaders compile, at the cost of brief missing effects.",
            DefaultValue = "false",
        },
        new()
        {
            Id = "use_docked_mode", Group = "System", Label = "Console mode", Type = OptionType.Choice,
            Help = "Docked renders at TV resolution; Handheld uses the lighter portable profile.",
            DefaultValue = "Docked", Choices = [C("Docked", "Docked"), C("Handheld", "Handheld")],
        },
        new()
        {
            Id = "cpu_accuracy", Group = "System", Label = "CPU accuracy", Type = OptionType.Choice,
            Help = "Auto is right for almost every game. Unsafe is faster but can crash.",
            DefaultValue = "Auto", Choices = [C("Auto", "Auto"), C("Accurate", "Accurate"), C("Unsafe", "Unsafe")],
        },
        new()
        {
            Id = "use_speed_limit", Group = "System", Label = "Speed limit", Type = OptionType.Bool,
            Help = "Keeps games at their normal speed. Turn off only to fast-forward.",
            DefaultValue = "true",
        },
        new()
        {
            Id = "use_multi_core", Group = "System", Label = "Multicore CPU", Type = OptionType.Bool,
            Help = "Uses several CPU cores. Much faster; leave on.",
            DefaultValue = "true",
        },
    ];

    public static OptionDefinition? Find(string id) => All.FirstOrDefault(o => o.Id == id);
}
