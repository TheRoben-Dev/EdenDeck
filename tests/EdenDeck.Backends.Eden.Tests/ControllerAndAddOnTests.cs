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

    [Fact]
    public void ExternalContentDirsAreAppendOnly()
    {
        var cfg = Path.Combine(_dir, "qt-config.ini");
        var mine = Directory.CreateDirectory(Path.Combine(_dir, "mine")).FullName;
        var lib = Directory.CreateDirectory(Path.Combine(_dir, "lib")).FullName;
        File.WriteAllText(cfg, $"[UI]\nPaths\\external_content_dirs\\1\\path={mine}\nPaths\\external_content_dirs\\size=1\n");

        Assert.True(EdenAddOns.EnsureExternalContentDirs(cfg, [lib, mine, Path.Combine(_dir, "missing")]));
        Assert.Equal([mine, lib], EdenAddOns.ExternalContentDirs(QtIni.Load(cfg)));
        Assert.False(EdenAddOns.EnsureExternalContentDirs(cfg, [lib + "/"]));
    }

    [Fact]
    public void AddOnStatusReflectsFoldersAndDisabledList()
    {
        var cfg = Path.Combine(_dir, "qt-config.ini");
        var lib = Directory.CreateDirectory(Path.Combine(_dir, "lib", "Game")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(_dir, "other")).FullName;
        GameEntry E(string path, ContentKind kind, string? ver = null) =>
            new() { BackendId = "eden", TitleId = "0100000000010000", Title = "G", Path = path, Kind = kind, Version = ver };
        var game = new LibraryGame(E(Path.Combine(lib, "g.nsp"), ContentKind.Base),
            [E(Path.Combine(lib, "u.nsp"), ContentKind.Update, "196608")],
            [E(Path.Combine(other, "d.nsp"), ContentKind.Dlc)]);

        File.WriteAllText(cfg, "[UI]\n");
        var s = EdenAddOns.Status(cfg, game, [Path.Combine(_dir, "lib")]);
        Assert.Equal(("Update 3 (v196608)", AddOnState.Active), (s[0].Label, s[0].State));
        Assert.Equal(("1 DLC", AddOnState.NotLoaded), (s[1].Label, s[1].State));

        var tid = DisabledAddOns.ParseTitleId("0100000000010000");
        File.WriteAllText(cfg, $"[DisabledAddOns]\n1\\title_id={tid}\n1\\disabled\\1\\d=Update@196608\n1\\disabled\\size=1\nsize=1\n");
        s = EdenAddOns.Status(cfg, game, [Path.Combine(_dir, "lib"), other]);
        Assert.Equal(AddOnState.Disabled, s[0].State);
        Assert.Equal(AddOnState.Active, s[1].State);
    }
}
