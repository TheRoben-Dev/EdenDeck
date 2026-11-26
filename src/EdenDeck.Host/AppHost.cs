using System.Text.Json;
using System.Text.Json.Serialization;
using EdenDeck.Backends.Eden;
using EdenDeck.Core;
using EdenDeck.Core.Backends;
using EdenDeck.Core.Launch;
using EdenDeck.Core.Library;
using EdenDeck.Core.Profiles;
using EdenDeck.Host.Updates;

namespace EdenDeck.Host;

/// <summary>EdenDeck's own app settings (not emulator settings).</summary>
public sealed record AppSettings
{
    public List<LibraryFolder> LibraryFolders { get; init; } = new();

    /// <summary>Directory with VkLayer_SCANLINE_overlay.json; null = auto (next to the app).</summary>
    public string? LayerDirectory { get; init; }

    /// <summary>Global kill switch for EdenDeck's Vulkan layer.</summary>
    public bool LayerKillSwitch { get; init; }

    /// <summary>UI theme id (see the app's theme list); unknown ids fall back to "dark".</summary>
    public string Theme { get; init; } = "dark";

    /// <summary>Accent colour per theme id (a swatch id from the app's list); missing = the theme's own.</summary>
    public Dictionary<string, string> Accents { get; init; } = new();

    /// <summary>Turns off UI animations.</summary>
    public bool ReduceMotion { get; init; }

    /// <summary>CRT scanlines over the UI in any theme.</summary>
    public bool Crt { get; init; }

    /// <summary>CRT effect strength when <see cref="Crt"/> is on: 1 subtle, 2 medium, 3 strong.</summary>
    public int CrtLevel { get; init; } = 2;

    /// <summary>The theme's slow animated background behind the menus.</summary>
    public bool AnimatedBackground { get; init; } = true;

    /// <summary>Handheld games (GBA, GB, GBC): a decorative frame fills the side bars.</summary>
    public bool HandheldFrame { get; init; } = true;

    /// <summary>
    /// The accent colour and controller names the app last saw. Games started from a Steam shortcut
    /// run without the app (no theme, no controller list), so they use these instead.
    /// </summary>
    public string? LastAccent { get; init; }

    public List<string> LastGamepads { get; init; } = new();

    /// <summary>Menu sounds, 0 (off) to 100.</summary>
    public int SoundVolume { get; init; } = 50;

    /// <summary>A light rumble on the pad for menu moves, presses and errors.</summary>
    public bool Haptics { get; init; } = true;

    /// <summary>Zip a game's saves before each play (the last few are kept per game).</summary>
    public bool BackupSavesOnLaunch { get; init; } = true;

    /// <summary>Docked console mode when a TV is connected, handheld on the built-in screen (per launch).</summary>
    public bool ConsoleModeFollowsScreen { get; init; } = true;

    /// <summary>Check GitHub Releases for a new Scanline version at startup.</summary>
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>Last app version whose "What's new" was shown; null on a fresh install.</summary>
    public string? LastSeenVersion { get; init; }
}

