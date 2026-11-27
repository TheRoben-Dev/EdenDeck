using EdenDeck.Core;
using EdenDeck.Core.Backends;
using EdenDeck.Core.Launch;
using EdenDeck.Core.Library;
using EdenDeck.Core.Profiles;
using EdenDeck.Core.Settings;

namespace EdenDeck.Backends.Rpcs3;

/// <summary>
/// PlayStation 3 through unmodified RPCS3. Games are found by their PARAM.SFO (disc folders, PSN
/// folders, RPCS3's own dev_hdd0/game) or inside a disc .iso. Launch is
/// <c>rpcs3 --no-gui --fullscreen &lt;EBOOT.BIN|iso&gt;</c>; per-game settings are RPCS3's own
/// custom configs. RPCS3 renders with Vulkan, so frame generation (lsfg-vk, 60 fps rule) and
/// Scanline's layer (CRT, quick menu) work as for Switch games, except inside the Flatpak sandbox.
/// The PS3 firmware is always the player's own PS3UPDAT.PUP, installed by RPCS3 itself.
/// </summary>
public sealed class Rpcs3Backend : IEmulatorBackend, ILibraryScanner, ILaunchPlanner, ISystemFiles
{
    public const string BackendId = "rpcs3";

    private readonly Func<string, string?> _env;

