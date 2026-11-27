using System.Text;
using EdenDeck.Core;
using EdenDeck.Core.Backends;
using EdenDeck.Core.Launch;
using EdenDeck.Core.Library;
using EdenDeck.Core.Profiles;
using EdenDeck.Core.Settings;

namespace EdenDeck.Backends.Pcsx2;

/// <summary>
/// PlayStation 2 through unmodified PCSX2. Discs are .iso, .cue/.bin, .chd, .cso and .zso; each is
/// known by its serial (SLUS-20946) and, for per-game settings, cheats and patches, its boot ELF's
/// CRC (see <see cref="Pcsx2Discs"/>). Launch is <c>pcsx2 -batch -nogui -fullscreen -- &lt;disc&gt;</c>.
/// PCSX2 renders with Vulkan on Linux by default, so frame generation (lsfg-vk, 60 fps rule) and
/// Scanline's layer (CRT, quick menu) work as for Switch games, except inside the Flatpak sandbox.
/// The BIOS is always the player's own dump.
/// </summary>
public sealed class Pcsx2Backend : IEmulatorBackend, ILibraryScanner, ILaunchPlanner, ISystemFiles
{
    public const string BackendId = "pcsx2";

    private readonly Func<string, string?> _env;

    public Pcsx2Backend(AppPaths app, HttpClient? http = null, Func<string, string?>? env = null)
    {
        _env = env ?? Environment.GetEnvironmentVariable;
        DataDir = app.Data;
        Installs = new Pcsx2InstallManager(app.EmulatorsDir, http, _env);
        Discs = new Pcsx2Discs(Path.Combine(app.Data, "pcsx2", "discs.json"));
        SettingsImpl = new Pcsx2Settings(() => Paths, Discs.CrcFor);
        Saves = new Pcsx2Saves(() => Paths, app.Data);
        Patches = new Pcsx2Patches(() => Paths, app.Data, http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) });
    }

    public string DataDir { get; }
    public string Id => BackendId;
    public string DisplayName => "PCSX2";
    public string SystemName => "PlayStation 2";
    public IReadOnlyCollection<string> FileExtensions { get; } = [".iso", ".cue", ".chd", ".cso", ".zso"];

    public Pcsx2InstallManager Installs { get; }
    public Pcsx2Discs Discs { get; }
    public Pcsx2Settings SettingsImpl { get; }
    public Pcsx2Saves Saves { get; }
    public Pcsx2Patches Patches { get; }
    public IInstallManager Install => Installs;
    public ILibraryScanner Library => this;
    public ISettingsProvider Settings => SettingsImpl;
    public ILaunchPlanner Launch => this;
    public ISystemFiles? SystemFiles => this;
    public Core.Saves.ISaveData? SaveData => Saves;
    public PresetSet? Presets => Pcsx2Settings.Performance;

    public bool IsFlatpak => Installs.Current?.ExecutablePath == Pcsx2InstallManager.FlatpakExe;

    /// <summary>PCSX2's folders, inside the Flatpak's sandbox home when that's the PCSX2 in use.</summary>
    public Pcsx2Paths Paths => new(_env, IsFlatpak);

    private string Home => _env("HOME") ?? "";

    // ---- library ----

    public GameEntry? TryRead(string path)
    {
        var disc = Discs.Identify(path);
        if (disc.Serial == null && !(path.EndsWith(".chd", StringComparison.OrdinalIgnoreCase) && LooksLikePs2Chd(path, disc.Source == "chd-dvd")))
        {
            return null;  // not a PS2 disc (a PS1 or PS3 disc, another system's CHD) or unreadable
        }
        var name = disc.Serial is { } s && Ps2Names.Find(s) is { } known ? known : Path.GetFileNameWithoutExtension(path);
        return new GameEntry
        {
            BackendId = Id,
            TitleId = disc.Serial ?? "",
            Title = Ps2Names.CleanTitle(name),
            Path = path,
            Kind = ContentKind.Base,
            Version = disc.Version,
            TitleIdSource = disc.Source,
        };
    }

    private static readonly HashSet<string> Ps2Folders = new(StringComparer.OrdinalIgnoreCase)
    {
        "ps2", "playstation 2", "playstation2", "pcsx2", "sony - playstation 2", "sony playstation 2",
    };

    private static readonly HashSet<string> OtherSystemFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "psx", "ps1", "psone", "playstation", "playstation 1", "sony - playstation", "psp", "saturn", "segacd", "sega cd", "megacd", "mega cd",
        "dreamcast", "dc", "naomi", "atomiswave", "pce", "pcecd", "pce-cd", "tg16", "tg-cd", "turbografx-cd", "pcfx", "3do", "neogeocd", "cdi", "cd-i",
        "jaguarcd", "amigacd32", "cd32", "gamecube", "gc", "wii", "xbox", "fmtowns", "pc88", "pc98",
    };

    /// <summary>
    /// A .chd that isn't in the DVD list: a PS2 game if a folder on its path says so (ps2,
    /// PlayStation 2…), or a DVD image in no other system's folder. CD images elsewhere are more
    /// likely PS1, Saturn or other CD systems.
    /// </summary>
    public static bool LooksLikePs2Chd(string path, bool dvd)
    {
        var folders = (Path.GetDirectoryName(path) ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (folders.Any(Ps2Folders.Contains))
        {
            return true;
        }
        return dvd && !folders.Any(OtherSystemFolders.Contains);
    }

    /// <summary>The CRC PCSX2 names this game's settings, cheats and patches after, or null until known.</summary>
    public uint? CrcOf(GameEntry game) => string.IsNullOrEmpty(game.TitleId) ? null : Discs.CrcFor(game.TitleId);

    // ---- launch ----

    /// <summary>Where PCSX2 writes its log for the game Scanline started (read afterwards to learn the disc's CRC).</summary>
    public string RunLog => Path.Combine(Paths.LogsDir, "scanline-last-run.log");

    public LaunchPlan Plan(GameEntry game, GameProfile profile, LaunchOptions options)
    {
        var cur = Installs.Current ?? throw new InvalidOperationException("PCSX2 isn't installed yet. Install it under Emulators → PlayStation 2.");
        if (!BiosInstalled)
        {
            throw new InvalidOperationException("PCSX2 needs a PS2 BIOS first. Choose your own BIOS file under Emulators → PlayStation 2.");
        }
        if (!File.Exists(game.Path))
        {
            throw new FileNotFoundException($"\"{game.Title}\" isn't where it was ({game.Path}). Rescan your library.", game.Path);
        }
        var args = new List<string>();
        string exe;
        var notes = new List<string> { IsFlatpak ? "PCSX2 (Flatpak)" : $"PCSX2 {cur.Version}" };
        var env = new Dictionary<string, string>();
        var prepare = new List<Action>();
        var vulkanWanted = FrameGenPolicy.Decide(profile.FrameGen, profile.NativeFps).Enabled || profile.Crt > 0;
        if (IsFlatpak)
        {
            exe = "flatpak";
            args.AddRange(["run", Pcsx2InstallManager.FlatpakId]);
            if (vulkanWanted)
            {
                notes.Add("warning: frame generation and the CRT effect can't reach into the PCSX2 Flatpak. Install the PCSX2 AppImage under Emulators → PlayStation 2 to use them.");
            }
        }
        else
        {
            exe = cur.ExecutablePath;
            options.Vulkan?.Apply(profile, options, env, prepare, notes);
            if (vulkanWanted && Renderer(game) is "12" or "13")
            {
                notes.Add("warning: frame generation and the CRT effect need PCSX2's Vulkan renderer; this game is set to " + (Renderer(game) == "12" ? "OpenGL." : "Software."));
            }
        }
        var paths = Paths;
        if (options.Players.Count > 0)
        {
            var sdl = options.Players.Take(Pcsx2Pads.MaxPads).Select(p => p.GamepadIndex).ToList();
            prepare.Add(() => Pcsx2Pads.Ensure(paths, sdl));
            notes.Add("controllers: " + string.Join(", ", options.Players.Take(Pcsx2Pads.MaxPads).Select((p, i) => $"player {i + 1} = {p} (SDL-{p.GamepadIndex})")));
        }
        else if (options.Gamepads.Any(p => !string.IsNullOrWhiteSpace(p)))
        {
            prepare.Add(() => Pcsx2Pads.Ensure(paths));
            notes.Add("controller: " + Pcsx2Pads.Device);
        }
        var log = RunLog;
        prepare.Add(() =>
        {
            // Scanline set the BIOS and controller, so PCSX2's first-run wizard has nothing left to do.
            var ini = paths.LoadConfigForWrite();
            if (!string.Equals(ini.Get("UI", "SetupWizardIncomplete"), "false", StringComparison.OrdinalIgnoreCase))
            {
                ini.Set("UI", "SetupWizardIncomplete", "false");
                ini.Save(paths.ConfigFile);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            File.Delete(log);
        });
        var discs = Discs;
        var disc = game.Path;
        var cleanup = new List<Action>
        {
            () =>
            {
                if (File.Exists(log) && Pcsx2Discs.ParseLog(File.ReadAllText(log)) is { } seen &&
                    (string.IsNullOrEmpty(game.TitleId) || seen.Serial == game.TitleId))
                {
                    discs.Learn(disc, seen.Serial, seen.Crc);
                }
            },
        };
        args.AddRange(["-batch", "-nogui"]);
        args.Add(options.Fullscreen ? "-fullscreen" : "-nofullscreen");
        args.AddRange(["-logfile", log]);
        if (CrcOf(game) is { } crc && File.Exists(paths.GameSettingsFile(game.TitleId, crc)))
        {
            notes.Add("settings just for this game (PCSX2 game properties)");
        }
        args.AddRange(["--", game.Path]);
        return new LaunchPlan
        {
            Executable = exe,
            Arguments = args,
            WorkingDirectory = Home.Length > 0 ? Home : "/",
            Environment = env,
            Prepare = prepare,
            Cleanup = cleanup,
            Notes = notes,
        };
    }

    /// <summary>The renderer this game runs with ("-1" automatic, "14" Vulkan, "12" OpenGL, "13" Software).</summary>
    private string Renderer(GameEntry game)
    {
        const string id = "EmuCore/GS/Renderer";
        var global = Settings.ReadGlobal().GetValueOrDefault(id) ?? "-1";
        return string.IsNullOrEmpty(game.TitleId) ? global : Settings.ReadPerGame(game.TitleId).GetValueOrDefault(id) ?? global;
    }

    /// <summary>PCSX2's own window (its settings, controller mapping, anything Scanline doesn't cover).</summary>
    public LaunchPlan PlanUi()
    {
        var cur = Installs.Current ?? throw new InvalidOperationException("PCSX2 isn't installed yet. Install it under Emulators → PlayStation 2.");
        var (exe, args) = IsFlatpak ? ("flatpak", new List<string> { "run", Pcsx2InstallManager.FlatpakId }) : (cur.ExecutablePath, new List<string>());
        return new LaunchPlan { Executable = exe, Arguments = args, WorkingDirectory = Home.Length > 0 ? Home : "/", Notes = ["PCSX2 window"] };
    }

    // ---- BIOS (the player's own dump) ----

    /// <summary>The BIOS file PCSX2 is set to use, or null.</summary>
    public string? BiosPath
    {
        get
        {
            var p = Paths;
            return Pcsx2Ini.Load(p.ConfigFile).Get("Filenames", "BIOS") is { Length: > 0 } name ? Path.Combine(p.BiosDir, name) : null;
        }
    }

    public bool BiosInstalled => BiosPath is { } b && File.Exists(b);

    public IReadOnlyList<SystemFileStatus> Status() =>
    [
        new SystemFileStatus("PS2 BIOS", BiosInstalled,
            BiosInstalled ? $"Using {Path.GetFileName(BiosPath)}." : "Needed to play. Use a BIOS dumped from your own PS2; Scanline never downloads it."),
    ];

    /// <summary>Checks a PS2 BIOS dump (its ROMDIR table, with the RESET entry first).</summary>
    public static void CheckBiosFile(string path)
    {
        var len = new FileInfo(path).Length;
        if (len is < 512 << 10 or > 16 << 20)
        {
            throw new InvalidDataException("That isn't a PS2 BIOS. Choose the BIOS file dumped from your PS2 (usually 4 MB, such as SCPH-70012.bin).");
        }
        var head = new byte[Math.Min(len, 1 << 20)];
        using (var f = File.OpenRead(path))
        {
            f.ReadExactly(head);
        }
        var text = Encoding.Latin1.GetString(head);
        var ok = false;
        for (var at = text.IndexOf("RESET\0", StringComparison.Ordinal); at >= 0 && !ok; at = text.IndexOf("RESET\0", at + 1, StringComparison.Ordinal))
        {
            ok = at % 16 == 0 && string.CompareOrdinal(text, at + 16, "ROMDIR", 0, 6) == 0;
        }
        if (!ok)
        {
            throw new InvalidDataException("That isn't a PS2 BIOS. Choose the BIOS file dumped from your PS2 (usually 4 MB, such as SCPH-70012.bin).");
        }
    }

    /// <summary>Copies the player's BIOS into PCSX2's bios folder and selects it. Returns the file name.</summary>
    public string ImportBios(string path)
    {
        CheckBiosFile(path);
        var p = Paths;
        var name = Path.GetFileName(path);
        var target = Path.Combine(p.BiosDir, name);
        Directory.CreateDirectory(p.BiosDir);
        if (Path.GetFullPath(target) != Path.GetFullPath(path))
        {
            File.Copy(path, target, overwrite: true);
        }
        var ini = p.LoadConfigForWrite();
        ini.Set("Filenames", "BIOS", name);
        ini.Set("UI", "SetupWizardIncomplete", "false");
        ini.Save(p.ConfigFile);
        return name;
    }

    // ---- storage ----

    public sealed record GameUsage(GameEntry Game, long Files, long States)
    {
        public long Total => Files + States;
    }

    public GameUsage Usage(GameEntry game) => new(game,
        File.Exists(game.Path) ? new FileInfo(game.Path).Length + CueData(game.Path) : 0,
        Saves.Saves(game).Where(s => s.Relative.StartsWith("sstates/", StringComparison.Ordinal)).Sum(s => s.Bytes));

    private static long CueData(string path) =>
        path.EndsWith(".cue", StringComparison.OrdinalIgnoreCase) && Ps2Disc.CueTrack(path) is { } bin && File.Exists(bin) ? new FileInfo(bin).Length : 0;
}
