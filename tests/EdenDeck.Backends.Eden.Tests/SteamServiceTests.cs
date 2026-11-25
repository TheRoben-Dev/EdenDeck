using EdenDeck.Core.Library;
using EdenDeck.Core.Steam;
using EdenDeck.Host;
using Xunit;

namespace EdenDeck.Backends.Eden.Tests;

public class SteamServiceTests
{
    [Fact]
    public void GameShortcutsGetTheirOwnIconOnceItsDrawn()
    {
        using var env = new TestEnv();
        var app = Path.Combine(env.Home, "app");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "scanline"), "");
        File.WriteAllText(Path.Combine(app, "scanline.png"), "");
        var config = Path.Combine(env.Home, ".local", "share", "Steam", "userdata", "1234", "config");
        Directory.CreateDirectory(config);
        var steam = new SteamService(env.AppPaths(), env.Home, app);
        var game = new LibraryGame(new GameEntry { BackendId = "rpcs3", TitleId = "BLUS12345", Title = "A Game", Path = "/g", Kind = ContentKind.Base }, [], []);

        // Added before its art exists: Scanline's icon.
        var first = steam.ForGame(game);
        Assert.Equal(Path.Combine(app, "scanline.png"), first.Icon);
        Assert.True(steam.Add(game.GameId, first));

        // The app draws the art; the next write points the shortcut at the game's icon.
        var art = steam.ArtDirFor(game.GameId)!;
        Directory.CreateDirectory(art);
        File.WriteAllText(Path.Combine(art, "icon.png"), "");
        Assert.Equal(1, steam.Apply());
        var entry = SteamShortcuts.Load(config).Find(first.AppId)!;
        Assert.Equal(Path.Combine(art, "icon.png"), entry.Str("icon"));
        Assert.Equal(Path.Combine(art, "icon.png"), steam.ForGame(game).Icon);
        Assert.Equal(0, steam.Apply());  // nothing left to change
    }

    [Fact]
    public void GamesWithTheSameTitleGetTheirOwnShortcut()
    {
        using var env = new TestEnv();
        var app = Path.Combine(env.Home, "app");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "scanline"), "");
        Directory.CreateDirectory(Path.Combine(env.Home, ".local", "share", "Steam", "userdata", "1234", "config"));
        var steam = new SteamService(env.AppPaths(), env.Home, app);
        LibraryGame G(string backend, string tid) => new(new GameEntry { BackendId = backend, TitleId = tid, Title = "Tetris", Path = "/g/" + tid, Kind = ContentKind.Base }, [], []);
        var gb = G("mgba", "GB_TETRIS-0A");
        var gba = G("mgba", "BTEE-11");

        steam.Add(gb.GameId, steam.ForGame(gb));
        var second = steam.ForGame(gba);
        steam.Add(gba.GameId, second);

        Assert.Equal("Tetris (BTEE-11)", second.AppName);
        Assert.Equal("Tetris", steam.ForGame(gb).AppName);  // updating the first keeps its name
        Assert.Equal(2, steam.Wanted().Select(e => e.Shortcut.AppId).Distinct().Count());
    }
}
