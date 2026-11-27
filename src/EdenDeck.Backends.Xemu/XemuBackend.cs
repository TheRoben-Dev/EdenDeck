using EdenDeck.Core.Config;
using EdenDeck.Backends.Pcsx2;
using EdenDeck.Core;
using EdenDeck.Core.Backends;
using EdenDeck.Core.Launch;
using EdenDeck.Core.Library;
using EdenDeck.Core.Profiles;
using EdenDeck.Core.Settings;

namespace EdenDeck.Backends.Xemu;

/// <summary>
/// The original Xbox through unmodified xemu. Games are XISO images (.iso, .xiso) or full Redump
/// dumps, known by the title id in their default.xbe ("4D530004"). Launch is
/// <c>xemu [-config_path run.toml] -full-screen -dvd_path &lt;image&gt;</c>. xemu has no per-game
/// settings, so a game's own settings are laid over a copy of xemu.toml for that run. Saves live
/// inside the Xbox hard disk image, so they're backed up as the whole disk rather than per game.
/// The MCPX boot ROM, flash BIOS and hard disk are always the player's own.
/// </summary>
public sealed class XemuBackend : IEmulatorBackend, ILibraryScanner, ILaunchPlanner, ISystemFiles
{
    public const string BackendId = "xemu";
    public const string Renderer = "display.renderer";

    private readonly Func<string, string?> _env;

    public XemuBackend(AppPaths app, HttpClient? http = null, Func<string, string?>? env = null)
    {
        _env = env ?? Environment.GetEnvironmentVariable;
        DataDir = app.Data;
        Installs = new XemuInstallManager(app.EmulatorsDir, http, _env);
        SettingsImpl = new XemuSettings(() => Paths, app.Data);
    }

    public string DataDir { get; }
    public string Id => BackendId;
    public string DisplayName => "xemu";
    public string SystemName => "Xbox";
    public IReadOnlyCollection<string> FileExtensions { get; } = [".iso", ".xiso"];

    public XemuInstallManager Installs { get; }
    public XemuSettings SettingsImpl { get; }
    public IInstallManager Install => Installs;
    public ILibraryScanner Library => this;
    public ISettingsProvider Settings => SettingsImpl;
    public ILaunchPlanner Launch => this;
    public ISystemFiles? SystemFiles => this;
    public PresetSet? Presets => XemuSettings.Performance;

    // Saves are inside the hard disk image (one file for every game): see BackupHardDisk.

    public bool IsFlatpak => Installs.Current?.ExecutablePath == XemuInstallManager.FlatpakExe;

    /// <summary>xemu's folders, inside the Flatpak's sandbox home when that's the xemu in use.</summary>
    public XemuPaths Paths => new(_env, IsFlatpak);

    private string Home => _env("HOME") ?? "";

    /// <summary>The config a game with its own settings is started with (inside xemu's folder, so the Flatpak can read it too).</summary>
    public string RunConfig => Path.Combine(Paths.SystemDir, "run.toml");

    // ---- library ----

