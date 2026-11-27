using EdenDeck.Core.Settings;
using EdenDeck.Core;
using EdenDeck.Core.Backends;
using EdenDeck.Core.Launch;
using EdenDeck.Core.Library;
using EdenDeck.Core.Profiles;

namespace EdenDeck.Backends.Mgba;

/// <summary>
/// Game Boy Advance (and Game Boy / Game Boy Color) through unmodified mGBA. Integration is the
/// command line (<c>-f</c> fullscreen, <c>-C key=value</c> per-game overrides) and mGBA's own
/// config.ini. The GBA BIOS is optional and always the player's own file.
/// </summary>
public sealed class MgbaBackend : IEmulatorBackend, ILibraryScanner, ILaunchPlanner, ISystemFiles
{
    public const string BackendId = "mgba";

    private readonly Func<string, string?> _env;

    public MgbaBackend(AppPaths app, HttpClient? http = null, Func<string, string?>? env = null)
    {
        _env = env ?? Environment.GetEnvironmentVariable;
        DataDir = app.Data;
        Installs = new MgbaInstallManager(app.EmulatorsDir, http, _env);
        SettingsImpl = new MgbaSettings(() => ConfigFile, Path.Combine(app.Data, "mgba", "overrides.json"));
        Saves = new MgbaSaves(this, app.Data);
        Mods = new MgbaMods(app.Data);
        CheatDatabase = new MgbaCheatDatabase(http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
    }

    public string DataDir { get; }
    public MgbaMods Mods { get; }
    public MgbaCheatDatabase CheatDatabase { get; }

    public MgbaSaves Saves { get; }
    public Core.Saves.ISaveData? SaveData => Saves;
    public PresetSet? Presets => MgbaSettings.Looks;

    public string Id => BackendId;
    public string DisplayName => "mGBA";
    public string SystemName => "Game Boy Advance";
    public IReadOnlyCollection<string> FileExtensions { get; } = [".gba", ".gb", ".gbc", ".zip"];

    public MgbaInstallManager Installs { get; }
    public MgbaSettings SettingsImpl { get; }
    public IInstallManager Install => Installs;
    public ILibraryScanner Library => this;
    public ISettingsProvider Settings => SettingsImpl;
    public ILaunchPlanner Launch => this;
    public ISystemFiles? SystemFiles => this;

    private string Home => _env("HOME") ?? "";

    /// <summary>mGBA's config.ini: inside the Flatpak's sandbox home when that's the mGBA in use.</summary>
    public string ConfigFile
    {
        get
        {
            if (Installs.Current?.ExecutablePath == MgbaInstallManager.FlatpakExe)
            {
                return Path.Combine(Home, ".var", "app", MgbaInstallManager.FlatpakId, "config", "mgba", "config.ini");
            }
            var xdg = _env("XDG_CONFIG_HOME");
            return Path.Combine(string.IsNullOrEmpty(xdg) ? Path.Combine(Home, ".config") : xdg, "mgba", "config.ini");
        }
    }

    // ---- library ----

    public GameEntry? TryRead(string path)
    {
        var header = MgbaRom.Read(path);
        if (header == null)
        {
            return null;
        }
        return new GameEntry
        {
            BackendId = Id,
            TitleId = header.Id,
            Title = MgbaRom.CleanTitle(MgbaNames.Find(header.Crc, header.GameCode) is { } known ? known + ".gba" : Path.GetFileName(path)),
            Path = path,
            Kind = ContentKind.Base,
            TitleIdSource = header.GameCode != null ? "gba-header" : "header-title",
        };
    }

    /// <summary>"Game Boy Advance", "Game Boy Color" or "Game Boy", from the id.</summary>
    public static string SystemOf(string titleId) =>
        titleId.StartsWith("GBC_", StringComparison.Ordinal) ? "Game Boy Color"
        : titleId.StartsWith("GB_", StringComparison.Ordinal) ? "Game Boy" : "Game Boy Advance";

    // ---- launch ----

    /// <summary>What Scanline last wrote to mGBA's gamepad section (see <see cref="MgbaPad"/>).</summary>
    public string PadStampFile => Path.Combine(DataDir, "mgba", "pad-written.txt");

    public LaunchPlan Plan(GameEntry game, GameProfile profile, LaunchOptions options)
    {
        var cur = Installs.Current ?? throw new InvalidOperationException("mGBA isn't installed yet. Install it under Emulators → Game Boy Advance.");
        var args = new List<string>();
        string exe;
        if (cur.ExecutablePath == MgbaInstallManager.FlatpakExe)
        {
            exe = "flatpak";
            args.AddRange(["run", MgbaInstallManager.FlatpakId]);
        }
        else
        {
            exe = cur.ExecutablePath;
        }
        if (options.Fullscreen)
        {
            args.Add("-f");
        }
        var notes = new List<string> { $"mGBA {cur.Version}" };
        var overrides = SettingsImpl.OverrideArgs(game.TitleId).ToList();
        args.AddRange(overrides);
        if (overrides.Count > 0)
        {
            notes.Add($"{overrides.Count / 2} setting(s) just for this game");
        }
        if (Mods.ActivePatch(game.TitleId) is { } patch)
        {
            args.AddRange(["-p", patch]);
            notes.Add($"patch: {Path.GetFileName(patch)}");
        }
        var configFile = ConfigFile;
        var device = options.Players.Count > 0 ? options.Players[0].JoystickName : null;
        if (device != null)
        {
            notes.Add($"controller: {options.Players[0]}");
        }
        var stampFile = PadStampFile;
        var prepare = new List<Action> { () => MgbaPad.Ensure(configFile, stampFile, device) };
        if (Mods.LaunchCheatFile(game.TitleId) is { } cheats)
        {
            args.AddRange(["-c", cheats]);
            prepare.Add(() => Mods.WriteLaunchCheats(game.TitleId));
            notes.Add($"cheats: {Mods.Cheats(game.TitleId).Count(c => c.Enabled)} on");
        }
        var look = new MgbaShader.Look(profile.Crt, profile.Frame ?? options.HandheldFrame, options.Accent);
        if (MgbaShader.DirFor(DataDir, look) is { } shader)
        {
            args.AddRange(["-C", "shader=" + shader]);
            prepare.Add(() => MgbaShader.Write(DataDir, look));
            if (look.Frame)
            {
                // The shader places the picture itself, so mGBA mustn't letterbox it.
                args.AddRange(["-C", "lockAspectRatio=0", "-C", "lockIntegerScaling=0"]);
                notes.Add("handheld frame");
            }
            if (MgbaShader.CrtLook(profile.Crt) != null)
            {
                notes.Add("CRT effect");
            }
        }
        args.Add(game.Path);
        return new LaunchPlan
        {
            Executable = exe,
            Arguments = args,
            WorkingDirectory = Path.GetDirectoryName(game.Path) ?? Home,
            Notes = notes,
            Prepare = prepare,
        };
    }

    // ---- BIOS (optional, the player's own) ----

    public string? BiosPath => SettingsImpl.GetRaw("gba.bios") is { Length: > 0 } p ? p : null;

    public IReadOnlyList<SystemFileStatus> Status()
    {
        var bios = BiosPath;
        var present = bios != null && File.Exists(bios);
        return
        [
            new SystemFileStatus("gba_bios.bin", present,
                present ? $"Your GBA BIOS: {bios}" : bios != null ? $"The BIOS file set in mGBA is missing: {bios}" : "Optional. mGBA's built-in replacement runs almost every game."),
        ];
    }

    /// <summary>Points mGBA at the player's own GBA BIOS (16 KB). Checks the size; never copies or downloads anything.</summary>
    public void SetBios(string? path)
    {
        if (path != null)
        {
            var len = new FileInfo(path).Length;
            if (len != 16384)
            {
                throw new InvalidDataException($"That file is {len} bytes. A GBA BIOS is exactly 16384 bytes (16 KB).");
            }
        }
        SettingsImpl.SetRaw("gba.bios", path);
    }

    // ---- storage ----

    public sealed record GameUsage(GameEntry Game, long Rom, long Saves, long Patches)
    {
        public long Total => Rom + Saves + Patches;
    }

    /// <summary>What one game takes up: the ROM, its save and save states, and its patches.</summary>
    public GameUsage Usage(GameEntry game)
    {
        static long Size(string f) => File.Exists(f) ? new FileInfo(f).Length : 0;
        var tid = game.TitleId ?? "";
        return new GameUsage(game, Size(game.Path),
            tid == "" ? 0 : Saves.Saves(game).Sum(s => s.Bytes),
            tid == "" ? 0 : Mods.Patches(tid).Sum(Size));
    }
}
