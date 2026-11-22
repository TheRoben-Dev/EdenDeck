using EdenDeck.Backends.Eden.SystemFiles;
using EdenDeck.Core.Launch;
using EdenDeck.Core.Library;
using EdenDeck.Core.Profiles;
using EdenDeck.Host;

// Headless entry point. Per-game Steam shortcuts run `scanline launch eden:<TITLEID>`, which
// applies the game's settings exactly like the UI does and then starts Eden.

return await Cli.Run(args);

internal static class Cli
{
    private const string Usage = """
        Scanline command line

          scanline launch <game-id|path>           apply the game's settings and start it
          scanline scan                            rescan library folders
          scanline library                         list games
          scanline folders add <dir> | remove <dir> | list
          scanline eden status                     installed Eden and system files
          scanline eden check <stable|nightly>     check Eden's official feed for an update
          scanline eden install <stable|nightly>   download + verify + install from the official feed
          scanline eden rollback                   switch back to the previous Eden version
          scanline eden use <AppImage>             use an Eden AppImage you already have
          scanline keys import <prod.keys> [title.keys]           your own default keys
          scanline firmware import <folder>                       your own firmware (.nca files)
          scanline keyset add <label> <prod.keys> [title.keys] [firmware-folder]
          scanline keyset list
          scanline game set <game-id> [--keyset <id>|--no-keyset] [--framegen off|builtin] [--fps <native fps>]
          scanline game show <game-id>
          scanline steam add <game-id|path|--app|--all>   Steam shortcut that starts the game with its settings
          scanline steam remove <game-id|--app>
          scanline steam list                      shortcuts Scanline manages
          scanline steam apply                     write them into Steam now (Steam must be closed)

        Scanline never downloads keys, firmware or games.
        """;

