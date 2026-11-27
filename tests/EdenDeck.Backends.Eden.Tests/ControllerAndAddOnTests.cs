using EdenDeck.Backends.Eden.Config;
using EdenDeck.Core.Library;
using Xunit;

namespace EdenDeck.Backends.Eden.Tests;

public sealed class ControllerAndAddOnTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "edendeck-ctl-" + Guid.NewGuid().ToString("N")[..10]);

    public ControllerAndAddOnTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private static readonly PadInfo SteamPad = new("030079f6de280000ff11000001000000", "Steam Virtual Gamepad", 0x28de, 0x11ff);

    [Fact]
    public void RecognisesDeckPadByGuidWhenNameAndIdsDiffer()
    {
        // Seen on a Steam Deck in Game Mode (0.2.6): Godot name/IDs differ from the evdev GUID.
        var deck = new PadInfo("030079f6de280000ff11000001000000", "#SettingsController_SteamController", 0x28de, 0x12fd);
        Assert.Equal((0x28de, 0x11ff), EdenControllerDefaults.GuidIds(deck.Guid));
        Assert.True(EdenControllerDefaults.HasKnownLayout(deck));
        var cfg = Path.Combine(Path.GetTempPath(), "edendeck-deckpad-" + Guid.NewGuid().ToString("N")[..8] + ".ini");
        File.WriteAllText(cfg, "[Controls]\nplayer_0_button_a=\"engine:keyboard,code:67,toggle:0\"\n");
        Assert.True(EdenControllerDefaults.EnsurePlayer1(cfg, deck));
        Assert.Contains("guid:03000000de280000ff11000001000000", QtIni.Load(cfg).Get("Controls", "player_0_button_a"));
        // A manual mapping made in Eden afterwards is left alone.
        Assert.False(EdenControllerDefaults.EnsurePlayer1(cfg, deck));
        File.Delete(cfg);
    }

    [Fact]
    public void EdenGuidZeroesNameCrc()
    {
        Assert.Equal("03000000de280000ff11000001000000", EdenControllerDefaults.EdenGuid(SteamPad.Guid));
        Assert.Throws<ArgumentException>(() => EdenControllerDefaults.EdenGuid("nothex"));
    }

    [Fact]
    public void MapsPlayer1OnlyWhenStillOnKeyboard()
    {
        var cfg = Path.Combine(_dir, "qt-config.ini");
        File.WriteAllText(cfg, "[Controls]\nplayer_0_button_a=\"engine:keyboard,code:67,toggle:0\"\n");
        Assert.True(EdenControllerDefaults.EnsurePlayer1(cfg, SteamPad));
        var ini = QtIni.Load(cfg);
        Assert.Equal("engine:sdl,port:0,guid:03000000de280000ff11000001000000,button:1", ini.Get("Controls", "player_0_button_a"));
        Assert.Equal("false", ini.Get("Controls", "player_0_button_a\\default"));
        Assert.Contains("axis:5,threshold:0.5", ini.Get("Controls", "player_0_button_zr"));
        Assert.Equal("true", ini.Get("Controls", "player_0_connected"));

        // A mapping now exists (user's or ours): never touched again.
        Assert.False(EdenControllerDefaults.EnsurePlayer1(cfg, SteamPad with { Guid = "0300000000000000aaaa000001000000" }));
    }

    private static EdenDeck.Core.Input.Gamepad Virtual(string name, int slot, bool builtIn = false) => new()
    {
        Name = name, Guid = "03000000de280000ff11000001000000", Vendor = builtIn ? 0x28de : 0x045e, Product = builtIn ? 0x1205 : 0x0b13,
        RawVendor = 0x28de, RawProduct = 0x11ff, SteamSlot = slot, BuiltIn = builtIn, GuidPort = slot, GamepadIndex = slot, JoystickIndex = slot,
    };

    [Fact]
    public void DockedControllerTakesPlayer1AndKeepsTheButtonChoices()
    {
        // Player 1 was set up on the Deck's controls (Steam pad, port 0) with A/B swapped in Settings → Buttons.
        var cfg = Path.Combine(_dir, "qt-config.ini");
        File.WriteAllText(cfg, "[Controls]\nplayer_0_button_a=\"engine:keyboard,code:67,toggle:0\"\n");
        Assert.True(EdenControllerDefaults.EnsurePlayer1(cfg, SteamPad));
        EdenButtonMap.Set(cfg, SteamPad, "button_a", "south");
        var swapped = QtIni.Load(cfg).Get("Controls", "player_0_button_a");

        var xbox = Virtual("Xbox Series X Controller", 1);
        var changes = EdenControllerDefaults.AssignPlayers(cfg, [xbox]);
        Assert.Equal(["player 1: Xbox Series X Controller"], changes);
        var ini = QtIni.Load(cfg);
        Assert.Equal(swapped!.Replace("port:0", "port:1"), ini.Get("Controls", "player_0_button_a"));
        Assert.Equal("engine:sdl,port:1,guid:03000000de280000ff11000001000000,axis_x:0,axis_y:1,offset_x:0,offset_y:0,invert_x:+,invert_y:+,deadzone:0.15,range:1", ini.Get("Controls", "player_0_lstick"));

        // Nothing changes on the next launch; undocked, the Deck's controls (port 0) are player 1 again.
        Assert.Empty(EdenControllerDefaults.AssignPlayers(cfg, [xbox]));
        Assert.Equal(["player 1: Steam Deck"], EdenControllerDefaults.AssignPlayers(cfg, [Virtual("Steam Deck", 0, builtIn: true)]));
        Assert.Contains("port:0,", QtIni.Load(cfg).Get("Controls", "player_0_button_b"));
    }

    [Fact]
    public void SecondControllerIsPlayer2WithPlayer1sButtons()
    {
        var cfg = Path.Combine(_dir, "qt-config.ini");
        File.WriteAllText(cfg, "[Controls]\n");
        var one = Virtual("Xbox Series X Controller", 1);
        var two = Virtual("DualSense Wireless Controller", 2);
        Assert.Equal(["player 1: Xbox Series X Controller", "player 2: DualSense Wireless Controller"], EdenControllerDefaults.AssignPlayers(cfg, [one, two]));
        var ini = QtIni.Load(cfg);
        Assert.Equal("engine:sdl,port:1,guid:03000000de280000ff11000001000000,button:1", ini.Get("Controls", "player_0_button_a"));
        Assert.Equal("engine:sdl,port:2,guid:03000000de280000ff11000001000000,button:1", ini.Get("Controls", "player_1_button_a"));
        Assert.Equal("true", ini.Get("Controls", "player_1_connected"));

        // The second pad goes away: player 2 is disconnected, player 1 stays.
        Assert.Equal(["player 2: disconnected"], EdenControllerDefaults.AssignPlayers(cfg, [one]));
        Assert.Equal("false", QtIni.Load(cfg).Get("Controls", "player_1_connected"));
    }

    [Fact]
    public void PlayersMappedInEdenToOtherPadsAreLeftAlone()
    {
        var cfg = Path.Combine(_dir, "qt-config.ini");
        const string own = "engine:sdl,port:0,guid:050000004c050000e60c000000810000,button:1";
        File.WriteAllText(cfg, $"[Controls]\nplayer_0_button_a=\"{own}\"\n");
        Assert.Empty(EdenControllerDefaults.AssignPlayers(cfg, [Virtual("Xbox Series X Controller", 1)]));
        Assert.Equal(own, QtIni.Load(cfg).Get("Controls", "player_0_button_a"));
    }

    [Fact]
    public void GameInputProfilesFollowPlayer1()
    {
        var profile = Path.Combine(_dir, "Zelda.ini");
        File.WriteAllText(profile, "[Controls]\nbutton_a=\"engine:sdl,port:0,guid:03000000de280000ff11000001000000,button:0\"\ntype=0\n");
        Assert.True(EdenControllerDefaults.RetargetProfile(profile, Virtual("Xbox Series X Controller", 1)));
        Assert.Equal("engine:sdl,port:1,guid:03000000de280000ff11000001000000,button:0", QtIni.Load(profile).Get("Controls", "button_a"));
        Assert.False(EdenControllerDefaults.RetargetProfile(profile, Virtual("Xbox Series X Controller", 1)));
    }

    [Fact]
    public void UnknownPadLayoutsAreLeftAlone()
    {
        var cfg = Path.Combine(_dir, "qt-config.ini");
        File.WriteAllText(cfg, "[Controls]\n");
        Assert.False(EdenControllerDefaults.EnsurePlayer1(cfg, new PadInfo("03000000aaaa0000bbbb000001000000", "Some Pad", 0xaaaa, 0xbbbb)));
        Assert.Equal("[Controls]\n", File.ReadAllText(cfg));
    }

    [Fact]
    public void ButtonMapReadsAndWritesEdenParams()
    {
        var cfg = Path.Combine(_dir, "qt-config.ini");
        File.WriteAllText(cfg, "[Controls]\nplayer_0_button_a=\"engine:keyboard,code:67,toggle:0\"\n");
        // Nothing mapped yet: games get Eden's auto map, by position.
        Assert.Equal("position", EdenButtonMap.PresetOf(EdenButtonMap.Read(cfg)));

        EdenButtonMap.Apply(cfg, SteamPad, EdenButtonMap.LabelLayout);
        var ini = QtIni.Load(cfg);
        Assert.Equal("engine:sdl,port:0,guid:03000000de280000ff11000001000000,button:0", ini.Get("Controls", "player_0_button_a"));
        Assert.Contains("axis:5,threshold:0.5", ini.Get("Controls", "player_0_button_zr"));  // auto map filled the rest
        Assert.Contains("axis_x:0,axis_y:1", ini.Get("Controls", "player_0_lstick"));
        var map = EdenButtonMap.Read(cfg);
        Assert.Equal("label", EdenButtonMap.PresetOf(map));
        Assert.Equal("south", map["button_a"]);

        EdenButtonMap.Set(cfg, SteamPad, "button_zl", "dup");
        EdenButtonMap.Set(cfg, SteamPad, "button_home", EdenButtonMap.None);
        map = EdenButtonMap.Read(cfg);
        Assert.Equal("dup", map["button_zl"]);
        Assert.Equal(EdenButtonMap.None, map["button_home"]);
        Assert.Null(EdenButtonMap.PresetOf(map));
    }

    [Fact]
    public void ButtonMapLeavesWhatItCannotShowAlone()
    {
        Assert.Equal(EdenButtonMap.Other, EdenButtonMap.InputOf("engine:keyboard,code:67,toggle:0"));
        Assert.Equal(EdenButtonMap.Other, EdenButtonMap.InputOf("engine:sdl,port:0,guid:x,button:14"));
        Assert.Equal(EdenButtonMap.Other, EdenButtonMap.InputOf("engine:sdl,port:0,guid:x,axis:2,threshold:0.5,invert:-"));
        Assert.Equal("ddown", EdenButtonMap.InputOf("\"engine:sdl,port:0,guid:x,hat:0,direction:down\""));
        Assert.Equal(EdenButtonMap.None, EdenButtonMap.InputOf("[empty]"));
        foreach (var input in EdenButtonMap.Inputs)
        {
            Assert.Equal(input.Id, EdenButtonMap.InputOf(EdenButtonMap.ParamFor(input.Id, "g")));
        }
        Assert.Throws<InvalidOperationException>(() =>
            EdenButtonMap.Set(Path.Combine(_dir, "x.ini"), new PadInfo("03000000aaaa0000bbbb000001000000", "Some Pad", 0xaaaa, 0xbbbb), "button_a", "south"));
    }

    private LibraryGame Game(string lib, string other)
    {
        GameEntry E(string path, ContentKind kind, string? ver = null) =>
            new() { BackendId = "eden", TitleId = "0100000000010000", Title = "G", Path = path, Kind = kind, Version = ver };
        foreach (var f in new[] { Path.Combine(lib, "g.nsp"), Path.Combine(lib, "u.nsp"), Path.Combine(other, "d.nsp") })
        {
            File.WriteAllText(f, "x");
        }
        return new LibraryGame(E(Path.Combine(lib, "g.nsp"), ContentKind.Base),
            [E(Path.Combine(lib, "u.nsp"), ContentKind.Update, "196608")],
            [E(Path.Combine(other, "d.nsp"), ContentKind.Dlc)]);
    }

    [Fact]
    public void LaunchStagesOnlyThisGamesAddOnsAndDropsLibraryFolders()
    {
        var cfg = Path.Combine(_dir, "qt-config.ini");
        var mine = Directory.CreateDirectory(Path.Combine(_dir, "mine")).FullName;
        var lib = Directory.CreateDirectory(Path.Combine(_dir, "lib")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(_dir, "other")).FullName;
        var staging = Path.Combine(_dir, "staging");
        var game = Game(lib, other);
        // 0.2.3 appended the library folder after the user's own entry.
        File.WriteAllText(cfg, $"[UI]\nPaths\\external_content_dirs\\1\\path={mine}\nPaths\\external_content_dirs\\2\\path={lib}\nPaths\\external_content_dirs\\size=2\n");

        Assert.True(EdenAddOns.PrepareLaunch(cfg, staging, game, enabled: true, [lib]));
        Assert.Equal([mine, staging], EdenAddOns.ExternalContentDirs(QtIni.Load(cfg)));
        Assert.Equal(["d.nsp", "u.nsp"], Directory.GetFiles(staging).Select(Path.GetFileName).Order());
        Assert.Equal(Path.Combine(lib, "u.nsp"), new FileInfo(Path.Combine(staging, "u.nsp")).LinkTarget);

        // Turned off for the game: staging is emptied, config already right.
        Assert.False(EdenAddOns.PrepareLaunch(cfg, staging, game, enabled: false, [lib]));
        Assert.Empty(Directory.GetFiles(staging));
        Assert.True(File.Exists(Path.Combine(lib, "u.nsp")));  // the real file is untouched
    }

    [Fact]
    public void AddOnStatusReflectsToggleAndDisabledList()
    {
        var cfg = Path.Combine(_dir, "qt-config.ini");
        var lib = Directory.CreateDirectory(Path.Combine(_dir, "lib")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(_dir, "other")).FullName;
        var game = Game(lib, other);
        var staging = Path.Combine(_dir, "staging");

        File.WriteAllText(cfg, "[UI]\n");
        var s = EdenAddOns.Status(cfg, game, enabled: true, staging);
        Assert.Equal(("Update 3 (v196608)", AddOnState.Active), (s[0].Label, s[0].State));
        Assert.Equal(("1 DLC", AddOnState.Active), (s[1].Label, s[1].State));
        s = EdenAddOns.Status(cfg, game, enabled: false, staging);
        Assert.Equal(AddOnState.NotLoaded, s[0].State);

        var tid = DisabledAddOns.ParseTitleId("0100000000010000");
        File.WriteAllText(cfg, $"[DisabledAddOns]\n1\\title_id={tid}\n1\\disabled\\1\\d=Update@196608\n1\\disabled\\size=1\nsize=1\n");
        s = EdenAddOns.Status(cfg, game, enabled: true, staging);
        Assert.Equal(AddOnState.Disabled, s[0].State);
        Assert.Equal(AddOnState.Active, s[1].State);
    }

    [Fact]
    public void ControllerStyleAndProfilesUseEdensKeys()
    {
        var cfg = Path.Combine(_dir, "qt-config.ini");
        var input = Path.Combine(_dir, "input");
        var custom = Path.Combine(_dir, "custom", "0100000000010000.ini");
        File.WriteAllText(cfg, "[Controls]\nplayer_0_button_a\\default=false\nplayer_0_button_a=\"engine:sdl,port:0,button:1\"\nplayer_0_connected=true\n");

        Assert.Equal(ControllerStyle.ProController, EdenControls.PlayerStyle(cfg));
        EdenControls.SetPlayerStyle(cfg, ControllerStyle.Handheld);
        Assert.Equal(ControllerStyle.Handheld, EdenControls.PlayerStyle(cfg));
        Assert.Equal("false", QtIni.Load(cfg).Get("Controls", "player_0_type\\default"));

        EdenControls.SaveProfile(cfg, input, "Handheld");
        Assert.Equal(["Handheld"], EdenControls.Profiles(input));
        var profile = QtIni.Load(Path.Combine(input, "Handheld.ini"));
        Assert.Equal("engine:sdl,port:0,button:1", profile.Get("Controls", "button_a"));
        Assert.Equal("4", profile.Get("Controls", "type"));
        Assert.Null(profile.Get("Controls", "connected"));
        Assert.Throws<ArgumentException>(() => EdenControls.SaveProfile(cfg, input, "../evil"));

        EdenControls.SetGameProfile(custom, "Handheld");
        Assert.Equal("Handheld", EdenControls.GameProfile(custom));
        EdenControls.SetGameProfile(custom, null);
        Assert.Null(EdenControls.GameProfile(custom));
    }
}
