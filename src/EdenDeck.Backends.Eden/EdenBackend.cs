using EdenDeck.Backends.Eden.Config;
using EdenDeck.Backends.Eden.Install;
using EdenDeck.Backends.Eden.Library;
using EdenDeck.Backends.Eden.Mods;
using EdenDeck.Backends.Eden.SystemFiles;
using EdenDeck.Core;
using EdenDeck.Core.Backends;

namespace EdenDeck.Backends.Eden;

/// <summary>Eden (Nintendo Switch). All Eden-specific knowledge lives in this assembly.</summary>
public sealed class EdenBackend : IEmulatorBackend
{
    public const string BackendId = "eden";

    private readonly AppPaths _app;
    private EdenPaths? _paths;

    public EdenBackend(AppPaths app, HttpClient? http = null)
    {
        _app = app;
        var install = new EdenInstallManager(app.EmulatorsDir, http);
        Install = install;
        KeySets = new KeySetStore(app.KeySetsDir);
        EdenSettings = new EdenSettings(() => Paths, () => Schema);
        Library = new EdenLibraryScanner(LoadHeaderKey);
        Launch = new EdenLaunchPlanner(this, app.Runtime);
        SystemFilesImpl = new EdenSystemFiles(() => Paths, Path.Combine(app.Data, "backups"));
        Cheats = new CheatDatabase(http ?? CheatHttp());
    }

