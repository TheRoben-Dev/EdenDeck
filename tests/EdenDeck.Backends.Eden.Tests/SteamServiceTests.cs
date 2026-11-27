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

    [Fact]
    public void SteamShortcutLaunchesUseTheControllerAndAccentTheAppSawLast()
    {
        using var env = new TestEnv();
        var app = new AppHost(env.AppPaths()) { Accent = "#3dff8f", Gamepads = () => [("Steam Virtual Gamepad", 0x28de, 0x11ff)], ScanPads = () => [] };
        var inApp = app.DefaultLaunchOptions();
        Assert.Equal(["Steam Virtual Gamepad"], inApp.Gamepads);

        // `scanline launch` from a Steam shortcut: no app, so nothing supplies these.
        var shortcut = new AppHost(env.AppPaths()) { ScanPads = () => [] }.DefaultLaunchOptions();
        Assert.Equal("#3dff8f", shortcut.Accent);
        Assert.Equal(["Steam Virtual Gamepad"], shortcut.Gamepads);

        // The app with no controller connected doesn't invent one.
        Assert.Empty(new AppHost(env.AppPaths()) { Gamepads = () => [], ScanPads = () => [] }.DefaultLaunchOptions().Gamepads);
    }

    [Fact]
    public void LaunchesGivePlayer1ToThePadInTheDock()
    {
        using var env = new TestEnv();
        var deck = new EdenDeck.Core.Input.Gamepad { Name = "Steam Deck", Guid = "03000000de280000ff11000001000000", Vendor = 0x28de, Product = 0x1205, SteamSlot = 0, BuiltIn = true };
        var xbox = new EdenDeck.Core.Input.Gamepad { Name = "Xbox Series X Controller", Guid = "03000000de280000ff11000001000000", Vendor = 0x045e, Product = 0x0b13, SteamSlot = 1, GuidPort = 1, GamepadIndex = 1, JoystickIndex = 1 };
        var options = new AppHost(env.AppPaths()) { ScanPads = () => [deck, xbox] }.DefaultLaunchOptions();
        Assert.Equal([xbox], options.Players);
        Assert.Equal(["Xbox Series X Controller"], options.Gamepads);

        var handheld = new AppHost(env.AppPaths()) { ScanPads = () => [deck] }.DefaultLaunchOptions();
        Assert.Equal([deck], handheld.Players);
    }

    [Fact]
    public void PadsSdlNamesFromItsDatabaseTakeTheAppsName()
    {
        var raw = new EdenDeck.Core.Input.Gamepad { Name = "Microsoft X-Box One S pad", Guid = "030000005e040000ea02000001000000", Vendor = 0x045e, Product = 0x02ea };
        var renamed = AppHost.Rename([raw, raw with { JoystickIndex = 1 }], [("Xbox One S Controller", 0x045e, 0x02ea), ("Xbox One S Controller", 0x045e, 0x02ea)]);
        Assert.Equal(["Xbox One S Controller", "Xbox One S Controller"], renamed.Select(p => p.Name));
        Assert.Equal([1, 2], renamed.Select(p => p.NameNumber));
    }
}