    public GameEntry? TryRead(string path)
    {
        XboxDiscInfo? disc;
        try
        {
            disc = XboxDisc.Read(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            return null;
        }
        if (disc == null)
        {
            return null;  // not an Xbox image (a PS2 or PS3 disc, say)
        }
        var name = disc.Serial is { } serial && XboxNames.Find(serial) is { } known ? known : disc.Name ?? Path.GetFileNameWithoutExtension(path);
        return new GameEntry
        {
            BackendId = Id,
            TitleId = disc.Hex,
            Title = Ps2Names.CleanTitle(name),
            Path = path,
            Kind = ContentKind.Base,
            TitleIdSource = "xbe",
        };
    }

    // ---- launch ----

    public LaunchPlan Plan(GameEntry game, GameProfile profile, LaunchOptions options)
    {
        var cur = Installs.Current ?? throw new InvalidOperationException("xemu isn't installed yet. Install it under Emulators → Xbox.");
        var paths = Paths;
        var toml = paths.Load();
        if (!FileOk(toml.Get(BootRomKey)) || !FileOk(toml.Get(FlashKey)))
        {
            throw new InvalidOperationException("xemu needs your Xbox's MCPX boot ROM and flash BIOS first. Choose them under Emulators → Xbox.");
        }
        if (!File.Exists(game.Path))
        {
            throw new FileNotFoundException($"\"{game.Title}\" isn't where it was ({game.Path}). Rescan your library.", game.Path);
        }
        var args = new List<string>();
        string exe;
        var notes = new List<string> { IsFlatpak ? "xemu (Flatpak)" : $"xemu {cur.Version}" };
        var env = new Dictionary<string, string>();
        var prepare = new List<Action>();
        var vulkanWanted = FrameGenPolicy.Decide(profile.FrameGen, profile.NativeFps).Enabled || profile.Crt > 0;
        if (IsFlatpak)
        {
            exe = "flatpak";
            args.AddRange(["run", XemuInstallManager.FlatpakId]);
            if (vulkanWanted)
            {
                notes.Add("warning: frame generation and the CRT effect can't reach into the xemu Flatpak. Install the xemu AppImage under Emulators → Xbox to use them.");
            }
        }
        else
        {
            exe = cur.ExecutablePath;
            options.Vulkan?.Apply(profile, options, env, prepare, notes);
        }
        if (!FileOk(toml.Get(HddKey)))
        {
            notes.Add("warning: no Xbox hard disk is set, so games can't save. Choose one under Emulators → Xbox.");
        }

        // The game's own settings, and Vulkan when frame generation or the CRT effect wants it.
        var overrides = string.IsNullOrEmpty(game.TitleId)
            ? new Dictionary<string, string?>()
            : SettingsImpl.ReadPerGame(game.TitleId).ToDictionary(kv => kv.Key, kv => (string?)kv.Value);
        if (overrides.Count > 0)
        {
            notes.Add("settings just for this game");
        }
        var renderer = overrides.GetValueOrDefault(Renderer) ?? toml.Get(Renderer);
        if (vulkanWanted && !IsFlatpak)
        {
            if (renderer == null)
            {
                overrides[Renderer] = "VULKAN";
                notes.Add("renderer: Vulkan (for frame generation and the CRT effect)");
            }
            else if (!renderer.Equals("VULKAN", StringComparison.OrdinalIgnoreCase))
            {
                notes.Add("warning: frame generation and the CRT effect need xemu's Vulkan renderer; this game is set to OpenGL.");
            }
        }

        var gamepad = options.Gamepads.Any(p => !string.IsNullOrWhiteSpace(p));
        if (gamepad)
        {
            notes.Add("controller: first gamepad on port 1");
        }
        if (options.Players.Count > 0 && options.Players[0].SteamSlot != null && options.Players[0].GamepadIndex > 0)
        {
            // xemu binds Steam Input pads in the order Steam lists them and can't be pointed at one.
            notes.Add($"warning: xemu gives port 1 to the first controller Steam lists, not {options.Players[0].Name}. To fix it, open Steam's Quick Access menu (the … button), go to the gear tab, choose Rearrange controller order and put your controller first.");
        }
        var settings = SettingsImpl;
        var run = RunConfig;
        prepare.Add(() =>
        {
            var t = paths.Load();
            Ready(t, gamepad);
            t.Save(paths.ConfigFile);
            if (overrides.Count > 0)
            {
                settings.Apply(t, overrides);
                Directory.CreateDirectory(Path.GetDirectoryName(run)!);
                t.Save(run);
            }
        });
        if (overrides.Count > 0)
        {
            args.AddRange(["-config_path", run]);
        }
        if (options.Fullscreen)
        {
            args.Add("-full-screen");
        }
        args.AddRange(["-dvd_path", game.Path]);
        return new LaunchPlan
        {
            Executable = exe,
            Arguments = args,
            WorkingDirectory = Home.Length > 0 ? Home : "/",
            Environment = env,
            Prepare = prepare,
            Notes = notes,
        };
    }

    /// <summary>
    /// What xemu needs to go straight into the game: its welcome window off (it stops the game
    /// starting), no update pop-up (Scanline updates xemu), and the controller on port 1 instead of
    /// the keyboard.
    /// </summary>
    internal static void Ready(TomlFile toml, bool gamepad)
    {
        toml.SetBool("general.show_welcome", false);
        toml.SetBool("general.updates.check", false);
        if (gamepad && string.Equals(toml.Get("input.bindings.port1"), "keyboard", StringComparison.OrdinalIgnoreCase))
        {
            toml.Remove("input.bindings.port1");  // empty: xemu puts the first gamepad there
        }
    }

    /// <summary>xemu's own window (its settings, controller mapping, anything Scanline doesn't cover).</summary>
    public LaunchPlan PlanUi()
    {
        var cur = Installs.Current ?? throw new InvalidOperationException("xemu isn't installed yet. Install it under Emulators → Xbox.");
        var (exe, args) = IsFlatpak ? ("flatpak", new List<string> { "run", XemuInstallManager.FlatpakId }) : (cur.ExecutablePath, new List<string>());
        var paths = Paths;
        return new LaunchPlan
        {
            Executable = exe,
            Arguments = args,
            WorkingDirectory = Home.Length > 0 ? Home : "/",
            Prepare =
            [
                () =>
                {
                    var t = paths.Load();
                    t.SetBool("general.show_welcome", false);
                    t.Save(paths.ConfigFile);
                },
            ],
            Notes = ["xemu window"],
        };
    }

    // ---- system files (the player's own) ----

    public const string BootRomKey = "sys.files.bootrom_path";
    public const string FlashKey = "sys.files.flashrom_path";
    public const string HddKey = "sys.files.hdd_path";
    public const int BootRomSize = 512;
    public const int FlashSize = 256 << 10;
    public const int FlashSizeLarge = 1 << 20;

    /// <summary>The boot ROM and flash BIOS are set (the hard disk is only needed for saves).</summary>
    public bool SystemFilesReady
    {
        get
        {
            var t = Paths.Load();
            return FileOk(t.Get(BootRomKey)) && FileOk(t.Get(FlashKey));
        }
    }

    public bool HardDiskSet => FileOk(Paths.Load().Get(HddKey));

    private static bool FileOk(string? path) => !string.IsNullOrEmpty(path) && File.Exists(path);

    public IReadOnlyList<SystemFileStatus> Status()
    {
        var t = Paths.Load();
        string Name(string key) => Path.GetFileName(t.Get(key) ?? "");
        return
        [
            new SystemFileStatus("MCPX boot ROM", FileOk(t.Get(BootRomKey)),
                FileOk(t.Get(BootRomKey)) ? $"Using {Name(BootRomKey)}." : "Needed to play. The 512-byte boot ROM dumped from your own Xbox; Scanline never downloads it."),
            new SystemFileStatus("Flash BIOS", FileOk(t.Get(FlashKey)),
                FileOk(t.Get(FlashKey)) ? $"Using {Name(FlashKey)}." : "Needed to play. The BIOS dumped from your own Xbox (256 KB or 1 MB)."),
            new SystemFileStatus("Hard disk", FileOk(t.Get(HddKey)),
                FileOk(t.Get(HddKey)) ? $"Using {t.Get(HddKey)}. Every game's saves are kept on it." : "Needed for saves. An Xbox hard disk image (.qcow2), such as one made from your own console."),
        ];
    }

    /// <summary>Checks an MCPX boot ROM dump: exactly 512 bytes, as xemu requires.</summary>
    public static void CheckBootRom(string path)
    {
        if (new FileInfo(path).Length != BootRomSize)
        {
            throw new InvalidDataException("That isn't an MCPX boot ROM. Choose the 512-byte boot ROM dumped from your Xbox (often mcpx_1.0.bin).");
        }
    }

    /// <summary>Checks a flash BIOS dump: 256 KB or 1 MB, and not blank.</summary>
    public static void CheckFlash(string path)
    {
        var len = new FileInfo(path).Length;
        var bad = new InvalidDataException("That isn't an Xbox flash BIOS. Choose the BIOS dumped from your Xbox (256 KB or 1 MB).");
        if (len is not (FlashSize or FlashSizeLarge))
        {
            throw bad;
        }
        var head = new byte[4096];
        using var f = File.OpenRead(path);
        f.ReadExactly(head);
        if (head.All(b => b == 0) || head.All(b => b == 0xFF))
        {
            throw bad;
        }
    }

    /// <summary>Checks a hard disk image: QEMU's qcow2 format, which is what xemu uses.</summary>
    public static void CheckHdd(string path)
    {
        var head = new byte[4];
        using (var f = File.OpenRead(path))
        {
            if (f.Length < 4)
            {
                throw new InvalidDataException("That isn't an Xbox hard disk image. Choose a .qcow2 hard disk image.");
            }
            f.ReadExactly(head);
        }
        if (!(head[0] == (byte)'Q' && head[1] == (byte)'F' && head[2] == (byte)'I' && head[3] == 0xFB))
        {
            throw new InvalidDataException("That isn't an Xbox hard disk image. Choose a .qcow2 hard disk image.");
        }
    }

    /// <summary>Copies the player's boot ROM next to xemu's settings and points xemu at it.</summary>
    public string ImportBootRom(string path)
    {
        CheckBootRom(path);
        return Import(path, "mcpx.bin", BootRomKey);
    }

    /// <summary>Copies the player's flash BIOS next to xemu's settings and points xemu at it.</summary>
    public string ImportFlash(string path)
    {
        CheckFlash(path);
        return Import(path, "flash.bin", FlashKey);
    }

    /// <summary>Points xemu at the player's hard disk image where it is (it's large and holds every save, so it isn't copied).</summary>
    public string UseHdd(string path)
    {
        CheckHdd(path);
        var p = Paths;
        var t = p.Load();
        t.SetString(HddKey, Path.GetFullPath(path));
        t.SetBool("general.show_welcome", false);
        t.Save(p.ConfigFile);
        return Path.GetFileName(path);
    }

    private string Import(string path, string name, string key)
    {
        var p = Paths;
        var target = Path.Combine(p.SystemDir, name);
        Directory.CreateDirectory(p.SystemDir);
        if (Path.GetFullPath(target) != Path.GetFullPath(path))
        {
            File.Copy(path, target, overwrite: true);
        }
        var t = p.Load();
        t.SetString(key, target);
        t.SetBool("general.show_welcome", false);
        t.Save(p.ConfigFile);
        return Path.GetFileName(path);
    }

    // ---- the hard disk (every game's saves) ----

    public const int KeepDiskBackups = 3;

    public string DiskBackupDir => Path.Combine(DataDir, "saves", "XBOX-HDD");

    public IReadOnlyList<FileInfo> DiskBackups() =>
        Directory.Exists(DiskBackupDir)
            ? new DirectoryInfo(DiskBackupDir).EnumerateFiles("*.qcow2").OrderByDescending(f => f.Name, StringComparer.Ordinal).ToList()
            : [];

    /// <summary>Copies the hard disk image (with every game's saves) into Scanline's backups, keeping the last few. Null when there's no disk.</summary>
    public FileInfo? BackupHardDisk(DateTime? now = null)
    {
        var hdd = Paths.Load().Get(HddKey);
        if (!FileOk(hdd))
        {
            return null;
        }
        Directory.CreateDirectory(DiskBackupDir);
        var target = Path.Combine(DiskBackupDir, $"xbox_hdd-{(now ?? DateTime.Now):yyyyMMdd-HHmmss}.qcow2");
        File.Copy(hdd!, target, overwrite: true);
        foreach (var old in DiskBackups().Skip(KeepDiskBackups))
        {
            old.Delete();
        }
        return new FileInfo(target);
    }

    // ---- storage ----

    public sealed record GameUsage(GameEntry Game, long Files);

    public GameUsage Usage(GameEntry game) => new(game, File.Exists(game.Path) ? new FileInfo(game.Path).Length : 0);

    /// <summary>The hard disk image's size (shared by every game), or 0.</summary>
    public long HardDiskBytes => Paths.Load().Get(HddKey) is { } h && File.Exists(h) ? new FileInfo(h).Length : 0;
}