    public Rpcs3Backend(AppPaths app, HttpClient? http = null, Func<string, string?>? env = null)
    {
        _env = env ?? Environment.GetEnvironmentVariable;
        DataDir = app.Data;
        Installs = new Rpcs3InstallManager(app.EmulatorsDir, http, _env);
        SettingsImpl = new Rpcs3Settings(() => Paths);
        Saves = new Rpcs3Saves(() => Paths, app.Data);
        Patches = new Rpcs3Patches(() => Paths, http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) });
    }

    public Rpcs3Patches Patches { get; }

    public string DataDir { get; }
    public string Id => BackendId;
    public string DisplayName => "RPCS3";
    public string SystemName => "PlayStation 3";
    public IReadOnlyCollection<string> FileExtensions { get; } = [".sfo", ".iso"];

    public Rpcs3InstallManager Installs { get; }
    public Rpcs3Settings SettingsImpl { get; }
    public Rpcs3Saves Saves { get; }
    public IInstallManager Install => Installs;
    public ILibraryScanner Library => this;
    public ISettingsProvider Settings => SettingsImpl;
    public ILaunchPlanner Launch => this;
    public ISystemFiles? SystemFiles => this;
    public Core.Saves.ISaveData? SaveData => Saves;
    public PresetSet? Presets => Rpcs3Settings.Performance;

    public bool IsFlatpak => Installs.Current?.ExecutablePath == Rpcs3InstallManager.FlatpakExe;

    /// <summary>RPCS3's folders, inside the Flatpak's sandbox home when that's the RPCS3 in use.</summary>
    public Rpcs3Paths Paths => new(_env, IsFlatpak);

    private string Home => _env("HOME") ?? "";

    // ---- library ----

    public IEnumerable<string> OwnFolders => [Paths.GamesDir];

    public GameEntry? TryRead(string path)
    {
        if (path.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
        {
            if (Ps3Sfo.ReadIso(path) is not { } disc)
            {
                return null;  // not a PS3 disc (or not readable): other systems use .iso too
            }
            return Entry(disc, path, "iso-param-sfo");
        }
        if (!Path.GetFileName(path).Equals("PARAM.SFO", StringComparison.OrdinalIgnoreCase) || Ps3Sfo.ReadFile(path) is not { } game || !Ps3Sfo.IsGame(game.Category))
        {
            return null;
        }
        var dir = Path.GetDirectoryName(path)!;
        var root = Path.GetFileName(dir).Equals("PS3_GAME", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(dir)! : dir;
        return File.Exists(Eboot(root)) ? Entry(game, root, "param-sfo") : null;
    }

    private GameEntry Entry(Ps3Game game, string path, string source) => new()
    {
        BackendId = Id,
        TitleId = game.TitleId,
        Title = CleanTitle(game.Title),
        Path = path,
        Kind = ContentKind.Base,
        Version = game.Version,
        TitleIdSource = source,
    };

    public static string CleanTitle(string title) =>
        string.Join(' ', title.Replace("™", "").Replace("®", "").Replace("©", "").Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The executable inside a game folder (disc layout or installed/PSN layout).</summary>
    public static string Eboot(string root)
    {
        var disc = Path.Combine(root, "PS3_GAME", "USRDIR", "EBOOT.BIN");
        return File.Exists(disc) ? disc : Path.Combine(root, "USRDIR", "EBOOT.BIN");
    }

    /// <summary>What RPCS3 is given to boot: the .iso itself, or the game's EBOOT.BIN.</summary>
    public static string BootTarget(GameEntry game) => File.Exists(game.Path) ? game.Path : Eboot(game.Path);

    /// <summary>ICON0.PNG from the game folder, or pulled out of the .iso once and cached.</summary>
    public string? IconFor(GameEntry game)
    {
        if (game.BackendId != Id)
        {
            return null;
        }
        if (Directory.Exists(game.Path))
        {
            return new[] { Path.Combine(game.Path, "PS3_GAME", "ICON0.PNG"), Path.Combine(game.Path, "ICON0.PNG") }.FirstOrDefault(File.Exists);
        }
        if (string.IsNullOrEmpty(game.TitleId) || !File.Exists(game.Path))
        {
            return null;
        }
        var cached = Path.Combine(DataDir, "rpcs3", "icons", game.TitleId + ".png");
        if (File.Exists(cached))
        {
            return cached;
        }
        try
        {
            using var f = File.OpenRead(game.Path);
            if (Iso9660.ReadFile(f, ["PS3_GAME", "ICON0.PNG"]) is not { } png)
            {
                return null;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
            File.WriteAllBytes(cached, png);
            return cached;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            return null;
        }
    }

    // ---- launch ----

    /// <summary>What Scanline last wrote as player 1's pad (see <see cref="Rpcs3Pads"/>).</summary>
    public string PadStampFile => Path.Combine(DataDir, "rpcs3", "pad-written.txt");

    public LaunchPlan Plan(GameEntry game, GameProfile profile, LaunchOptions options)
    {
        var cur = Installs.Current ?? throw new InvalidOperationException("RPCS3 isn't installed yet. Install it under Emulators → PlayStation 3.");
        if (!File.Exists(Paths.FirmwareMarker))
        {
            throw new InvalidOperationException("RPCS3 needs the PS3 system software first. Install your own PS3UPDAT.PUP under Emulators → PlayStation 3.");
        }
        var target = BootTarget(game);
        if (!File.Exists(target))
        {
            throw new FileNotFoundException($"\"{game.Title}\" isn't where it was ({target}). Rescan your library.", target);
        }
        var args = new List<string>();
        string exe;
        var notes = new List<string> { $"RPCS3 {cur.Version}" };
        var env = new Dictionary<string, string>();
        var prepare = new List<Action>();
        if (IsFlatpak)
        {
            exe = "flatpak";
            args.AddRange(["run", Rpcs3InstallManager.FlatpakId]);
            if (FrameGenPolicy.Decide(profile.FrameGen, profile.NativeFps).Enabled || profile.Crt > 0)
            {
                notes.Add("warning: frame generation and the CRT effect can't reach into the RPCS3 Flatpak. Install the RPCS3 AppImage under Emulators → PlayStation 3 to use them.");
            }
        }
        else
        {
            exe = cur.ExecutablePath;
            options.Vulkan?.Apply(profile, options, env, prepare, notes);
        }
        var pads = options.Gamepads;
        var paths = Paths;
        if (options.Players.Count > 0)
        {
            var devices = Rpcs3Pads.DevicesFor(options.Players);
            var stamp = PadStampFile;
            prepare.Add(() => Rpcs3Pads.EnsurePlayers(paths, devices, stamp));
            notes.Add("controllers: " + string.Join(", ", devices.Select((d, i) => $"player {i + 1} = {d}")));
        }
        else if (Rpcs3Pads.DeviceFor(pads) is { } pad)
        {
            prepare.Add(() => Rpcs3Pads.Ensure(paths, pads, PadStampFile));
            notes.Add($"controller: {pad}");
        }
        args.Add("--no-gui");
        if (options.Fullscreen)
        {
            args.Add("--fullscreen");
        }
        if (File.Exists(Paths.CustomConfigFile(game.TitleId)))
        {
            notes.Add("settings just for this game (RPCS3 custom config)");
        }
        args.Add(target);
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

    // ---- firmware (the player's own PS3UPDAT.PUP) ----

    public bool FirmwareInstalled => File.Exists(Paths.FirmwareMarker);

    public IReadOnlyList<SystemFileStatus> Status() =>
    [
        new SystemFileStatus("PS3 system software", FirmwareInstalled,
            FirmwareInstalled ? "Installed in RPCS3." : "Needed to play. Use your own PS3UPDAT.PUP (the official PS3 system update file); Scanline never downloads it."),
    ];

    /// <summary>Checks a PS3UPDAT.PUP (the "SCEUF" package header) before RPCS3 is asked to install it.</summary>
    public static void CheckFirmwareFile(string path)
    {
        var head = new byte[5];
        using (var f = File.OpenRead(path))
        {
            if (f.Length < 1 << 20 || f.Read(head, 0, 5) != 5)
            {
                throw new InvalidDataException("That isn't a PS3 system update file. Choose your PS3UPDAT.PUP.");
            }
        }
        if (System.Text.Encoding.ASCII.GetString(head) != "SCEUF")
        {
            throw new InvalidDataException("That isn't a PS3 system update file. Choose your PS3UPDAT.PUP.");
        }
    }

    /// <summary>RPCS3 installs the firmware itself: <c>rpcs3 --installfw &lt;PUP&gt;</c>.</summary>
    public (string Exe, IReadOnlyList<string> Args) FirmwareInstallCommand(string pup)
    {
        CheckFirmwareFile(pup);
        return Command(["--installfw", pup]);
    }

    public LaunchPlan PlanFirmwareInstall(string pup)
    {
        var (exe, args) = FirmwareInstallCommand(pup);
        return new LaunchPlan { Executable = exe, Arguments = args, WorkingDirectory = Home.Length > 0 ? Home : "/", Notes = ["RPCS3: install system software"] };
    }

    /// <summary>RPCS3's own window (controller setup, anything Scanline doesn't cover).</summary>
    public LaunchPlan PlanUi()
    {
        var (exe, args) = Command([]);
        return new LaunchPlan { Executable = exe, Arguments = args, WorkingDirectory = Home.Length > 0 ? Home : "/", Notes = ["RPCS3 window"] };
    }

    private (string Exe, IReadOnlyList<string> Args) Command(IReadOnlyList<string> args)
    {
        var cur = Installs.Current ?? throw new InvalidOperationException("RPCS3 isn't installed yet. Install it under Emulators → PlayStation 3.");
        return IsFlatpak ? ("flatpak", ["run", Rpcs3InstallManager.FlatpakId, .. args]) : (cur.ExecutablePath, args);
    }

    // ---- storage ----

    public sealed record GameUsage(GameEntry Game, long Files, long Saves, long Cache)
    {
        public long Total => Files + Saves + Cache;
    }

    public GameUsage Usage(GameEntry game) => new(game,
        File.Exists(game.Path) ? new FileInfo(game.Path).Length : DirBytes(game.Path),
        Saves.Saves(game).Sum(s => s.Bytes),
        string.IsNullOrEmpty(game.TitleId) ? 0 : DirBytes(Paths.GameCacheDir(game.TitleId)));

    /// <summary>Deletes the game's compiled PPU/SPU/shader cache; RPCS3 rebuilds it on the next start.</summary>
    public void ClearCache(GameEntry game)
    {
        var dir = Paths.GameCacheDir(game.TitleId);
        if (!string.IsNullOrEmpty(game.TitleId) && Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    public static long DirBytes(string dir)
    {
        try
        {
            return Directory.Exists(dir)
                ? new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).Sum(f => f.Length)
                : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
