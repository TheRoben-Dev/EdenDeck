using EdenDeck.Core.Input;
using Xunit;

namespace EdenDeck.Core.Tests;

public sealed class GamepadScanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "scanline-pads-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string ClassInput => Path.Combine(_root, "class", "input");

    // A gamepad's capabilities: ABS_X/ABS_Y and BTN_SOUTH (0x130) are what makes it a joystick.
    private const string PadAbs = "3003f";
    private const string PadKey = "7fff000000000000 0 0 0 0";  // bits 0x130..0x13e

    private void Device(int evt, int input, string name, int vendor, int product, int bus = 3, int version = 1, int? js = null, string abs = PadAbs, string key = PadKey)
    {
        var dev = Path.Combine(_root, "devices", $"input{input}");
        Directory.CreateDirectory(Path.Combine(dev, "id"));
        Directory.CreateDirectory(Path.Combine(dev, "capabilities"));
        File.WriteAllText(Path.Combine(dev, "name"), name + "\n");
        File.WriteAllText(Path.Combine(dev, "id", "bustype"), $"{bus:x4}\n");
        File.WriteAllText(Path.Combine(dev, "id", "vendor"), $"{vendor:x4}\n");
        File.WriteAllText(Path.Combine(dev, "id", "product"), $"{product:x4}\n");
        File.WriteAllText(Path.Combine(dev, "id", "version"), $"{version:x4}\n");
        File.WriteAllText(Path.Combine(dev, "capabilities", "abs"), abs + "\n");
        File.WriteAllText(Path.Combine(dev, "capabilities", "key"), key + "\n");
        if (js is { } j)
        {
            Directory.CreateDirectory(Path.Combine(dev, $"js{j}"));
        }
        var ev = Path.Combine(ClassInput, $"event{evt}");
        Directory.CreateDirectory(ev);
        Directory.CreateSymbolicLink(Path.Combine(ev, "device"), dev);
    }

    private GamepadScan.Source Source(Dictionary<string, string> env, string? dmi = "Galileo") => new(ClassInput, k => env.GetValueOrDefault(k), dmi);

    /// <summary>A docked Steam Deck in Game Mode with an Xbox pad: what Steam hands a game it launches.</summary>
    private Dictionary<string, string> DockedDeck()
    {
        // Physical devices (Steam tells SDL to ignore them) ...
        Device(5, 7, "Steam Deck", 0x28de, 0x1205, js: 0);
        Device(6, 8, "Steam Deck Motion Sensors", 0x28de, 0x1205, abs: "3f", key: "0");
        Device(20, 31, "Xbox Wireless Controller", 0x045e, 0x0b13, bus: 5, js: 1);
        // ... and Steam Input's virtual pads, one per slot. The pad connected later got an input
        // number that sorts first as text: SDL still orders by slot.
        Device(18, 29, "Microsoft X-Box 360 pad 0", 0x28de, 0x11ff, js: 2);
        Device(21, 102, "Microsoft X-Box 360 pad 1", 0x28de, 0x11ff, js: 3);
        var info = Path.Combine(_root, "SteamVirtualGamepadInfo.txt");
        File.WriteAllText(info, "[slot 0]\nVID=0x28de\nPID=0x1205\ntype=steamdeck\nname=Steam Deck\nhandle=1\n[slot 1]\nVID=0x45e\nPID=0xb13\ntype=xboxone\nname=Xbox Series X Controller\nhandle=2\n");
        return new()
        {
            ["SteamVirtualGamepadInfo"] = info,
            ["SDL_GAMECONTROLLER_ALLOW_STEAM_VIRTUAL_GAMEPAD"] = "1",
            ["SDL_GAMECONTROLLER_IGNORE_DEVICES"] = "0x28DE/0x1205,0x045E/0x0B13",
        };
    }

    [Fact]
    public void DockedDeckGivesPlayer1ToTheControllerInTheDock()
    {
        var pads = GamepadScan.Scan(Source(DockedDeck()));
        Assert.Equal(["Steam Deck", "Xbox Series X Controller"], pads.Select(p => p.Name));
        Assert.True(pads[0].BuiltIn);
        Assert.False(pads[1].BuiltIn);
        Assert.Equal([0, 1], pads.Select(p => p.SteamSlot!.Value));
        Assert.Equal("03000000de280000ff11000001000000", pads[1].Guid);
        Assert.Equal(1, pads[1].GuidPort);
        Assert.Equal(1, pads[1].GamepadIndex);
        Assert.Equal((0x045e, 0x0b13), (pads[1].Vendor, pads[1].Product));

        var players = GamepadScan.Players(pads);
        Assert.Equal(["Xbox Series X Controller"], players.Select(p => p.Name));
    }

    [Fact]
    public void BuiltInControlsArePlayer1WhenNothingElseIsConnected()
    {
        Device(5, 7, "Steam Deck", 0x28de, 0x1205, js: 0);
        Device(18, 29, "Microsoft X-Box 360 pad 0", 0x28de, 0x11ff, js: 1);
        var info = Path.Combine(_root, "info.txt");
        File.WriteAllText(info, "[slot 0]\nVID=0x28de\nPID=0x12fd\nname=#SettingsController_SteamController\n");
        var env = new Dictionary<string, string>
        {
            ["SteamVirtualGamepadInfo"] = info,
            ["SDL_GAMECONTROLLER_ALLOW_STEAM_VIRTUAL_GAMEPAD"] = "1",
            ["SDL_GAMECONTROLLER_IGNORE_DEVICES"] = "0x28de/0x1205",
        };
        var players = GamepadScan.Players(GamepadScan.Scan(Source(env)));
        var only = Assert.Single(players);
        Assert.True(only.BuiltIn);
        Assert.Equal(0, only.GuidPort);
    }

    [Fact]
    public void MoreControllersArePlayers2AndUpInConnectionOrder()
    {
        var env = DockedDeck();
        Device(22, 40, "Microsoft X-Box 360 pad 2", 0x28de, 0x11ff, js: 4);
        File.AppendAllText(env["SteamVirtualGamepadInfo"], "[slot 2]\nVID=0x54c\nPID=0xce6\nname=DualSense Wireless Controller\n");
        var players = GamepadScan.Players(GamepadScan.Scan(Source(env)));
        Assert.Equal(["Xbox Series X Controller", "DualSense Wireless Controller"], players.Select(p => p.Name));
        Assert.Equal([1, 2], players.Select(p => p.GuidPort));
    }

    [Fact]
    public void WithoutSteamRawPadsAreUsedAndTheDeckIsListedFirst()
    {
        // Desktop mode, Scanline started outside Steam: SDL hides Steam Input pads and reads the
        // hardware itself; its HIDAPI driver takes the Deck's controls before evdev pads.
        Device(5, 7, "Steam Deck", 0x28de, 0x1205, js: 0);
        Device(18, 29, "Microsoft X-Box 360 pad 0", 0x28de, 0x11ff, js: 1);
        Device(20, 31, "Xbox Wireless Controller", 0x045e, 0x0b13, bus: 5, js: 2);
        var pads = GamepadScan.Scan(Source(new()));
        Assert.Equal(["Steam Deck", "Xbox Wireless Controller"], pads.Select(p => p.Name));
        Assert.Equal([0, 1], pads.Select(p => p.GamepadIndex));
        Assert.Equal([0, 2], pads.Select(p => p.JoystickIndex));  // melonDS's SDL2 joystick list still has the Steam pad
        Assert.Equal("05000000" + "5e040000" + "130b0000" + "01000000", pads[1].Guid);
        var player = Assert.Single(GamepadScan.Players(pads));
        Assert.Equal("Xbox Wireless Controller", player.Name);
    }

    [Fact]
    public void NotADeckMeansNoBuiltInControls()
    {
        Device(18, 29, "Microsoft X-Box 360 pad 0", 0x28de, 0x11ff);
        var info = Path.Combine(_root, "info.txt");
        File.WriteAllText(info, "[slot 0]\nVID=0x28de\nPID=0x1102\nname=Steam Controller\n");
        var pads = GamepadScan.Scan(Source(new() { ["SteamVirtualGamepadInfo"] = info, ["SDL_GAMECONTROLLER_ALLOW_STEAM_VIRTUAL_GAMEPAD"] = "1" }, dmi: "B550 AORUS"));
        Assert.False(Assert.Single(pads).BuiltIn);
    }

    [Fact]
    public void NoSysfsMeansNoPads()
    {
        Assert.Empty(GamepadScan.Scan(new GamepadScan.Source(Path.Combine(_root, "missing"), _ => null, null)));
        Assert.Empty(GamepadScan.Players([]));
    }

    [Fact]
    public void ParsesSlotsAndIgnoreLists()
    {
        Assert.Equal(3, GamepadScan.SlotFromName("Microsoft X-Box 360 pad 3"));
        Assert.Null(GamepadScan.SlotFromName("Steam Deck"));
        Assert.Equal([(0x28de, 0x1205), (0x45e, 0xb13)], GamepadScan.IgnoreList("0x28DE/0x1205, 0x045e/0x0b13,junk"));
        Assert.Equal("03000000de280000ff11000001000000", GamepadScan.SdlGuid(3, 0x28de, 0x11ff, 1));
    }
}