    private static HttpClient CheatHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Scanline");
        return h;
    }

    /// <summary>The community cheat database, fetched one game at a time when the player asks.</summary>
    public CheatDatabase Cheats { get; }

    public IReadOnlyList<ModInfo> Mods(string titleId) => EdenMods.List(Paths, titleId);

    public void SetModEnabled(string titleId, string name, bool enabled) => EdenMods.SetEnabled(Paths.GlobalConfigFile, titleId, name, enabled);

    public void RemoveMod(string titleId, string name) => EdenMods.Remove(Paths, titleId, name);

    /// <summary>Installs a mod from a .zip or a folder; returns the installed names.</summary>
    public IReadOnlyList<string> InstallMod(string titleId, string path, bool replace = false) =>
        Directory.Exists(path) ? EdenMods.InstallFolder(Paths, titleId, path, replace) : EdenMods.InstallZip(Paths, titleId, path, replace);

    /// <summary>The game version Eden will run: the newest update from the library if it's loaded, else the base game (0).</summary>
    public static uint GameVersion(Core.Library.LibraryGame game, bool loadAddOns) =>
        loadAddOns && game.Updates.Count > 0 && uint.TryParse(game.Updates[0].Version, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

    public string Id => BackendId;
    public string DisplayName => "Eden";
    public string SystemName => "Nintendo Switch";
    public IReadOnlyCollection<string> FileExtensions => EdenLibraryScanner.Extensions;

    public IInstallManager Install { get; }
    public ILibraryScanner Library { get; }
    public ISettingsProvider Settings => EdenSettings;
    public ILaunchPlanner Launch { get; }
    public ISystemFiles? SystemFiles => SystemFilesImpl;

    public EdenSettings EdenSettings { get; }
    public EdenSystemFiles SystemFilesImpl { get; }
    public KeySetStore KeySets { get; }

    /// <summary>Eden's dirs as the launched process will see them (neutral cwd, not portable).</summary>
    public EdenPaths Paths => _paths ??= Env != null ? EdenPaths.Resolve(Path.Combine(_app.Runtime, "cwd"), Env) : EdenPaths.Resolve(Path.Combine(_app.Runtime, "cwd"));

    /// <summary>Environment lookup for Eden paths and $HOME (tests use a throwaway home).</summary>
    public Func<string, string?>? Env { get; set; }

    /// <summary>Schema snapshot matching the installed Eden (nearest one for unknown builds).</summary>
    public EdenSchema Schema
    {
        get
        {
            var cur = Install.Current;
            return EdenSchema.Select(cur?.Channel ?? "stable", cur?.Version);
        }
    }

    public void ReloadPaths() => _paths = null;

    /// <summary>Home dir for finding the user's Steam install (tests override it).</summary>
    public string Home => (Env ?? Environment.GetEnvironmentVariable)("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Copies the user's own Lossless.dll from Steam into Eden's lossless/ dir when needed.</summary>
    public LosslessDll.Status EnsureLosslessDll() => LosslessDll.Ensure(Paths.LosslessDir, Home);

    /// <summary>The Lossless.dll frame gen should load: the Steam copy, else the one in Eden's lossless/ dir.</summary>
    public string? LosslessDllPath()
    {
        var steam = LosslessDll.FindInSteam(Home);
        if (steam != null)
        {
            return steam;
        }
        var local = Path.Combine(Paths.LosslessDir, LosslessDll.FileName);
        return LosslessDll.IsInstalled(Paths.LosslessDir) ? local : null;
    }

    /// <summary>Installed lsfg-vk layer, if any.</summary>
    /// <summary>Where the lsfg-vk bundled with Scanline lives (vulkan/lsfg-vk next to the app), if any.</summary>
    public string? BundledLsfgDir { get; set; }

    public LsfgVk.Install FindLsfgVk() => LsfgVk.Find(Env ?? Environment.GetEnvironmentVariable, BundledLsfgDir);

    /// <summary>Read-only check for the game page: is frame gen going to find Lossless.dll?</summary>
    public bool LosslessDllAvailable() => LosslessDll.IsInstalled(Paths.LosslessDir) || LosslessDll.FindInSteam(Home) != null;

    /// <summary>Pre-launch check that the keys the game will run with are new enough (see <see cref="KeyRequirements"/>).</summary>
    public void CheckKeys(string title, IEnumerable<string> gameFiles, string? keySetId)
    {
        var set = keySetId != null ? KeySets.Get(keySetId) : null;
        var prod = set != null ? Path.Combine(set.Directory, "prod.keys") : Path.Combine(Paths.KeysDir, "prod.keys");
        KeyRequirements.Check(title, gameFiles, prod, set != null ? $"The keys in set \"{set.Label}\"" : "Your keys");
    }

    /// <summary>Lets Eden load updates/DLC straight from the library folders (no NAND install).</summary>
    /// <summary>Folder of links to the launched game's update/DLC, listed in Eden's external content dirs.</summary>
    public string AddOnStagingDir => Path.Combine(_app.Data, "addons");

    /// <summary>Stages this game's update/DLC for Eden (or nothing, if turned off for the game).</summary>
    public bool PrepareAddOns(Core.Library.LibraryGame game, bool enabled, IEnumerable<string> libraryFolders) =>
        EdenAddOns.PrepareLaunch(Paths.GlobalConfigFile, AddOnStagingDir, game, enabled, libraryFolders);

    public IReadOnlyList<AddOnStatus> AddOnStatus(Core.Library.LibraryGame game, bool enabled) =>
        EdenAddOns.Status(Paths.GlobalConfigFile, game, enabled, AddOnStagingDir);

    /// <summary>Gives player 1 a controller mapping if Eden still has only the keyboard mapped.</summary>
    public bool EnsureControllerMapping(PadInfo pad) => EdenControllerDefaults.EnsurePlayer1(Paths.GlobalConfigFile, pad);

    /// <summary>Player 1's button mapping (games only), one pad input per Switch button.</summary>
    public Dictionary<string, string> ButtonMap() => EdenButtonMap.Read(Paths.GlobalConfigFile);

    public void SetButton(PadInfo pad, string buttonKey, string input)
    {
        EnsureClosed();
        EdenButtonMap.Set(Paths.GlobalConfigFile, pad, buttonKey, input);
    }

    public void ApplyButtonLayout(PadInfo pad, IReadOnlyDictionary<string, string> layout)
    {
        EnsureClosed();
        EdenButtonMap.Apply(Paths.GlobalConfigFile, pad, layout);
    }

    public int PlayerStyle() => EdenControls.PlayerStyle(Paths.GlobalConfigFile);

    public void SetPlayerStyle(int style)
    {
        EnsureClosed();
        EdenControls.SetPlayerStyle(Paths.GlobalConfigFile, style);
    }

    public IReadOnlyList<string> InputProfiles() => EdenControls.Profiles(Paths.InputProfileDir);

    public void SaveInputProfile(string name)
    {
        EnsureClosed();
        EdenControls.SaveProfile(Paths.GlobalConfigFile, Paths.InputProfileDir, name);
    }

    public string? GameInputProfile(string titleId) => EdenControls.GameProfile(Paths.PerGameConfigFile(titleId));

    public void SetGameInputProfile(string titleId, string? name)
    {
        EnsureClosed();
        EdenControls.SetGameProfile(Paths.PerGameConfigFile(titleId), name);
    }

    private static void EnsureClosed()
    {
        if (EdenProcess.IsRunning())
        {
            throw new EdenRunningException();
        }
    }

    /// <summary>True while player 1 is still on Eden's keyboard defaults.</summary>
    public bool ControllerNeedsMapping() => EdenControllerDefaults.NeedsMapping(QtIni.Load(Paths.GlobalConfigFile));

    /// <summary>Plan to open Eden's own window (no game), for settings Scanline doesn't cover.</summary>
    public Core.Launch.LaunchPlan PlanEdenUi()
    {
        var eden = Install.Current ?? throw new InvalidOperationException("No Eden install found. Install one in the Eden manager.");
        var workDir = Path.Combine(_app.Runtime, "cwd");
        return new Core.Launch.LaunchPlan
        {
            Executable = eden.ExecutablePath,
            Arguments = [],
            WorkingDirectory = workDir,
            Prepare = [() => Directory.CreateDirectory(workDir)],
            Notes = [$"Eden {eden.Channel} {eden.Version}: settings window"],
        };
    }

    /// <summary>Highest master key index in a prod.keys file, or -1 if unreadable.</summary>
    public static int MaxMasterKeyOf(string prodKeys)
    {
        try
        {
            return File.Exists(prodKeys) ? KeyRequirements.MaxMasterKey(KeyFile.Parse(File.ReadAllText(prodKeys))) : -1;
        }
        catch (IOException)
        {
            return -1;
        }
    }

    /// <summary>
    /// The game's own square icon (JPEG), read from its control data with the user's keys and cached
    /// under &lt;data&gt;/icons. Null when the keys can't open it (tried again after new keys are imported).
    /// </summary>
    public string? GameIcon(Core.Library.GameEntry game)
    {
        if (string.IsNullOrEmpty(game.TitleId) || !File.Exists(game.Path))
        {
            return null;
        }
        var dir = Path.Combine(_app.Data, "icons");
        var icon = Path.Combine(dir, game.TitleId + ".jpg");
        if (File.Exists(icon))
        {
            return icon;
        }
        var none = Path.Combine(dir, game.TitleId + ".none");
        var prod = Path.Combine(Paths.KeysDir, "prod.keys");
        if (!File.Exists(prod) || (File.Exists(none) && File.GetLastWriteTimeUtc(none) > File.GetLastWriteTimeUtc(prod)))
        {
            return null;
        }
        Directory.CreateDirectory(dir);
        try
        {
            var title = Path.Combine(Paths.KeysDir, "title.keys");
            var control = SwitchControl.Read(game.Path, KeyFile.Parse(File.ReadAllText(prod)),
                File.Exists(title) ? KeyFile.Parse(File.ReadAllText(title)) : null);
            if (control?.IconJpeg is { Length: > 0 } jpeg)
            {
                File.WriteAllBytes(icon + ".tmp", jpeg);
                File.Move(icon + ".tmp", icon, overwrite: true);
                return icon;
            }
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or IOException or ArgumentException or System.Security.Cryptography.CryptographicException)
        {
        }
        File.WriteAllText(none, "");
        return null;
    }

    private byte[]? LoadHeaderKey()
    {
        var prod = Path.Combine(Paths.KeysDir, "prod.keys");
        if (!File.Exists(prod))
        {
            return null;
        }
        try
        {
            return KeyFile.TryGetHeaderKey(KeyFile.Parse(File.ReadAllText(prod)), out var k) ? k : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
