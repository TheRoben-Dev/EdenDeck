using System.Text;
using EdenDeck.Backends.Pcsx2;
using EdenDeck.Core;
using EdenDeck.Core.Backends;
using EdenDeck.Core.Launch;
using EdenDeck.Core.Library;
using EdenDeck.Core.Profiles;
using EdenDeck.Core.Settings;

namespace EdenDeck.Backends.Duckstation;

/// <summary>
/// PlayStation through unmodified DuckStation. Discs are .cue/.bin, .iso, .chd and .pbp; each is
/// known by its serial (SLUS-00067), which names its settings, cheats, patches and save states.
/// Launch is <c>duckstation -batch -nogui -fullscreen -- &lt;disc&gt;</c>. DuckStation renders with
/// Vulkan on Linux by default, so frame generation (lsfg-vk, 60 fps rule) and Scanline's layer
/// (CRT, quick menu) work as for the other systems, except inside a Flatpak sandbox. The BIOS is
/// always the player's own dump.
/// </summary>
public sealed class DuckstationBackend : IEmulatorBackend, ILibraryScanner, ILaunchPlanner, ISystemFiles
{
    public const string BackendId = "duckstation";

    private readonly Func<string, string?> _env;

    public DuckstationBackend(AppPaths app, HttpClient? http = null, Func<string, string?>? env = null)
    {
        _env = env ?? Environment.GetEnvironmentVariable;
        DataDir = app.Data;
        Installs = new DuckstationInstallManager(app.EmulatorsDir, http, _env);
        Discs = new DuckstationDiscs(Path.Combine(app.Data, "duckstation", "discs.json"));
        SettingsImpl = new DuckstationSettings(() => Paths);
        Saves = new DuckstationSaves(() => Paths, app.Data, Discs.SaveTitleFor);
        Cheats = new DuckstationCheats(() => Paths, app.Data, http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) });
    }

    public string DataDir { get; }
    public string Id => BackendId;
    public string DisplayName => "DuckStation";
    public string SystemName => "PlayStation";
    public IReadOnlyCollection<string> FileExtensions { get; } = [".cue", ".iso", ".chd", ".pbp"];

    public DuckstationInstallManager Installs { get; }
    public DuckstationDiscs Discs { get; }
    public DuckstationSettings SettingsImpl { get; }
    public DuckstationSaves Saves { get; }
    public DuckstationCheats Cheats { get; }
    public IInstallManager Install => Installs;
    public ILibraryScanner Library => this;
    public ISettingsProvider Settings => SettingsImpl;
    public ILaunchPlanner Launch => this;
    public ISystemFiles? SystemFiles => this;
    public Core.Saves.ISaveData? SaveData => Saves;
    public PresetSet? Presets => DuckstationSettings.Performance;

    public bool IsFlatpak => Installs.Current?.ExecutablePath == DuckstationInstallManager.FlatpakExe;

    /// <summary>DuckStation's folders, inside the Flatpak's sandbox home when that's the DuckStation in use.</summary>
    public DuckstationPaths Paths => new(_env, IsFlatpak);

    private string Home => _env("HOME") ?? "";

    // ---- library ----

    public GameEntry? TryRead(string path)
    {
        var disc = Discs.Identify(path);
        if (disc.Serial == null && !(disc.Source == "chd" && LooksLikePs1Chd(path)))
        {
            return null;  // not a PS1 disc (a PS2 or PS3 disc, another system's CHD) or unreadable
        }
        var serial = disc.Serial ?? "";
        var name = serial.Length > 0 && Ps1Names.Find(serial) is { } known ? known : disc.Title ?? Path.GetFileNameWithoutExtension(path);
        return new GameEntry
        {
            BackendId = Id,
            TitleId = serial,
            Title = Ps2Names.CleanTitle(name),
            Path = path,
            Kind = ContentKind.Base,
            TitleIdSource = disc.Source,
        };
    }

    private static readonly HashSet<string> Ps1Folders = new(StringComparer.OrdinalIgnoreCase)
    {
        "psx", "ps1", "psone", "playstation", "playstation 1", "playstation1", "duckstation", "sony - playstation", "sony playstation",
    };

    /// <summary>A CD .chd whose name isn't a Redump PS1 name: a PS1 game only if a folder on its path says so (psx, ps1…).</summary>
    public static bool LooksLikePs1Chd(string path) =>
        (Path.GetDirectoryName(path) ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries).Any(Ps1Folders.Contains);

    // ---- launch ----

    public LaunchPlan Plan(GameEntry game, GameProfile profile, LaunchOptions options)
    {
        var cur = Installs.Current ?? throw new InvalidOperationException("DuckStation isn't installed yet. Install it under Emulators → PlayStation.");
        if (!BiosInstalled)
        {
            throw new InvalidOperationException("DuckStation needs a PS1 BIOS first. Choose your own BIOS file under Emulators → PlayStation.");
        }
        if (!File.Exists(game.Path))
        {
            throw new FileNotFoundException($"\"{game.Title}\" isn't where it was ({game.Path}). Rescan your library.", game.Path);
        }
        var args = new List<string>();
        string exe;
        var notes = new List<string> { IsFlatpak ? "DuckStation (Flatpak)" : $"DuckStation {cur.Version}" };
        var env = new Dictionary<string, string>();
        var prepare = new List<Action>();
        var vulkanWanted = FrameGenPolicy.Decide(profile.FrameGen, profile.NativeFps).Enabled || profile.Crt > 0;
        if (IsFlatpak)
        {
            exe = "flatpak";
            args.AddRange(["run", DuckstationInstallManager.FlatpakId]);
            if (vulkanWanted)
            {
                notes.Add("warning: frame generation and the CRT effect can't reach into the DuckStation Flatpak. Install the DuckStation AppImage under Emulators → PlayStation to use them.");
            }
        }
        else
        {
            exe = cur.ExecutablePath;
            options.Vulkan?.Apply(profile, options, env, prepare, notes);
            if (vulkanWanted && Renderer(game) is "OpenGL" or "Software")
            {
                notes.Add($"warning: frame generation and the CRT effect need DuckStation's Vulkan renderer; this game is set to {Renderer(game)}.");
            }
        }
        var paths = Paths;
        if (options.Players.Count > 0)
        {
            var sdl = options.Players.Take(Pcsx2Pads.MaxPads).Select(p => p.GamepadIndex).ToList();
            prepare.Add(() => DuckstationPads.Ensure(paths, sdl));
            notes.Add("controllers: " + string.Join(", ", options.Players.Take(Pcsx2Pads.MaxPads).Select((p, i) => $"player {i + 1} = {p} (SDL-{p.GamepadIndex})")));
        }
        else if (options.Gamepads.Any(p => !string.IsNullOrWhiteSpace(p)))
        {
            prepare.Add(() => DuckstationPads.Ensure(paths));
            notes.Add("controller: " + DuckstationPads.Device);
        }
        // DuckStation names the game in its log; Scanline reads that afterwards to learn the serial
        // of a disc it couldn't read (and the title its memory cards are named after).
        var loggingAdded = false;
        prepare.Add(() =>
        {
            var ini = paths.LoadConfigForWrite();
            // Scanline set the BIOS and controller, so DuckStation's setup wizard has nothing left to do.
            ini.Set("Main", "SetupWizardIncomplete", "false");
            if (!string.Equals(ini.Get("Logging", "LogToFile"), "true", StringComparison.OrdinalIgnoreCase))
            {
                ini.Set("Logging", "LogToFile", "true");
                loggingAdded = true;
            }
            ini.Save(paths.ConfigFile);
            if (File.Exists(paths.LogFile))
            {
                File.Delete(paths.LogFile);
            }
        });
        var discs = Discs;
        var disc = game.Path;
        var cleanup = new List<Action>
        {
            () =>
            {
                if (File.Exists(paths.LogFile) && DuckstationDiscs.ParseLog(File.ReadAllText(paths.LogFile)) is { } seen &&
                    (string.IsNullOrEmpty(game.TitleId) || seen.Serial == game.TitleId))
                {
                    discs.Learn(disc, seen.Serial, seen.Title);
                }
            },
            () =>
            {
                if (loggingAdded)
                {
                    var ini = Pcsx2Ini.Load(paths.ConfigFile);
                    ini.Set("Logging", "LogToFile", "false");
                    ini.Save(paths.ConfigFile);
                }
            },
        };
        args.AddRange(["-batch", "-nogui"]);
        args.Add(options.Fullscreen ? "-fullscreen" : "-nofullscreen");
        if (!string.IsNullOrEmpty(game.TitleId) && File.Exists(paths.GameSettingsFile(game.TitleId)))
        {
            notes.Add("settings just for this game (DuckStation game properties)");
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

    /// <summary>The renderer this game runs with (Automatic, Vulkan, OpenGL or Software).</summary>
    private string Renderer(GameEntry game)
    {
        const string id = "GPU/Renderer";
        var global = Settings.ReadGlobal().GetValueOrDefault(id) ?? "Automatic";
        return string.IsNullOrEmpty(game.TitleId) ? global : Settings.ReadPerGame(game.TitleId).GetValueOrDefault(id) ?? global;
    }

    /// <summary>DuckStation's own window (its settings, controller mapping, anything Scanline doesn't cover).</summary>
    public LaunchPlan PlanUi()
    {
        var cur = Installs.Current ?? throw new InvalidOperationException("DuckStation isn't installed yet. Install it under Emulators → PlayStation.");
        var (exe, args) = IsFlatpak ? ("flatpak", new List<string> { "run", DuckstationInstallManager.FlatpakId }) : (cur.ExecutablePath, new List<string>());
        return new LaunchPlan { Executable = exe, Arguments = args, WorkingDirectory = Home.Length > 0 ? Home : "/", Notes = ["DuckStation window"] };
    }

    // ---- BIOS (the player's own dump) ----

    public const int Ps1BiosSize = 512 << 10;
    public const int Ps2BiosSize = 4 << 20;

    /// <summary>The BIOS files in DuckStation's bios folder (it picks the best one for each game's region by itself).</summary>
    public IReadOnlyList<string> BiosFiles
    {
        get
        {
            var dir = Paths.BiosDir;
            return Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir).Where(f => new FileInfo(f).Length is Ps1BiosSize or Ps2BiosSize).Order(StringComparer.Ordinal).ToList()
                : [];
        }
    }

    public bool BiosInstalled => BiosFiles.Count > 0;

    public IReadOnlyList<SystemFileStatus> Status()
    {
        var files = BiosFiles;
        return
        [
            new SystemFileStatus("PS1 BIOS", files.Count > 0,
                files.Count switch
                {
                    0 => "Needed to play. Use a BIOS dumped from your own PS1 (or PS2); Scanline never downloads it.",
                    1 => $"Using {Path.GetFileName(files[0])}.",
                    _ => $"{files.Count} BIOS files; DuckStation picks the one matching each game's region.",
                }),
        ];
    }

    /// <summary>Checks a PS1 BIOS dump (512 KB, Sony's copyright notice in its header), or a PS2 BIOS, which DuckStation can use too.</summary>
    public static void CheckBiosFile(string path)
    {
        var len = new FileInfo(path).Length;
        if (len == Ps2BiosSize)
        {
            Pcsx2Backend.CheckBiosFile(path);
            return;
        }
        var bad = new InvalidDataException("That isn't a PS1 BIOS. Choose the BIOS file dumped from your PS1 (512 KB, such as SCPH-1001.bin).");
        if (len != Ps1BiosSize)
        {
            throw bad;
        }
        var head = new byte[64 << 10];
        using (var f = File.OpenRead(path))
        {
            f.ReadExactly(head);
        }
        if (!Encoding.Latin1.GetString(head).Contains("Sony Computer Entertainment", StringComparison.Ordinal))
        {
            throw bad;
        }
    }

    /// <summary>Copies the player's BIOS into DuckStation's bios folder. Returns the file name.</summary>
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
        ini.Set("Main", "SetupWizardIncomplete", "false");
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
        Saves.Saves(game).Where(s => s.Relative.StartsWith("savestates/", StringComparison.Ordinal)).Sum(s => s.Bytes));

    /// <summary>Every .bin a .cue names (PS1 discs often have one file per track).</summary>
    internal static long CueData(string path)
    {
        if (!path.EndsWith(".cue", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
        var dir = Path.GetDirectoryName(path) ?? "";
        long total = 0;
        foreach (var raw in File.ReadLines(path).Take(500))
        {
            var line = raw.Trim();
            if (!line.StartsWith("FILE ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var q1 = line.IndexOf('"');
            var q2 = q1 >= 0 ? line.IndexOf('"', q1 + 1) : -1;
            var file = q2 > q1 ? line[(q1 + 1)..q2] : line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
            if (file != null && Path.Combine(dir, file) is var f && File.Exists(f))
            {
                total += new FileInfo(f).Length;
            }
        }
        return total;
    }
}
