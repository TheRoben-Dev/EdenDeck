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
/// (<see cref="RestartSteamAndApply"/>). Every app start also re-applies the list if Steam is closed.
/// </summary>
public sealed class SteamService
{
    public const string Tag = "Scanline";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _registry;
    private readonly string _home;
    private readonly string? _appDir;

    public SteamService(AppPaths paths, string home, string? appDir)
    {
        _registry = Path.Combine(paths.Config, "steam-shortcuts.json");
        _home = home;
        _appDir = appDir;
    }

    /// <summary>The headless launcher next to the app, which Steam shortcuts run.</summary>
    public string? Launcher => _appDir is { } d && File.Exists(Path.Combine(d, "scanline")) ? Path.Combine(d, "scanline") : null;

    /// <summary>The app itself, for the Scanline shortcut.</summary>
    public string? AppBinary => _appDir is { } d && File.Exists(Path.Combine(d, "Scanline.x86_64")) ? Path.Combine(d, "Scanline.x86_64") : null;

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
            Icon = IconIn(Path.GetDirectoryName(app)!),
            Tags = [Tag],
        };
    }

    /// <summary>Steam shows PNG icons, not SVG; older packages only had the SVG.</summary>
    private static string IconIn(string dir) =>
        File.Exists(Path.Combine(dir, "scanline.png")) ? Path.Combine(dir, "scanline.png") : Path.Combine(dir, "scanline.svg");

    // ---- The list Scanline manages ----------------------------------------------------------------

    public sealed record Entry(string Key, SteamShortcut Shortcut, bool Remove = false);

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
    /// used account. These show up once Steam restarts through <see cref="RestartSteamAndApply"/>.
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
        string.Equals(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"), "gamescope", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Starts a detached helper (<c>scanline steam apply --after-steam-exits</c>) and asks Steam to
    /// shut down. The helper writes the shortcuts the moment Steam has exited and starts Steam again
    /// outside Game Mode (in Game Mode the session does that itself). Scanline is closed along with Steam
    /// when Steam started it.
    /// </summary>
    public void RestartSteamAndApply()
    {
        var launcher = Launcher ?? throw new InvalidOperationException(Unavailable);
        var helper = new ProcessStartInfo("setsid")
        {
            UseShellExecute = false,
            RedirectStandardInput = false,
            ArgumentList = { "-f", launcher, "steam", "apply", "--after-steam-exits", GameMode ? "--no-restart" : "--restart" },
        };
        try
        {
            Process.Start(helper)?.Dispose();
            AppLog.Info("steam: shutting Steam down to add shortcuts");
            Process.Start(new ProcessStartInfo("steam") { UseShellExecute = false, ArgumentList = { "-shutdown" } })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new InvalidOperationException($"Couldn't restart Steam ({e.Message}). Close Steam yourself; Scanline adds the shortcuts the next time it starts with Steam closed.", e);
        }
    }

    /// <summary>The helper's side: waits (up to two minutes) for Steam to exit, applies, optionally restarts Steam.</summary>
    public int ApplyAfterSteamExits(bool restart)
    {
        var until = DateTime.UtcNow.AddMinutes(2);
        while (SteamRunning)
        {
            if (DateTime.UtcNow > until)
            {
                AppLog.Warn("steam: Steam didn't exit within two minutes; shortcuts not written");
                return 1;
            }
            Thread.Sleep(100);
        }
        Apply();
        if (restart)
        {
            Process.Start(new ProcessStartInfo("setsid") { UseShellExecute = false, ArgumentList = { "-f", "steam" } })?.Dispose();
        }
        return 0;
    }
}
