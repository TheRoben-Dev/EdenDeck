using EdenDeck.Backends.Mgba;
using EdenDeck.Core;
using EdenDeck.Core.Backends;
using EdenDeck.Core.Config;
using EdenDeck.Core.Launch;
using EdenDeck.Core.Library;
using EdenDeck.Core.Profiles;
using EdenDeck.Core.Settings;

namespace EdenDeck.Backends.MelonDs;

/// <summary>
/// The Nintendo DS through unmodified melonDS. Games are .nds files, known by the game code in
/// their header ("NTR-AMCE"). Launch is <c>melonDS -f &lt;rom&gt;</c>. melonDS has no per-game
/// settings, so a game's own settings are laid over a copy of melonDS.toml that it reads for that
/// run (through XDG_CONFIG_HOME). melonDS draws with OpenGL or in software, so frame generation and
/// Scanline's Vulkan layer (CRT effect, quick menu) can't reach it. The DS BIOS and firmware are
/// optional (melonDS has free replacements built in) and always the player's own.
/// </summary>
public sealed class MelonDsBackend : IEmulatorBackend, ILibraryScanner, ILaunchPlanner, ISystemFiles
{
    public const string BackendId = "melonds";

    private readonly Func<string, string?> _env;

    public MelonDsBackend(AppPaths app, HttpClient? http = null, Func<string, string?>? env = null)
    {
        _env = env ?? Environment.GetEnvironmentVariable;
        DataDir = app.Data;
        Installs = new MelonDsInstallManager(app.EmulatorsDir, http, _env);
        SettingsImpl = new MelonDsSettings(() => Paths, app.Data);
        Saves = new MelonDsSaves(() => Paths, app.Data);
        Cheats = new MelonDsCheats(() => Paths, app.Data, http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) });
    }

    public string DataDir { get; }
    public string Id => BackendId;
    public string DisplayName => "melonDS";
    public string SystemName => "Nintendo DS";
    public IReadOnlyCollection<string> FileExtensions { get; } = [".nds"];

    public MelonDsInstallManager Installs { get; }
    public MelonDsSettings SettingsImpl { get; }
    public MelonDsSaves Saves { get; }
    public MelonDsCheats Cheats { get; }
    public IInstallManager Install => Installs;
    public ILibraryScanner Library => this;
    public ISettingsProvider Settings => SettingsImpl;
    public ILaunchPlanner Launch => this;
    public ISystemFiles? SystemFiles => this;
    public Core.Saves.ISaveData? SaveData => Saves;
    public PresetSet? Presets => MelonDsSettings.Performance;

    public bool IsFlatpak => Installs.Current?.ExecutablePath == MelonDsInstallManager.FlatpakExe;

    /// <summary>melonDS's folders, inside the Flatpak's sandbox home when that's the melonDS in use.</summary>
    public MelonDsPaths Paths => new(_env, IsFlatpak);

    private string Home => _env("HOME") ?? "";

    // ---- library ----

    public GameEntry? TryRead(string path)
    {
        NdsHeader? header;
        try
        {
            header = NdsRom.ReadHeader(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            return null;
        }
        if (header == null)
        {
            return null;
        }
        // The No-Intro name, else the file name (the header's own title is short and in capitals).
        var name = header.HasGameCode && DsNames.Find(header.GameCode) is { } known ? known : Path.GetFileNameWithoutExtension(path);
        return new GameEntry
        {
            BackendId = Id,
            TitleId = header.TitleId,
            Title = MgbaRom.CleanTitle(name + ".nds"),
            Path = path,
            Kind = ContentKind.Base,
            TitleIdSource = "header",
        };
    }

    /// <summary>The cartridge's game code ("AMCE") from Scanline's id, or null for homebrew.</summary>
    public static string? GameCode(GameEntry game) =>
        game.TitleId is { Length: 8 } id && id.StartsWith("NTR-", StringComparison.Ordinal) && !id.StartsWith("NTR-H", StringComparison.Ordinal) ? id[4..] : null;

    /// <summary>The game's 32x32 banner icon, drawn into a PNG once and cached.</summary>
    public string? IconFor(GameEntry game)
    {
        if (game.BackendId != Id || string.IsNullOrEmpty(game.TitleId) || !File.Exists(game.Path))
        {
            return null;
        }
        var cached = Path.Combine(DataDir, "melonds", "icons", game.TitleId + ".png");
        if (File.Exists(cached))
        {
            return cached;
        }
        try
        {
            if (NdsRom.ReadHeader(game.Path) is not { } header || NdsRom.ReadIcon(game.Path, header) is not { } rgba)
            {
                return null;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
            File.WriteAllBytes(cached, NdsRom.Png(rgba, 32, 32));
            return cached;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            return null;
        }
    }

    // ---- launch ----

    public LaunchPlan Plan(GameEntry game, GameProfile profile, LaunchOptions options)
    {
        var cur = Installs.Current ?? throw new InvalidOperationException("melonDS isn't installed yet. Install it under Emulators → melonDS (Nintendo DS).");
        if (!File.Exists(game.Path))
        {
            throw new FileNotFoundException($"\"{game.Title}\" isn't where it was ({game.Path}). Rescan your library.", game.Path);
        }
        var paths = Paths;
        var args = new List<string>();
        string exe;
        var notes = new List<string> { IsFlatpak ? "melonDS (Flatpak)" : $"melonDS {cur.Version}" };
        var env = new Dictionary<string, string>();
        var prepare = new List<Action>();
        if (FrameGenPolicy.Decide(profile.FrameGen, profile.NativeFps).Enabled || profile.Crt > 0)
        {
            notes.Add("warning: frame generation and the CRT effect can't reach melonDS, which draws with OpenGL or in software.");
        }

        var overrides = string.IsNullOrEmpty(game.TitleId)
            ? new Dictionary<string, string?>()
            : SettingsImpl.ReadPerGame(game.TitleId).ToDictionary(kv => kv.Key, kv => (string?)kv.Value);
        if (overrides.Count > 0)
        {
            notes.Add("settings just for this game");
        }
        if (IsFlatpak)
        {
            exe = "flatpak";
            args.AddRange(["run"]);
            if (overrides.Count > 0)
            {
                args.Add("--env=XDG_CONFIG_HOME=" + paths.RunRoot);
            }
            args.Add(MelonDsInstallManager.FlatpakId);
        }
        else
        {
            exe = cur.ExecutablePath;
            if (overrides.Count > 0)
            {
                env["XDG_CONFIG_HOME"] = paths.RunRoot;
            }
        }

        var gamepad = options.Gamepads.Any(p => !string.IsNullOrWhiteSpace(p));
        if (gamepad)
        {
            notes.Add("controller: DS layout (A right, B bottom)");
        }
        var cheatsOn = !string.IsNullOrEmpty(game.TitleId) && Cheats.EnabledCount(game) > 0;
        if (cheatsOn)
        {
            notes.Add("cheats on");
        }
        var settings = SettingsImpl;
        var stamp = Path.Combine(DataDir, "melonds", "pad.stamp");
        prepare.Add(() =>
        {
            var t = paths.Load();
            if (gamepad)
            {
                MelonDsPad.Ensure(t, stamp);
            }
            if (cheatsOn)
            {
                t.SetBool("Instance0.EnableCheats", true);
            }
            t.Save(paths.ConfigFile);
            if (overrides.Count > 0)
            {
                settings.Apply(t, overrides);
                t.Save(paths.RunConfigFile);
            }
        });
        if (options.Fullscreen)
        {
            args.Add("-f");
        }
        args.Add(game.Path);
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

    /// <summary>melonDS's own window (its settings, input mapping, cheats, anything Scanline doesn't cover).</summary>
    public LaunchPlan PlanUi()
    {
        var cur = Installs.Current ?? throw new InvalidOperationException("melonDS isn't installed yet. Install it under Emulators → melonDS (Nintendo DS).");
        var (exe, args) = IsFlatpak ? ("flatpak", new List<string> { "run", MelonDsInstallManager.FlatpakId }) : (cur.ExecutablePath, new List<string>());
        return new LaunchPlan { Executable = exe, Arguments = args, WorkingDirectory = Home.Length > 0 ? Home : "/", Notes = ["melonDS window"] };
    }

    // ---- BIOS and firmware (optional, the player's own) ----

    public sealed record SystemFile(string Kind, string Key, string FileName, long[] Sizes, string Help);

    public static readonly IReadOnlyList<SystemFile> Files =
    [
        new("ARM9 BIOS", "DS.BIOS9Path", "bios9.bin", [4 << 10], "the 4 KB ARM9 BIOS (bios9.bin)"),
        new("ARM7 BIOS", "DS.BIOS7Path", "bios7.bin", [16 << 10], "the 16 KB ARM7 BIOS (bios7.bin)"),
        new("Firmware", "DS.FirmwarePath", "firmware.bin", [128 << 10, 256 << 10, 512 << 10], "the firmware (firmware.bin: 128, 256 or 512 KB)"),
    ];

    private static bool FileOk(string? path) => !string.IsNullOrEmpty(path) && File.Exists(path);

    /// <summary>All three are set, so melonDS uses the real BIOS and firmware.</summary>
    public bool UsesOwnBios
    {
        get
        {
            var t = Paths.Load();
            return Files.All(f => FileOk(t.Get(f.Key)));
        }
    }

    public IReadOnlyList<SystemFileStatus> Status()
    {
        var t = Paths.Load();
        return Files.Select(f => new SystemFileStatus(f.Kind, FileOk(t.Get(f.Key)),
            FileOk(t.Get(f.Key)) ? $"Using {Path.GetFileName(t.Get(f.Key))}." : $"Optional: {f.Help} from your own DS. Without it melonDS uses its free built-in one.")).ToList();
    }

    /// <summary>Which of the three files this is, by its size; throws when it's none of them.</summary>
    public static SystemFile Identify(string path)
    {
        var len = new FileInfo(path).Length;
        var match = Files.FirstOrDefault(f => f.Sizes.Contains(len))
                    ?? throw new InvalidDataException("That isn't a DS BIOS or firmware file. Choose bios9.bin (4 KB), bios7.bin (16 KB) or firmware.bin (128-512 KB) dumped from your own DS.");
        var head = new byte[Math.Min(len, 4096)];
        using (var f = File.OpenRead(path))
        {
            f.ReadExactly(head);
        }
        if (head.All(b => b == 0) || head.All(b => b == 0xFF))
        {
            throw new InvalidDataException("That file is blank, so it isn't a real BIOS or firmware dump.");
        }
        return match;
    }

    /// <summary>
    /// Copies one of the player's files into melonDS's folder and points melonDS at it. Once all
    /// three are there melonDS is switched to them (it needs all three together). Returns which it was.
    /// </summary>
    public SystemFile ImportSystemFile(string path)
    {
        var kind = Identify(path);
        var p = Paths;
        var target = Path.Combine(p.SystemDir, kind.FileName);
        Directory.CreateDirectory(p.SystemDir);
        if (Path.GetFullPath(target) != Path.GetFullPath(path))
        {
            File.Copy(path, target, overwrite: true);
        }
        var t = p.Load();
        t.SetString(kind.Key, target);
        if (Files.All(f => FileOk(t.Get(f.Key))))
        {
            t.SetBool("Emu.ExternalBIOSEnable", true);
        }
        t.Save(p.ConfigFile);
        return kind;
    }

    // ---- storage ----

    public sealed record GameUsage(GameEntry Game, long Files, long Saves)
    {
        public long Total => Files + Saves;
    }

    public GameUsage Usage(GameEntry game) => new(game,
        File.Exists(game.Path) ? new FileInfo(game.Path).Length : 0,
        Saves.Saves(game).Sum(s => s.Bytes));
}
