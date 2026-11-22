using System.Diagnostics;
using System.Text.Json;
using EdenDeck.Core;
using EdenDeck.Core.Library;
using EdenDeck.Core.Steam;

namespace EdenDeck.Host;

/// <summary>
/// "Add to Steam": per-game non-Steam shortcuts that start the game through <c>scanline launch</c>
/// (so every game setting is applied), plus one for Scanline itself.
///
/// Steam only reads shortcuts.vdf when it starts, and saves its own copy over the file whenever its
/// shortcut list changes (play time of any non-Steam game included), so writing while Steam runs
/// is unreliable. Scanline therefore keeps the list of shortcuts it manages in its own config and
/// writes them only while Steam is closed: right away if Steam isn't running, otherwise from a
/// small detached helper that waits for Steam to exit, writes, and starts Steam again
/// (<see cref="WriteWhenSteamRestarts"/>). Every app start also re-applies the list if Steam is closed.
/// </summary>
public sealed class SteamService
{
    public const string Tag = "Scanline";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _registry;
    private readonly string _artRoot;
    private readonly string _home;
    private readonly string? _appDir;

    public SteamService(AppPaths paths, string home, string? appDir)
    {
        _registry = Path.Combine(paths.Config, "steam-shortcuts.json");
        _artRoot = Path.Combine(paths.Data, "steam-art");
        _home = home;
        _appDir = appDir;
    }

    /// <summary>The headless launcher next to the app, which Steam shortcuts run.</summary>
    public string? Launcher => _appDir is { } d && File.Exists(Path.Combine(d, "scanline")) ? Path.Combine(d, "scanline") : null;

    /// <summary>The app itself, for the Scanline shortcut.</summary>
    public string? AppBinary => _appDir is { } d && File.Exists(Path.Combine(d, "Scanline.x86_64")) ? Path.Combine(d, "Scanline.x86_64") : null;

    /// <summary>Scanline's own Steam artwork (32-bit 3D theme), shipped in steam-art next to the app.</summary>
    public string? ArtDir => _appDir is { } d && Directory.Exists(Path.Combine(d, "steam-art")) ? Path.Combine(d, "steam-art") : null;

    public bool Available => Launcher != null && SteamShortcuts.AccountConfigDirs(_home).Count > 0;

    /// <summary>Why Add to Steam can't work here, or null when it can.</summary>
    public string? Unavailable =>
        Launcher == null ? "Add to Steam needs the installed app (the scanline launcher isn't next to it)." :
        SteamShortcuts.AccountConfigDirs(_home).Count == 0 ? "Steam isn't set up for this user yet. Sign in to Steam once, then try again." :
        null;

    public bool SteamRunning => SteamShortcuts.SteamRunning(_home);

    public SteamShortcut ForGame(LibraryGame game)
    {
        var launcher = Launcher ?? throw new InvalidOperationException(Unavailable);
        return new SteamShortcut
        {
            AppName = game.Base.Title,
            Exe = launcher,
            StartDir = Path.GetDirectoryName(launcher)!,
            LaunchOptions = $"launch \"{game.GameId}\"",
            Icon = IconIn(Path.GetDirectoryName(launcher)!),
            Tags = [Tag],
        };
    }

    public SteamShortcut ForApp()
    {
        var app = AppBinary ?? throw new InvalidOperationException(Unavailable);
        return new SteamShortcut
        {
            AppName = "Scanline",
            Exe = app,
            StartDir = Path.GetDirectoryName(app)!,
            Icon = ArtDir is { } art && File.Exists(Path.Combine(art, "icon.png")) ? Path.Combine(art, "icon.png") : IconIn(Path.GetDirectoryName(app)!),
            Tags = [Tag],
        };
    }

    /// <summary>Steam shows PNG icons, not SVG; older packages only had the SVG.</summary>
    private static string IconIn(string dir) =>
        File.Exists(Path.Combine(dir, "scanline.png")) ? Path.Combine(dir, "scanline.png") : Path.Combine(dir, "scanline.svg");

    // ---- The list Scanline manages ----------------------------------------------------------------

    public sealed record Entry(string Key, SteamShortcut Shortcut, bool Remove = false);