    public static async Task<int> Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }
        try
        {
            var host = new AppHost();
            return args[0] switch
            {
                "launch" when args.Length >= 2 => await Launch(host, args[1]),
                "scan" => Scan(host),
                "library" => ListLibrary(host),
                "folders" => Folders(host, args[1..]),
                "eden" when args.Length >= 2 => await Eden(host, args[1..]),
                "keys" when args.Length >= 3 && args[1] == "import" => ImportKeys(host, args),
                "firmware" when args.Length >= 3 && args[1] == "import" => ImportFirmware(host, args[2]),
                "keyset" when args.Length >= 2 => KeySet(host, args[1..]),
                "game" when args.Length >= 3 => Game(host, args[1..]),
                "steam" when args.Length >= 2 => Steam(host, args[1..]),
                _ => Fail(Usage),
            };
        }
        catch (Exception e) when (e is InvalidOperationException or KeyImportException or IOException or ArgumentException or HttpRequestException or InvalidDataException or KeyNotFoundException)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return 1;
        }
    }

    private static async Task<int> Launch(AppHost host, string idOrPath)
    {
        var game = host.FindGame(idOrPath) ?? throw new InvalidOperationException($"Game not found: {idOrPath} (run `scanline scan`?)");
        var plan = host.PlanLaunch(game);
        var launcher = new ProcessLauncher(Console.WriteLine);
        host.MarkPlayed(game);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        try
        {
            return await launcher.RunAsync(plan, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
    }

    private static int Scan(AppHost host)
    {
        if (host.Settings.LibraryFolders.Count == 0)
        {
            return Fail("No library folders. Add one with `scanline folders add <dir>`.");
        }
        var games = host.Rescan();
        Console.WriteLine($"{games.Count} games");
        return ListLibrary(host);
    }

    private static int ListLibrary(AppHost host)
    {
        foreach (var g in host.Library)
        {
            var extra = (g.Updates.Count > 0 ? $" +{g.Updates.Count} update" : "") + (g.Dlc.Count > 0 ? $" +{g.Dlc.Count} DLC" : "");
            Console.WriteLine($"{g.GameId,-24} {g.Base.Title}{extra}  [{g.Base.TitleIdSource}]");
        }
        return 0;
    }

    private static int Folders(AppHost host, string[] a)
    {
        var folders = host.Settings.LibraryFolders.ToList();
        switch (a.FirstOrDefault())
        {
            case "add" when a.Length >= 2:
                var dir = Path.GetFullPath(a[1]);
                if (!Directory.Exists(dir))
                {
                    return Fail($"Not a directory: {dir}");
                }
                if (folders.All(f => f.Path != dir))
                {
                    folders.Add(new LibraryFolder(dir));
                }
                break;
            case "remove" when a.Length >= 2:
                folders.RemoveAll(f => f.Path == Path.GetFullPath(a[1]));
                break;
            case "list" or null:
                break;
            default:
                return Fail(Usage);
        }
        host.SaveSettings(host.Settings with { LibraryFolders = folders });
        foreach (var f in folders)
        {
            Console.WriteLine(f.Path);
        }
        return 0;
    }

    private static async Task<int> Eden(AppHost host, string[] a)
    {
        var install = host.Eden.Install;
        switch (a[0])
        {
            case "status":
                Console.WriteLine(install.Current is { } c ? $"Eden {c.Channel} {c.Version} ({(c.ManagedByEdenDeck ? "managed" : "external")}): {c.ExecutablePath}" : "Eden: not found");
                if (install.Previous is { } p)
                {
                    Console.WriteLine($"Previous (rollback): {p.Channel} {p.Version}");
                }
                Console.WriteLine($"Schema: {host.Eden.Schema.Label}");
                Console.WriteLine($"Data: {host.Eden.Paths.DataDir}  Config: {host.Eden.Paths.ConfigDir}");
                foreach (var s in host.Eden.SystemFilesImpl.Status())
                {
                    Console.WriteLine($"{(s.Present ? "ok " : "-- ")} {s.Kind}: {s.Detail}");
                }
                return 0;
            case "check" when a.Length >= 2:
                var rel = await install.CheckForUpdateAsync(a[1], CancellationToken.None);
                Console.WriteLine(rel == null ? "Up to date (or no Linux AppImage in the feed)." : $"Available: {rel.Channel} {rel.Version} ({rel.Size / 1048576} MB){(rel.Sha256 == null ? ", no checksum published" : "")}");
                return 0;
            case "install" when a.Length >= 2:
                var r = await install.CheckForUpdateAsync(a[1], CancellationToken.None) ?? throw new InvalidOperationException("Nothing to install: already current, or the feed has no Linux AppImage.");
                var progress = new Progress<double>(x => Console.Write($"\r{x:P0}   "));
                var installed = await install.InstallAsync(r, progress, CancellationToken.None);
                Console.WriteLine($"\nInstalled Eden {installed.Channel} {installed.Version}{(r.Sha256 != null ? " (checksum verified)" : " (no checksum published)")}");
                return 0;
            case "use" when a.Length >= 2:
                var file = Path.GetFullPath(a[1]);
                if (!File.Exists(file))
                {
                    return Fail($"Not a file: {file}");
                }
                install.UseExternal(EdenDeck.Backends.Eden.Install.EdenInstallManager.FromFile(file, managed: false));
                Console.WriteLine($"Now using {file}");
                return 0;
            case "rollback":
                var back = install.Rollback();
                Console.WriteLine($"Now using Eden {back.Channel} {back.Version}");
                return 0;
            default:
                return Fail(Usage);
        }
    }

    private static int ImportKeys(AppHost host, string[] a)
    {
        host.Eden.SystemFilesImpl.ImportDefaultKeys(a[2], a.Length >= 4 ? a[3] : null);
        Console.WriteLine("Keys imported.");
        return 0;
    }

    private static int ImportFirmware(AppHost host, string folder)
    {
        var n = host.Eden.SystemFilesImpl.ImportFirmware(folder);
        Console.WriteLine($"Firmware imported ({n} files).");
        return 0;
    }

    private static int Steam(AppHost host, string[] a)
    {
        var steam = host.Steam;
        switch (a[0])
        {
            case "add" when a.Length >= 2:
            {
                if (steam.Unavailable is { } why)
                {
                    return Fail(why);
                }
                var items = a[1] switch
                {
                    "--app" => [("app", steam.ForApp())],
                    "--all" => host.Library.Select(g => (g.GameId, steam.ForGame(g))).ToList(),
                    var id => [(host.FindGame(id)?.GameId ?? throw new KeyNotFoundException($"Game not found: {id}"), steam.ForGame(host.FindGame(id)!))],
                };
                var written = false;
                foreach (var (key, shortcut) in items)
                {
                    written = steam.Add(key, shortcut);
                    Console.WriteLine($"{shortcut.AppName}: {shortcut.Exe} {shortcut.LaunchOptions}".TrimEnd());
                }
                Console.WriteLine(written ? "Added to Steam. Start Steam to see it." : "Saved. Steam is running: close Steam, then run `scanline steam apply` (or add it from the app, which restarts Steam for you).");
                return 0;
            }
            case "remove" when a.Length >= 2:
            {
                var key = a[1] == "--app" ? "app" : host.FindGame(a[1])?.GameId ?? a[1];
                if (!steam.Remove(key))
                {
                    return Fail($"Scanline has no Steam shortcut for {a[1]}.");
                }
                Console.WriteLine(steam.SteamRunning ? "Will be removed the next time Steam is closed and shortcuts are applied." : "Removed from Steam.");
                return 0;
            }
            case "list":
                foreach (var e in steam.Wanted())
                {
                    Console.WriteLine($"{e.Key,-32} {e.Shortcut.AppName}{(e.Remove ? "  (to remove)" : "")}  appid {e.Shortcut.AppId}");
                }
                return 0;
            case "apply":
                if (a.Contains("--after-steam-exits"))
                {
                    return steam.ApplyAfterSteamExits();
                }
                var n = steam.Apply();
                if (n < 0)
                {
                    return Fail("Steam is running. Close it first: Steam menu > Exit.");
                }
                Console.WriteLine($"Done ({n} Steam account file(s) changed).");
                return 0;
            default:
                return Fail(Usage);
        }
    }

    private static int KeySet(AppHost host, string[] a)
    {
        switch (a[0])
        {
            case "add" when a.Length >= 3:
                var set = host.Eden.KeySets.Import(a[1], a[2], a.Length >= 4 ? a[3] : null, a.Length >= 5 ? a[4] : null);
                Console.WriteLine($"Added key set '{set.Label}' (id {set.Id}){(set.HasFirmware ? " with firmware" : "")}");
                return 0;
            case "list":
                foreach (var k in host.Eden.KeySets.List())
                {
                    Console.WriteLine($"{k.Id,-20} {k.Label}{(k.HasFirmware ? "  [firmware]" : "")}");
                }
                return 0;
            default:
                return Fail(Usage);
        }
    }

    private static int Game(AppHost host, string[] a)
    {
        var game = host.FindGame(a[1]) ?? throw new InvalidOperationException($"Game not found: {a[1]}");
        var profile = host.Profiles.Get(game.GameId);
        if (a[0] == "set")
        {
            for (var i = 2; i < a.Length; i++)
            {
                switch (a[i])
                {
                    case "--keyset" when i + 1 < a.Length:
                        var id = a[++i];
                        _ = host.Eden.KeySets.Get(id) ?? throw new InvalidOperationException($"No key set '{id}'");
                        profile = profile with { KeySet = id };
                        break;
                    case "--no-keyset":
                        profile = profile with { KeySet = null };
                        break;
                    case "--framegen" when i + 1 < a.Length:
                        profile = profile with { FrameGen = Enum.Parse<FrameGenMode>(a[++i], ignoreCase: true) };
                        break;
                    case "--fps" when i + 1 < a.Length:
                        profile = profile with { NativeFps = int.Parse(a[++i]) };
                        break;
                    default:
                        return Fail($"Unknown option {a[i]}");
                }
            }
            host.Profiles.Set(game.GameId, profile);
        }
        Console.WriteLine($"{game.GameId}  {game.Base.Title}");
        Console.WriteLine($"  key set:   {profile.KeySet ?? "(default)"}");
        Console.WriteLine($"  frame gen: {FrameGenPolicy.Decide(profile.FrameGen, profile.NativeFps).Reason}");
        return 0;
    }

    private static int Fail(string msg)
    {
        Console.Error.WriteLine(msg);
        return 2;
    }
}
