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

    [Fact]
    public void UnknownPadLayoutsAreLeftAlone()
    {
        var cfg = Path.Combine(_dir, "qt-config.ini");
        File.WriteAllText(cfg, "[Controls]\n");
        Assert.False(EdenControllerDefaults.EnsurePlayer1(cfg, new PadInfo("03000000aaaa0000bbbb000001000000", "Some Pad", 0xaaaa, 0xbbbb)));
        Assert.Equal("[Controls]\n", File.ReadAllText(cfg));
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
}