    /// <summary>Registry key of the shortcut for Scanline itself.</summary>
    public const string AppKey = "app";

    /// <summary>
    /// Where the artwork for a shortcut lives: Scanline's own ships with the app; a game's is rendered
    /// by the app into &lt;data&gt;/steam-art/&lt;game&gt; (see the app's SteamArt) when it's added.
    /// </summary>
    public string? ArtDirFor(string key) =>
        key == AppKey ? ArtDir : Path.Combine(_artRoot, string.Concat(key.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_')));

    /// <summary>True when the game's artwork has been rendered.</summary>
    public bool HasArt(string key) => ArtDirFor(key) is { } d && File.Exists(Path.Combine(d, "capsule.png"));

    /// <summary>Puts a managed shortcut's artwork into Steam (after the app rendered it). Never fatal.</summary>
    public void InstallArt(string key)
    {
        if (Wanted().FirstOrDefault(e => e.Key == key && !e.Remove) is { } e)
        {
            Artwork(e.Shortcut.AppId, ArtDirFor(key), remove: false);
        }
    }

    /// <summary>Installs (or removes) artwork for one shortcut in every account. Never fatal.</summary>
    private void Artwork(uint appId, string? art, bool remove)
    {
        if (art == null || !Directory.Exists(art))
        {
            return;
        }
        foreach (var config in SteamShortcuts.AccountConfigDirs(_home))
        {
            try
            {
                SteamShortcuts.InstallArtwork(config, appId, art, remove);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn($"steam: artwork for {appId} in {config}: {e.Message}");
            }
        }
    }

    public IReadOnlyList<Entry> Wanted()
    {
        try
        {
            return File.Exists(_registry) ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_registry), Json) ?? [] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void SaveWanted(IEnumerable<Entry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_registry)!);
        var tmp = _registry + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(entries.ToList(), Json));
        File.Move(tmp, _registry, overwrite: true);
    }

    /// <summary>
    /// Managed shortcuts Steam doesn't have yet (or still has, for removals), in the most recently
    /// used account. These show up once Steam restarts (see <see cref="WriteWhenSteamRestarts"/>).
    /// </summary>
    public IReadOnlyList<Entry> Pending()
    {
        var config = SteamShortcuts.AccountConfigDirs(_home).FirstOrDefault();
        if (config == null)
        {
            return [];
        }
        SteamShortcuts file;
        try
        {
            file = SteamShortcuts.Load(config);
        }
        catch (InvalidDataException)
        {
            return Wanted();
        }
        return Wanted().Where(e => (file.Find(e.Shortcut.AppId) != null) == e.Remove).ToList();
    }

    /// <summary>True when Scanline has (or will add) a shortcut under this key ("app" or a game id).</summary>
    public bool Has(string key) => Wanted().Any(e => e.Key == key && !e.Remove);

    /// <summary>Remembers the shortcut and writes it now if Steam is closed. Returns true if it's in Steam's file already.</summary>
    public bool Add(string key, SteamShortcut shortcut)
    {
        SaveWanted(Wanted().Where(e => e.Key != key).Append(new Entry(key, shortcut)));
        Artwork(shortcut.AppId, ArtDirFor(key), remove: false);  // Steam picks up grid files without a restart
        return !SteamRunning && Apply() >= 0;
    }

    /// <summary>Forgets the shortcut and removes it from Steam (now if Steam is closed, else at the next apply).</summary>
    public bool Remove(string key)
    {
        var old = Wanted().FirstOrDefault(e => e.Key == key);
        if (old == null)
        {
            return false;
        }
        SaveWanted(Wanted().Where(e => e.Key != key).Append(old with { Remove = true }));
        Artwork(old.Shortcut.AppId, ArtDirFor(key), remove: true);
        if (!SteamRunning)
        {
            Apply();
        }
        return true;
    }

    /// <summary>
    /// Writes the managed shortcuts into every signed-in account's shortcuts.vdf. Only call with
    /// Steam closed. Returns how many entries changed, or -1 if Steam is running (nothing written).
    /// </summary>
    public int Apply()
    {
        if (SteamRunning)
        {
            return -1;
        }
        var wanted = Wanted();
        var changed = 0;
        foreach (var config in SteamShortcuts.AccountConfigDirs(_home))
        {
            var file = SteamShortcuts.Load(config);
            var before = File.Exists(file.FilePath) ? File.ReadAllBytes(file.FilePath) : [];
            foreach (var e in wanted)
            {
                if (e.Remove)
                {
                    file.Remove(e.Shortcut.AppId);
                }
                else
                {
                    file.AddOrUpdate(e.Shortcut);
                }
            }
            var after = file.ToBytes();
            if (!before.AsSpan().SequenceEqual(after))
            {
                file.Save();
                changed++;
            }
        }
        foreach (var e in wanted.Where(e => !e.Remove))
        {
            Artwork(e.Shortcut.AppId, ArtDirFor(e.Key), remove: false);
        }
        // Removals are done once they've been written.
        if (wanted.Any(e => e.Remove))
        {
            SaveWanted(wanted.Where(e => !e.Remove));
        }
        AppLog.Info($"steam: applied {wanted.Count} shortcut(s), {changed} file(s) changed");
        return changed;
    }

    // ---- Restarting Steam -------------------------------------------------------------------------

    /// <summary>True inside Steam's Game Mode (gamescope session), where the session restarts Steam on its own.</summary>
    public static bool GameMode =>
        Environment.GetEnvironmentVariable("SteamGamepadUI") == "1" ||
        Environment.GetEnvironmentVariable("GAMESCOPE_WAYLAND_DISPLAY") is { Length: > 0 } ||
        string.Equals(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"), "gamescope", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_DESKTOP"), "gamescope", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Starts a detached helper (<c>scanline steam apply --after-steam-exits</c>) that waits, for up
    /// to a day, for the player to close or restart Steam (or the computer) and writes the pending
    /// shortcuts the moment Steam has exited, so they're there when Steam starts again. Scanline
    /// never closes Steam itself: in Game Mode Steam is the session, and stopping it left the Deck
    /// on a dead screen. Only one helper runs at a time; it writes everything pending when it fires.
    /// </summary>
    public void WriteWhenSteamRestarts()
    {
        if (HelperRunning())
        {
            return;
        }
        var launcher = Launcher ?? throw new InvalidOperationException(Unavailable);
        Run("setsid", "-f", launcher, "steam", "apply", "--after-steam-exits");
        AppLog.Info("steam: waiting for Steam to restart to add shortcuts");
    }

    private string HelperLock => _registry + ".waiting";

    /// <summary>True while a helper holds the lock file (it keeps it open with no sharing).</summary>
    private bool HelperRunning()
    {
        try
        {
            using var f = new FileStream(HelperLock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static void Run(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        try
        {
            Process.Start(psi)?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new InvalidOperationException($"Couldn't run {file} ({e.Message}). Close Steam yourself; Scanline adds the shortcuts the next time it starts with Steam closed.", e);
        }
    }

    /// <summary>The helper's side: waits (up to <paramref name="maxWait"/>, a day by default) for Steam to exit, then applies.</summary>
    public int ApplyAfterSteamExits(TimeSpan? maxWait = null)
    {
        FileStream? held = null;
        Directory.CreateDirectory(Path.GetDirectoryName(HelperLock)!);
        for (var attempt = 0; held == null; attempt++)
        {
            try
            {
                held = new FileStream(HelperLock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(100);  // the app may be probing the lock this instant
            }
            catch (IOException)
            {
                return 0;  // another helper is already waiting; it writes everything pending
            }
        }
        using var _ = held;
        // A reboot or logout sends everyone SIGTERM at once; keep going until Steam is gone and the
        // file is written.
        using var term = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM, c => c.Cancel = true);
        using var hup = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGHUP, c => c.Cancel = true);
        var until = DateTime.UtcNow + (maxWait ?? TimeSpan.FromHours(24));
        while (SteamRunning)
        {
            if (DateTime.UtcNow > until)
            {
                AppLog.Warn("steam: Steam wasn't restarted in time; shortcuts are added the next time Scanline starts with Steam closed");
                return 1;
            }
            Thread.Sleep(200);
        }
        Apply();
        return 0;
    }
}
