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
    private static OptionChoice C(string v, string l, string? d = null) => new(v, l, d);

    public static readonly IReadOnlyList<OptionDefinition> All =
    [
        new()
        {
            Id = "resolution_setup", Group = "Graphics", Label = "Resolution scale", Type = OptionType.Choice,
            Help = "How sharp games render. Higher looks better but costs GPU time; 1x is the Switch's own resolution.",
            DefaultValue = "Res1X",
            Choices = [C("Res1_2X", "0.5x", "Blurry, but frees a lot of GPU time. Only for games that can't hold their frame rate."), C("Res3_4X", "0.75x", "Slightly soft. A good rescue for demanding games on Steam Deck."), C("Res1X", "1x (native)", "What the Switch itself renders. The safe choice on Steam Deck."), C("Res3_2X", "1.5x", "Noticeably sharper. Fine for lighter games on Deck, easy on a desktop GPU."),
                       C("Res2X", "2x", "About 1080p docked. Desktop GPUs; too heavy for most games on Deck."), C("Res3X", "3x", "About 1440p docked. Needs a strong desktop GPU."), C("Res4X", "4x", "About 4K docked. High-end GPUs only.")],
        },
        new()
        {
            Id = "scaling_filter", Group = "Graphics", Label = "Upscaling filter", Type = OptionType.Choice,
            Help = "How the image is stretched to your screen. FSR sharpens; SGSR is an alternative (Nightly).",
            DefaultValue = "NearestNeighbor",
            Choices = [C("NearestNeighbor", "Nearest", "Crisp blocky pixels, no smoothing. Free."), C("Bilinear", "Bilinear", "Soft and smooth. Free."), C("Bicubic", "Bicubic", "A little sharper than bilinear. Nearly free."),
                       C("ScaleForce", "ScaleForce", "Edge-aware upscaling, good for 2D and pixel art. Light cost."), C("Fsr", "AMD FSR", "Sharp upscaling, pairs well with lower resolution scales on Deck. Small cost; tune with FSR sharpness."), C("Sgsr", "SGSR", "Alternative sharp upscaler. Eden Nightly only."), C("Area", "Area", "Best when shrinking a higher-resolution image down to your screen.")],
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
            DefaultValue = "None", Choices = [C("None", "Off", "No smoothing, no cost."), C("Fxaa", "FXAA", "Very cheap, slightly blurs fine detail."), C("Smaa", "SMAA", "Cleaner edges than FXAA for a little more GPU time.")],
        },
        new()
        {
            Id = "use_vsync", Group = "Graphics", Label = "VSync", Type = OptionType.Choice,
            Help = "FIFO avoids tearing (recommended on Steam Deck). Mailbox lowers latency; Immediate may tear.",
            DefaultValue = "Fifo",
            Choices = [C("Fifo", "On (FIFO)", "No tearing and steady pacing. Adds a frame or so of input lag."), C("Mailbox", "Mailbox", "No tearing with lower lag, but uneven pacing on some displays."), C("FifoRelaxed", "Relaxed", "Like FIFO, but tears briefly instead of stuttering when a frame is late."), C("Immediate", "Off", "Lowest lag, visible tearing.")],
        },
        new()
        {
            Id = "backend", Group = "Graphics", Label = "Renderer", Type = OptionType.Choice,
            Help = "Vulkan is fastest and required for the EdenDeck overlay. OpenGL is a fallback.",
            DefaultValue = "Vulkan", Choices = [C("Vulkan", "Vulkan", "Fastest on Linux and needed for the overlay and frame generation."), C("OpenGL_GLSL", "OpenGL", "Only if a game breaks on Vulkan. Slower, and no overlay.")],
        },
        new()
        {
            Id = "gpu_accuracy", Group = "Graphics", Label = "GPU accuracy", Type = OptionType.Choice,
            Help = "High fixes more graphical bugs; Low is faster but some games glitch.",
            DefaultValue = "High", Choices = [C("Low", "Low", "Fastest. Some games show missing effects or flicker."), C("Medium", "Medium (Stable only)", "A middle ground; only in Eden Stable."), C("High", "High", "Fewest graphics bugs. Costs some GPU time.")],
        },
        new()
        {
            Id = "use_disk_shader_cache", Group = "Graphics", Label = "Shader cache", Type = OptionType.Bool,
            Help = "Saves compiled shaders so stutter goes away on later runs. Leave on.",
            OnDetail = "Stutter only happens the first time an effect appears.",
            OffDetail = "Every effect stutters each time you play.",
            DefaultValue = "true",
        },
        new()
        {
            Id = "use_asynchronous_shaders", Group = "Graphics", Label = "Async shaders", Type = OptionType.Bool,
            Help = "Less stutter while new shaders compile, at the cost of brief missing effects.",
            OnDetail = "Smoother when new effects appear; they may pop in a moment late.",
            OffDetail = "Effects are always correct, with short hitches while they compile.",
            DefaultValue = "false",
        },
        new()
        {
            Id = "use_docked_mode", Group = "System", Label = "Console mode", Type = OptionType.Choice,
            Help = "Docked renders at TV resolution; Handheld uses the lighter portable profile.",
            DefaultValue = "Docked", Choices = [C("Docked", "Docked", "TV mode: games render at higher resolution and may look sharper but run heavier."), C("Handheld", "Handheld", "Portable mode: lighter, often steadier frame rates on Steam Deck.")],
        },
        new()
        {
            Id = "cpu_accuracy", Group = "System", Label = "CPU accuracy", Type = OptionType.Choice,
            Help = "Auto is right for almost every game. Unsafe is faster but can crash.",
            DefaultValue = "Auto", Choices = [C("Auto", "Auto", "Picks safe speed-ups automatically. Use this."), C("Accurate", "Accurate", "Slowest; only for a game that crashes or misbehaves on Auto."), C("Unsafe", "Unsafe", "Extra speed from skipping checks. Can crash or glitch.")],
        },
        new()
        {
            Id = "use_speed_limit", Group = "System", Label = "Speed limit", Type = OptionType.Bool,
            Help = "Keeps games at their normal speed. Turn off only to fast-forward.",
            OnDetail = "Games run at their intended speed.",
            OffDetail = "Games run as fast as your PC can push them. Only for fast-forwarding.",
            DefaultValue = "true",
        },
        new()
        {
            Id = "use_multi_core", Group = "System", Label = "Multicore CPU", Type = OptionType.Bool,
            Help = "Uses several CPU cores. Much faster; leave on.",
            OnDetail = "Much faster. Leave on.",
            OffDetail = "Single core: very slow, only for troubleshooting.",
            DefaultValue = "true",
        },
    ];

    public static OptionDefinition? Find(string id) => All.FirstOrDefault(o => o.Id == id);
}
