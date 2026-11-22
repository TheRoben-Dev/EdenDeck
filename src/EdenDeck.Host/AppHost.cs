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

    /// <summary>Directory with VkLayer_EDENDECK_fsr.json; null = auto (next to the app).</summary>
    public string? LayerDirectory { get; init; }

    /// <summary>Global kill switch for EdenDeck's Vulkan layer.</summary>
    public bool LayerKillSwitch { get; init; }

    /// <summary>UI theme id (see the app's theme list); unknown ids fall back to "dark".</summary>
    public string Theme { get; init; } = "dark";

    /// <summary>Turns off UI animations.</summary>
    public bool ReduceMotion { get; init; }

    /// <summary>CRT scanlines over the UI in any theme.</summary>
    public bool Crt { get; init; }

    /// <summary>Check GitHub Releases for a new Eden Deck version at startup.</summary>
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>Last app version whose "What's new" was shown; null on a fresh install.</summary>
    public string? LastSeenVersion { get; init; }
}

/// <summary>
/// Composition root shared by the Godot UI and the headless CLI (`edendeck launch`), so a game
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
    public IReadOnlyList<IEmulatorBackend> Backends { get; }
    public ProfileStore Profiles { get; }
    public LibraryService LibraryService { get; }
    public AppSettings Settings { get; private set; }
    public IReadOnlyList<LibraryGame> Library { get; private set; } = [];
    public AppUpdater Updater { get; }

    public AppHost(AppPaths? paths = null, HttpClient? http = null)
    {
        Paths = paths ?? new AppPaths();
        Directory.CreateDirectory(Paths.Config);
        Directory.CreateDirectory(Paths.Data);
        Eden = new EdenBackend(Paths, http);
        Backends = [Eden];
        Profiles = new ProfileStore(Paths.ProfilesFile);
        LibraryService = new LibraryService(Backends);
        Settings = LoadJson<AppSettings>(Paths.SettingsFile) ?? new AppSettings();
        Library = LoadJson<List<LibraryGame>>(Paths.LibraryCacheFile) ?? [];
        _misnamed = LoadJson<List<MisnamedFile>>(MisnamedCacheFile) ?? [];
        Updater = new AppUpdater();
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
        Library = LibraryService.Scan(Settings.LibraryFolders, ct);
        SaveJson(Paths.LibraryCacheFile, Library);
        _misnamed = LibraryService.LastMisnamed.ToList();
        SaveJson(MisnamedCacheFile, _misnamed);
        return Library;
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
    };

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
        return Backend(game.Base.BackendId).Launch.Plan(game.Base, profile, DefaultLaunchOptions());
    }

    public void MarkPlayed(LibraryGame game) =>
        Profiles.Set(game.GameId, Profiles.Get(game.GameId) with { LastPlayed = DateTimeOffset.Now });

    /// <summary>The layer ships next to the app binary: &lt;app dir&gt;/vulkan/ (tarball), $APPDIR/usr/share/edendeck/vulkan (AppImage) or /app/share/edendeck/vulkan (Flatpak).</summary>
    private static string? AutoLayerDirectory()
    {
        string?[] candidates =
        [
            Environment.GetEnvironmentVariable("APPDIR") is { } appdir ? Path.Combine(appdir, "usr/share/edendeck/vulkan") : null,
            Environment.ProcessPath is { } exe ? Path.Combine(Path.GetDirectoryName(exe)!, "vulkan") : null,
            Path.Combine(AppContext.BaseDirectory, "vulkan"),
            "/app/share/edendeck/vulkan",
        ];
        return candidates.FirstOrDefault(c => c != null && File.Exists(Path.Combine(c, "VkLayer_EDENDECK_fsr.json")));
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