/// <summary>
/// Composition root shared by the Godot UI and the headless CLI (`scanline launch`), so a game
/// started from a Steam shortcut goes through exactly the same code as one started in the UI.
/// </summary>
public sealed class AppHost
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public AppPaths Paths { get; }
    public EdenBackend Eden { get; }
    public EdenDeck.Backends.Mgba.MgbaBackend Mgba { get; }
    public EdenDeck.Backends.Rpcs3.Rpcs3Backend Rpcs3 { get; }
    public EdenDeck.Backends.Pcsx2.Pcsx2Backend Pcsx2 { get; }
    public EdenDeck.Backends.Duckstation.DuckstationBackend Duckstation { get; }
    public EdenDeck.Backends.Xemu.XemuBackend Xemu { get; }
    public IReadOnlyList<IEmulatorBackend> Backends { get; }
    public ProfileStore Profiles { get; }
    public LibraryService LibraryService { get; }
    public AppSettings Settings { get; private set; }
    public IReadOnlyList<LibraryGame> Library { get; private set; } = [];
    public AppUpdater Updater { get; }

    /// <summary>Add to Steam: per-game shortcuts and one for the app.</summary>
    public SteamService Steam { get; }

    public AppHost(AppPaths? paths = null, HttpClient? http = null)
    {
        if (paths == null)
        {
            AppPaths.MigrateFromEdenDeck();  // Eden Deck's folders -> Scanline's, once
        }
        Paths = paths ?? new AppPaths();
        Directory.CreateDirectory(Paths.Config);
        Directory.CreateDirectory(Paths.Data);
        Eden = new EdenBackend(Paths, http);
        Mgba = new EdenDeck.Backends.Mgba.MgbaBackend(Paths, http);
        Rpcs3 = new EdenDeck.Backends.Rpcs3.Rpcs3Backend(Paths, http);
        Pcsx2 = new EdenDeck.Backends.Pcsx2.Pcsx2Backend(Paths, http);
        Duckstation = new EdenDeck.Backends.Duckstation.DuckstationBackend(Paths, http);
        Xemu = new EdenDeck.Backends.Xemu.XemuBackend(Paths, http);
        Backends = [Eden, Mgba, Rpcs3, Pcsx2, Duckstation, Xemu];
        Profiles = new ProfileStore(Paths.ProfilesFile);
        LibraryService = new LibraryService(Backends);
        Settings = LoadJson<AppSettings>(Paths.SettingsFile) ?? new AppSettings();
        if ((Settings.LayerDirectory ?? AutoLayerDirectory()) is { } vk)
        {
            Eden.BundledLsfgDir = Path.Combine(vk, "lsfg-vk");
        }
        Library = LoadJson<List<LibraryGame>>(Paths.LibraryCacheFile) ?? [];
        _misnamed = LoadJson<List<MisnamedFile>>(MisnamedCacheFile) ?? [];
        Updater = new AppUpdater();
        Steam = new SteamService(Paths,
            Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.ProcessPath is { } exe ? Path.GetDirectoryName(exe) : null);
    }

    public IEmulatorBackend Backend(string id) =>
        Backends.FirstOrDefault(b => b.Id == id) ?? throw new KeyNotFoundException($"No backend '{id}'");

    public void SaveSettings(AppSettings settings)
    {
        Settings = settings;
        SaveJson(Paths.SettingsFile, settings);
    }

    public IReadOnlyList<LibraryGame> Rescan(CancellationToken ct = default)
    {
        var own = Backends.SelectMany(b => b.Library.OwnFolders).Where(Directory.Exists).Select(f => new LibraryFolder(f));
        Library = LibraryService.Scan(Settings.LibraryFolders.Concat(own), ct);
        CarryOverProfiles();
        SaveJson(Paths.LibraryCacheFile, Library);
        _misnamed = LibraryService.LastMisnamed.ToList();
        SaveJson(MisnamedCacheFile, _misnamed);
        return Library;
    }

    /// <summary>
    /// A game first seen without a title id (named by its file) and identified later, such as a PS2
    /// or PS1 .chd once its emulator has run it, gets a new id; its card settings move along with it.
    /// </summary>
    private void CarryOverProfiles()
    {
        foreach (var g in Library.Where(g => !string.IsNullOrEmpty(g.Base.TitleId)))
        {
            var byFile = GameIds.Make(g.Base.BackendId, Path.GetFileName(g.Base.Path));
            if (byFile != g.GameId && Profiles.All.TryGetValue(byFile, out var old) && !Profiles.All.ContainsKey(g.GameId))
            {
                Profiles.Set(g.GameId, old);
            }
        }
    }

    private List<MisnamedFile> _misnamed;
    private string MisnamedCacheFile => Path.Combine(Path.GetDirectoryName(Paths.LibraryCacheFile)!, "misnamed.json");

    /// <summary>Game files from the last scan that need a name fix (still there and not yet renamed).</summary>
    public IReadOnlyList<MisnamedFile> Misnamed => _misnamed.Where(m => File.Exists(m.Path) && !File.Exists(m.SuggestedPath)).ToList();

    /// <summary>Finds a game by id ("eden:0100...") or by file path.</summary>
    public LibraryGame? FindGame(string idOrPath)
    {
        var byId = Library.FirstOrDefault(g => string.Equals(g.GameId, idOrPath, StringComparison.OrdinalIgnoreCase));
        if (byId != null)
        {
            return byId;
        }
        // An id from before the game's title id was known ("pcsx2:Game.chd"), e.g. in a Steam shortcut.
        if (GameIds.TryParse(idOrPath, out var backendId, out var fileName) &&
            Library.FirstOrDefault(g => g.Base.BackendId == backendId && Path.GetFileName(g.Base.Path) == fileName) is { } byFile)
        {
            return byFile;
        }
        if (File.Exists(idOrPath))
        {
            var full = Path.GetFullPath(idOrPath);
            var cached = Library.FirstOrDefault(g => g.Base.Path == full);
            if (cached != null)
            {
                return cached;
            }
            foreach (var b in Backends.Where(b => b.FileExtensions.Contains(Path.GetExtension(full).ToLowerInvariant())))
            {
                if (b.Library.TryRead(full) is { } entry)
                {
                    return new LibraryGame(entry, [], []);
                }
            }
        }
        return null;
    }

    public LaunchOptions DefaultLaunchOptions() => new()
    {
        LayerDirectory = Settings.LayerDirectory ?? AutoLayerDirectory(),
        LayerStatsPath = Path.Combine(Paths.Runtime, "stats.json"),
        LayerKillSwitch = Settings.LayerKillSwitch,
        Fullscreen = true,
        CrtMotion = !Settings.ReduceMotion,
        Docked = Settings.ConsoleModeFollowsScreen ? ScreenDetect.External() : null,
        Vulkan = new EdenDeck.Backends.Eden.EdenVulkanExtras(Eden, Paths.Runtime),
        HandheldFrame = Settings.HandheldFrame,
        Accent = RememberedAccent(),
        Gamepads = RememberedGamepads(),
    };

    /// <summary>The app's accent, remembered for launches without the app (Steam shortcuts).</summary>
    private string? RememberedAccent()
    {
        if (Accent is not { } accent)
        {
            return Settings.LastAccent;
        }
        if (accent != Settings.LastAccent)
        {
            SaveSettings(Settings with { LastAccent = accent });
        }
        return accent;
    }

    /// <summary>The connected controllers, or the ones the app saw last when it isn't running.</summary>
    private IReadOnlyList<string> RememberedGamepads()
    {
        if (Gamepads == null)
        {
            return Settings.LastGamepads;  // no app: a Steam shortcut, where Steam Input's pad is the one the app saw
        }
        var pads = Gamepads();
        if (pads.Count > 0 && !pads.SequenceEqual(Settings.LastGamepads))
        {
            SaveSettings(Settings with { LastGamepads = pads.ToList() });
        }
        return pads;
    }

    /// <summary>The connected controllers' SDL names (first = in use), supplied by the app.</summary>
    public Func<IReadOnlyList<string>>? Gamepads { get; set; }

    /// <summary>The app's current accent colour (#rrggbb), set by the app; used for in-game decoration.</summary>
    public string? Accent { get; set; }

    public LaunchPlan PlanLaunch(LibraryGame game)
    {
        var profile = Profiles.Get(game.GameId);
        if (game.Base.BackendId == Eden.Id)
        {
            // The base and (if loaded) its newest update decide which keys are needed.
            var updates = profile.LoadAddOns ? game.Updates.Take(1).Select(u => u.Path) : [];
            Eden.CheckKeys(game.Base.Title, [game.Base.Path, .. updates], profile.KeySet);
            Eden.PrepareAddOns(game, profile.LoadAddOns, Settings.LibraryFolders.Select(f => f.Path));
        }
        if (Settings.BackupSavesOnLaunch && !string.IsNullOrEmpty(game.Base.TitleId) && Backend(game.Base.BackendId).SaveData is { } saves)
        {
            try
            {
                saves.Backup(game.Base, automatic: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                AppLog.Warn($"saves: automatic backup failed: {e.Message}");  // never blocks playing
            }
        }
        return Backend(game.Base.BackendId).Launch.Plan(game.Base, profile, DefaultLaunchOptions());
    }

    public void MarkPlayed(LibraryGame game) =>
        Profiles.Set(game.GameId, Profiles.Get(game.GameId) with { LastPlayed = DateTimeOffset.Now });

    /// <summary>The layer ships next to the app binary: &lt;app dir&gt;/vulkan/ (tarball), $APPDIR/usr/share/scanline/vulkan (AppImage) or /app/share/scanline/vulkan (Flatpak).</summary>
    private static string? AutoLayerDirectory()
    {
        string?[] candidates =
        [
            Environment.GetEnvironmentVariable("APPDIR") is { } appdir ? Path.Combine(appdir, "usr/share/scanline/vulkan") : null,
            Environment.ProcessPath is { } exe ? Path.Combine(Path.GetDirectoryName(exe)!, "vulkan") : null,
            Path.Combine(AppContext.BaseDirectory, "vulkan"),
            "/app/share/scanline/vulkan",
        ];
        return candidates.FirstOrDefault(c => c != null && File.Exists(Path.Combine(c, "VkLayer_SCANLINE_overlay.json")));
    }

    private static T? LoadJson<T>(string path) where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void SaveJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
