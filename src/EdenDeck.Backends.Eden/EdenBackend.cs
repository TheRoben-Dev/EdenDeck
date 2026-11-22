using EdenDeck.Backends.Eden.Config;
using EdenDeck.Backends.Eden.Install;
using EdenDeck.Backends.Eden.Library;
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
    }

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
    public EdenPaths Paths => _paths ??= EdenPaths.Resolve(Path.Combine(_app.Runtime, "cwd"));

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

    /// <summary>Pre-launch check that the keys the game will run with are new enough (see <see cref="KeyRequirements"/>).</summary>
    public void CheckKeys(string title, IEnumerable<string> gameFiles, string? keySetId)
    {
        var set = keySetId != null ? KeySets.Get(keySetId) : null;
        var prod = set != null ? Path.Combine(set.Directory, "prod.keys") : Path.Combine(Paths.KeysDir, "prod.keys");
        KeyRequirements.Check(title, gameFiles, prod, set != null ? $"The keys in set \"{set.Label}\"" : "Your keys");
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
