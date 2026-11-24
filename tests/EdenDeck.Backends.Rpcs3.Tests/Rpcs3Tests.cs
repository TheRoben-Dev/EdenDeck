using System.Text;
using EdenDeck.Backends.Rpcs3;
using EdenDeck.Core;
using EdenDeck.Core.Launch;
using EdenDeck.Core.Profiles;
using Xunit;

namespace EdenDeck.Backends.Rpcs3.Tests;

public sealed class Rpcs3Tests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "scanline-rpcs3-" + Guid.NewGuid().ToString("N")[..10]);
    private readonly Dictionary<string, string?> _vars = new();

    public Rpcs3Tests()
    {
        Directory.CreateDirectory(_home);
        _vars["HOME"] = _home;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_home, true);
        }
        catch (IOException)
        {
        }
    }

    private string? Env(string n) => _vars.TryGetValue(n, out var v) ? v : null;

    private Rpcs3Backend Backend() => new(new AppPaths(Env), env: Env);

    /// <summary>A PARAM.SFO with UTF-8 string values.</summary>
    public static byte[] Sfo(params (string Key, string Value)[] values)
    {
        var keys = new MemoryStream();
        var data = new MemoryStream();
        var index = new MemoryStream();
        foreach (var (k, v) in values)
        {
            var bytes = Encoding.UTF8.GetBytes(v + "\0");
            var max = (bytes.Length + 3) / 4 * 4;
            index.Write(BitConverter.GetBytes((ushort)keys.Length));
            index.Write(BitConverter.GetBytes((ushort)0x0204));
            index.Write(BitConverter.GetBytes(bytes.Length));
            index.Write(BitConverter.GetBytes(max));
            index.Write(BitConverter.GetBytes((int)data.Length));
            keys.Write(Encoding.ASCII.GetBytes(k + "\0"));
            data.Write(bytes);
            data.Write(new byte[max - bytes.Length]);
        }
        while (keys.Length % 4 != 0)
        {
            keys.WriteByte(0);
        }
        var keyStart = 20 + (int)index.Length;
        var head = new MemoryStream();
        head.Write([0, (byte)'P', (byte)'S', (byte)'F', 1, 1, 0, 0]);
        head.Write(BitConverter.GetBytes(keyStart));
        head.Write(BitConverter.GetBytes(keyStart + (int)keys.Length));
        head.Write(BitConverter.GetBytes(values.Length));
        head.Write(index.ToArray());
        head.Write(keys.ToArray());
        head.Write(data.ToArray());
        return head.ToArray();
    }

    private static byte[] Record(string id, uint extent, uint size, bool dir)
    {
        var name = Encoding.ASCII.GetBytes(id);
        var len = 33 + name.Length + (name.Length % 2 == 0 ? 1 : 0);
        var r = new byte[len];
        r[0] = (byte)len;
        BitConverter.GetBytes(extent).CopyTo(r, 2);
        BitConverter.GetBytes(size).CopyTo(r, 10);
        r[25] = (byte)(dir ? 2 : 0);
        r[32] = (byte)name.Length;
        name.CopyTo(r, 33);
        return r;
    }

    /// <summary>A tiny ISO 9660 image holding PS3_GAME/PARAM.SFO.</summary>
    public static byte[] Iso(byte[] sfo)
    {
        const int s = 2048;
        var img = new byte[s * 22];
        img[16 * s] = 1;
        Encoding.ASCII.GetBytes("CD001").CopyTo(img, 16 * s + 1);
        Record("\0", 18, s, true).CopyTo(img, 16 * s + 156);
        var root = Record("\0", 18, s, true).Concat(Record("\u0001", 18, s, true)).Concat(Record("PS3_DISC.SFB;1", 21, 4, false)).Concat(Record("PS3_GAME", 19, s, true)).ToArray();
        root.CopyTo(img, 18 * s);
        Record("PARAM.SFO;1", 20, (uint)sfo.Length, false).CopyTo(img, 19 * s);
        sfo.CopyTo(img, 20 * s);
        return img;
    }

    private static byte[] GameSfo(string id = "BLUS30001", string title = "Turbo Kart™ Racing", string cat = "DG") =>
        Sfo(("CATEGORY", cat), ("TITLE", title), ("TITLE_ID", id), ("APP_VER", "01.02"));

    private string DiscFolder(string name, byte[] sfo)
    {
        var root = Path.Combine(_home, "ps3", name);
        Directory.CreateDirectory(Path.Combine(root, "PS3_GAME", "USRDIR"));
        File.WriteAllBytes(Path.Combine(root, "PS3_GAME", "PARAM.SFO"), sfo);
        File.WriteAllText(Path.Combine(root, "PS3_GAME", "USRDIR", "EBOOT.BIN"), "SCE");
        return root;
    }

    private void InstallFakeRpcs3(bool firmware = true)
    {
        var apps = Path.Combine(_home, "Applications");
        Directory.CreateDirectory(apps);
        File.WriteAllText(Path.Combine(apps, "rpcs3-v0.0.38-18400-abcdef12_linux64.AppImage"), "#!/bin/sh\n");
        if (firmware)
        {
            var vsh = Path.Combine(_home, ".config", "rpcs3", "dev_flash", "vsh", "module");
            Directory.CreateDirectory(vsh);
            File.WriteAllText(Path.Combine(vsh, "vsh.self"), "x");
        }
    }

    [Fact]
    public void Player1GetsTheControllerUnlessThePlayerSetOneUp()
    {
        var b = Backend();
        var paths = b.Paths;
        var file = Rpcs3Pads.ProfileFile(paths);
        Assert.EndsWith("input_configs/global/Default.yml", file);
        string[] pads = ["Steam Virtual Gamepad"];

        // No setup yet (RPCS3 would fall back to the keyboard): SDL with the pad the app sees.
        Assert.True(Rpcs3Pads.Ensure(paths, pads, b.PadStampFile));
        Assert.Equal(("SDL", "Steam Virtual Gamepad 1"), Rpcs3Pads.Player1(File.ReadAllText(file)));
        Assert.False(Rpcs3Pads.Ensure(paths, pads, b.PadStampFile));

        // Another pad later: Scanline's own setup follows it.
        Assert.True(Rpcs3Pads.Ensure(paths, ["Xbox Series X Controller"], b.PadStampFile));
        Assert.Equal("Xbox Series X Controller 1", Rpcs3Pads.Player1(File.ReadAllText(file)).Device);

        // RPCS3's keyboard default is replaced, its key mapping dropped, other players kept.
        File.WriteAllText(file, "Player 1 Input:\n  Handler: Keyboard\n  Device: Keyboard\n  Config:\n    Cross: Z\n    Circle: X\nPlayer 2 Input:\n  Handler: Null\n  Device: Null\n");
        File.Delete(b.PadStampFile);
        Assert.True(Rpcs3Pads.Ensure(paths, pads, b.PadStampFile));
        var text = File.ReadAllText(file);
        Assert.DoesNotContain("Cross: Z", text);
        Assert.Contains("Player 2 Input:\n  Handler: Null", text);

        // A pad the player set up in RPCS3 stays.
        File.WriteAllText(file, "Player 1 Input:\n  Handler: DualSense\n  Device: DualSense Wireless Controller 1\n");
        Assert.False(Rpcs3Pads.Ensure(paths, pads, b.PadStampFile));
        Assert.Equal("DualSense", Rpcs3Pads.Player1(File.ReadAllText(file)).Handler);

        // The active global profile is the one written.
        Directory.CreateDirectory(Path.Combine(paths.ConfigDir, "input_configs"));
        File.WriteAllText(Path.Combine(paths.ConfigDir, "input_configs", "active_input_configurations.yml"), "Active Configurations:\n  global: Couch\n");
        Assert.EndsWith("global/Couch.yml", Rpcs3Pads.ProfileFile(paths));
        Assert.False(Rpcs3Pads.Ensure(paths, [], b.PadStampFile));  // no pad, nothing to do
    }

    [Fact]
    public void FindsDiscFoldersInstalledGamesAndIsos()
    {
        var b = Backend();
        var root = DiscFolder("Turbo Kart", GameSfo());
        var disc = b.TryRead(Path.Combine(root, "PS3_GAME", "PARAM.SFO"))!;
        Assert.Equal("BLUS30001", disc.TitleId);
        Assert.Equal("Turbo Kart Racing", disc.Title);
        Assert.Equal(root, disc.Path);
        Assert.Equal("01.02", disc.Version);
        Assert.Equal(Path.Combine(root, "PS3_GAME", "USRDIR", "EBOOT.BIN"), Rpcs3Backend.BootTarget(disc));

        // A PSN game in RPCS3's own dev_hdd0/game, found without adding a folder.
        var psn = Path.Combine(_home, ".config", "rpcs3", "dev_hdd0", "game", "NPUB30002");
        Directory.CreateDirectory(Path.Combine(psn, "USRDIR"));
        File.WriteAllBytes(Path.Combine(psn, "PARAM.SFO"), GameSfo("NPUB30002", "Sky Garden", "HG"));
        File.WriteAllText(Path.Combine(psn, "USRDIR", "EBOOT.BIN"), "SCE");
        Assert.Contains(Path.Combine(_home, ".config", "rpcs3", "dev_hdd0", "game"), b.OwnFolders);
        Assert.Equal("Sky Garden", b.TryRead(Path.Combine(psn, "PARAM.SFO"))!.Title);

        // Updates, saves and DLC have a PARAM.SFO too, but aren't games.
        var patch = Path.Combine(_home, ".config", "rpcs3", "dev_hdd0", "game", "BLUS30001");
        Directory.CreateDirectory(Path.Combine(patch, "USRDIR"));
        File.WriteAllBytes(Path.Combine(patch, "PARAM.SFO"), GameSfo(cat: "GD"));
        File.WriteAllText(Path.Combine(patch, "USRDIR", "EBOOT.BIN"), "SCE");
        Assert.Null(b.TryRead(Path.Combine(patch, "PARAM.SFO")));

        var iso = Path.Combine(_home, "ps3", "Moon Base.iso");
        File.WriteAllBytes(iso, Iso(GameSfo("BCES00003", "Moon Base")));
        var fromIso = b.TryRead(iso)!;
        Assert.Equal("BCES00003", fromIso.TitleId);
        Assert.Equal(iso, Rpcs3Backend.BootTarget(fromIso));
        File.WriteAllBytes(Path.Combine(_home, "ps3", "other.iso"), new byte[40000]);
        Assert.Null(b.TryRead(Path.Combine(_home, "ps3", "other.iso")));
    }

    [Fact]
    public void LaunchesWithoutGuiFullscreenAndNeedsFirmware()
    {
        InstallFakeRpcs3(firmware: false);
        var b = Backend();
        Assert.Equal("0.0.38-18400", b.Install.Current!.Version);
        var game = b.TryRead(Path.Combine(DiscFolder("Turbo Kart", GameSfo()), "PS3_GAME", "PARAM.SFO"))!;
        var e = Assert.Throws<InvalidOperationException>(() => b.Launch.Plan(game, new GameProfile(), new LaunchOptions()));
        Assert.Contains("PS3UPDAT.PUP", e.Message);
        InstallFakeRpcs3();
        var plan = b.Launch.Plan(game, new GameProfile(), new LaunchOptions());
        Assert.EndsWith(".AppImage", plan.Executable);
        Assert.Equal(["--no-gui", "--fullscreen", Rpcs3Backend.BootTarget(game)], plan.Arguments);
    }

    private sealed class FakeExtras : ILaunchExtras
    {
        public void Apply(GameProfile profile, LaunchOptions options, Dictionary<string, string> env, List<Action> prepare, List<string> notes) =>
            env["VK_INSTANCE_LAYERS"] = "VK_LAYER_SCANLINE_overlay";
    }

    [Fact]
    public void GetsFrameGenAndTheLayerLikeEden()
    {
        InstallFakeRpcs3();
        var b = Backend();
        var game = b.TryRead(Path.Combine(DiscFolder("Turbo Kart", GameSfo()), "PS3_GAME", "PARAM.SFO"))!;
        var plan = b.Launch.Plan(game, new GameProfile { FrameGen = FrameGenMode.Builtin, NativeFps = 30 }, new LaunchOptions { Vulkan = new FakeExtras() });
        Assert.Equal("VK_LAYER_SCANLINE_overlay", plan.Environment["VK_INSTANCE_LAYERS"]);
    }

    [Fact]
    public void YamlEditsKeepRpcs3sOwnLines()
    {
        var y = Rpcs3Yaml.Parse("Core:\n  PPU Decoder: Recompiler (LLVM)\nVideo:\n  Renderer: Vulkan\n  Resolution Scale: 100\n  Vulkan:\n    Adapter: \"\"\nMiscellaneous:\n  Show trophy popups: true\n");
        Assert.Equal("100", y.Get("Video/Resolution Scale"));
        Assert.Equal("", y.Get("Video/Vulkan/Adapter"));
        y.Set("Video/Resolution Scale", "150");
        y.Set("Video/Output Scaling Mode", "FidelityFX Super Resolution");
        y.Set("Audio/Renderer", "Cubeb");
        y.Set("Video/Vulkan/Asynchronous Texture Streaming", "true");
        Assert.Equal("Core:\n  PPU Decoder: Recompiler (LLVM)\nVideo:\n  Renderer: Vulkan\n  Resolution Scale: 150\n  Vulkan:\n    Adapter: \"\"\n    Asynchronous Texture Streaming: true\n  Output Scaling Mode: FidelityFX Super Resolution\nMiscellaneous:\n  Show trophy popups: true\nAudio:\n  Renderer: Cubeb\n", y.ToString());
        y.Remove("Audio/Renderer");
        Assert.DoesNotContain("Audio", y.ToString());
        Assert.Equal("$(EmulatorDir)dev_hdd0/", Rpcs3Yaml.Parse("$(EmulatorDir): \"\"\n/dev_hdd0/: $(EmulatorDir)dev_hdd0/\n").GetRoot("/dev_hdd0/"));
    }

    [Fact]
    public void PerGameSettingsAreRpcs3CustomConfigs()
    {
        var b = Backend();
        b.Settings.WriteGlobal(new Dictionary<string, string?> { ["Video/Frame limit"] = "Auto" });
        Assert.Contains("Video:\n  Frame limit: Auto\n", File.ReadAllText(b.Paths.ConfigFile));
        Rpcs3Settings.Performance.Apply(b.Settings, "BLUS30001", "tv");
        var custom = b.Paths.CustomConfigFile("BLUS30001");
        Assert.Equal(Path.Combine(_home, ".config", "rpcs3", "custom_configs", "config_BLUS30001.yml"), custom);
        Assert.Equal("150", b.Settings.ReadPerGame("BLUS30001")["Video/Resolution Scale"]);
        Assert.Equal("tv", Rpcs3Settings.Performance.Current(b.Settings.ReadPerGame("BLUS30001"), b.Settings.Options));
        Rpcs3Settings.Performance.Apply(b.Settings, "BLUS30001", Core.Settings.PresetSet.Global);
        Assert.False(File.Exists(custom));  // nothing left: RPCS3 uses the global config again
        Assert.Throws<ArgumentException>(() => b.Settings.WritePerGame("X", new Dictionary<string, string?> { ["Core/PPU Decoder"] = "x" }));
    }

    [Fact]
    public void SavesFollowVfsAndRoundTrip()
    {
        var b = Backend();
        // dev_hdd0 moved to another drive in vfs.yml.
        var hdd = Path.Combine(_home, "sd", "dev_hdd0");
        Directory.CreateDirectory(Path.Combine(_home, ".config", "rpcs3"));
        File.WriteAllText(b.Paths.VfsFile, $"$(EmulatorDir): \"\"\n/dev_hdd0/: {hdd}/\n");
        Assert.Equal(hdd, b.Paths.DevHdd0);
        var game = b.TryRead(Path.Combine(DiscFolder("Turbo Kart", GameSfo()), "PS3_GAME", "PARAM.SFO"))!;
        var slot = Path.Combine(hdd, "home", "00000001", "savedata", "BLUS30001-SAVE00");
        Directory.CreateDirectory(slot);
        File.WriteAllText(Path.Combine(slot, "DATA.BIN"), "lap 1");
        var other = Path.Combine(hdd, "home", "00000001", "savedata", "BLUS39999-SAVE");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "DATA.BIN"), "other");
        Assert.Equal("home/00000001/savedata/BLUS30001-SAVE00", Assert.Single(b.Saves.Saves(game)).Relative);
        var backup = b.Saves.Backup(game)!;
        File.WriteAllText(Path.Combine(slot, "DATA.BIN"), "lap 9");
        b.Saves.Restore(game, backup.Path);
        Assert.Equal("lap 1", File.ReadAllText(Path.Combine(slot, "DATA.BIN")));
        b.Saves.Delete(game);
        Assert.False(Directory.Exists(slot));
        Assert.True(Directory.Exists(other));
    }

    [Fact]
    public void InstallsOnlyOfficialBuildsFromRpcs3sUpdateService()
    {
        var json = """
        {"return_code":-1,"latest_build":{"pr":17000,"datetime":"2026-11-20 10:00:00","version":"0.0.38-18400",
          "windows":{"download":"https://github.com/RPCS3/rpcs3-binaries-win/releases/download/build-x/rpcs3.7z","size":1,"checksum":"AA"},
          "linux":{"download":"https://github.com/RPCS3/rpcs3-binaries-linux/releases/download/build-x/rpcs3-v0.0.38-18400-x_linux64.AppImage","size":123,"checksum":"ABCD"}}}
        """;
        var a = Rpcs3InstallManager.ToAvailable(json)!;
        Assert.Equal("0.0.38-18400", a.Version);
        Assert.Equal("abcd", a.Sha256);
        Assert.True(Rpcs3InstallManager.IsOfficial(a.DownloadUrl));
        Assert.False(Rpcs3InstallManager.IsOfficial("https://example.com/rpcs3.AppImage"));
        Assert.False(Rpcs3InstallManager.IsOfficial("https://github.com/someone/rpcs3-binaries-linux/x.AppImage"));
        Assert.Throws<InvalidDataException>(() => Rpcs3InstallManager.ToAvailable("{\"return_code\":-2}"));
    }

    [Fact]
    public void UpdateRequestPassesTheServicesChecks()
    {
        // update.rpcs3.net answers -3 ("illegal search") for a build hash ("c") that isn't 7+
        // alphanumeric characters, and for a v3 request without os_type and os_arch.
        var url = new Uri(string.Format(System.Globalization.CultureInfo.InvariantCulture, Rpcs3InstallManager.ReleaseFeed, "x64"));
        var q = url.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "");
        Assert.Equal("v3", q["api"]);
        Assert.Equal("linux", q["os_type"]);
        Assert.Equal("x64", q["os_arch"]);
        if (q.TryGetValue("c", out var c))
        {
            Assert.True(c.Length >= 7 && c.All(char.IsAsciiLetterOrDigit), $"c={c} would be rejected");
        }
    }

    [Fact]
    public void FirmwareIsTheUsersOwnPup()
    {
        InstallFakeRpcs3(firmware: false);
        var b = Backend();
        Assert.False(b.Status().Single().Present);
        var notPup = Path.Combine(_home, "PS3UPDAT.PUP");
        File.WriteAllBytes(notPup, new byte[2 << 20]);
        Assert.Throws<InvalidDataException>(() => b.FirmwareInstallCommand(notPup));
        var pup = new byte[2 << 20];
        Encoding.ASCII.GetBytes("SCEUF").CopyTo(pup, 0);
        File.WriteAllBytes(notPup, pup);
        var (exe, args) = b.FirmwareInstallCommand(notPup);
        Assert.EndsWith(".AppImage", exe);
        Assert.Equal(["--installfw", notPup], args);
    }

    private const string PatchYml = """
        Version: 1.2

        Anchors:
          anchor_x: &anchor_x
            - [ be32, 0x10, 0x60000000 ]

        PPU-1111111111111111111111111111111111111111:
          "60 FPS":
            Games:
              "Turbo Kart Racing":
                BLUS30001: [ 01.00, 01.02 ]
                BLES00001: [ 01.00 ]
            Author: "Someone"
            Notes: "Needs a fast CPU: game: speed"
            Patch Version: 1.0
            Patch:
              - [ be32, 0x00123456, 0x60000000 ]
          Disable Motion Blur:
            Games:
              "Turbo Kart Racing":
                BLES00001: [ 01.00 ]
            Patch:
              - [ load, *anchor_x ]

        PPU-2222222222222222222222222222222222222222:
          "60 FPS":
            Games:
              "Turbo Kart Racing":
                BLUS30001: [ 01.02 ]
            Patch:
              - [ be32, 0x1, 0x2 ]
        """;

    [Fact]
    public void PatchesComeFromRpcs3sListAndUseItsConfig()
    {
        var b = Backend();
        var patches = Rpcs3Patches.ParsePatches(PatchYml, "BLUS30001");
        var fps = Assert.Single(patches);
        Assert.Equal("60 FPS", fps.Name);
        Assert.Equal("Needs a fast CPU: game: speed", fps.Notes);
        Assert.Equal(3, fps.Targets.Count);  // two versions on one hash, one on another
        Assert.Equal(2, Rpcs3Patches.ParsePatches(PatchYml, "BLES00001").Count);

        Directory.CreateDirectory(Path.GetDirectoryName(b.Patches.PatchFile)!);
        File.WriteAllText(b.Patches.PatchFile, PatchYml);
        // A patch config RPCS3 already wrote, with a configurable value Scanline must keep.
        File.WriteAllText(b.Patches.ConfigFile, "PPU-9999:\n  Other:\n    Other Game:\n      BLUS99999:\n        01.00:\n          Enabled: true\n          Configurable Values:\n            Speed: 2\n");
        var p = b.Patches.ForGame("BLUS30001").Single();
        Assert.False(b.Patches.IsEnabled(p));
        b.Patches.SetEnabled(p, true);
        Assert.True(b.Patches.IsEnabled(p));
        Assert.Equal(1, b.Patches.EnabledCount("BLUS30001"));
        var text = File.ReadAllText(b.Patches.ConfigFile);
        Assert.Contains("PPU-1111111111111111111111111111111111111111:\n  \"60 FPS\":\n    \"Turbo Kart Racing\":\n      \"BLUS30001\":\n        \"01.00\":\n          Enabled: true\n        \"01.02\":\n          Enabled: true\n", text);
        Assert.Contains("          Configurable Values:\n            Speed: 2\n", text);
        b.Patches.SetEnabled(p, false);
        Assert.False(b.Patches.IsEnabled(p));
        Assert.DoesNotContain("PPU-1111", File.ReadAllText(b.Patches.ConfigFile));
        Assert.Contains("Speed: 2", File.ReadAllText(b.Patches.ConfigFile));
    }

    [Fact]
    public void PatchDownloadsAreChecked()
    {
        var content = "Version: 1.2\n";
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        Assert.Equal(content, Rpcs3Patches.ParseFeed($"{{\"return_code\":0,\"version\":\"1.2\",\"sha256\":\"{sha}\",\"patch\":\"Version: 1.2\\n\"}}"));
        Assert.Null(Rpcs3Patches.ParseFeed("{\"return_code\":1}"));
        Assert.Throws<InvalidDataException>(() => Rpcs3Patches.ParseFeed($"{{\"return_code\":0,\"version\":\"1.2\",\"sha256\":\"00\",\"patch\":\"x\"}}"));
        Assert.Throws<InvalidDataException>(() => Rpcs3Patches.ParseFeed("{\"return_code\":-2}"));
    }

    [Fact]
    public void IconComesFromTheFolderOrTheIso()
    {
        var b = Backend();
        var root = DiscFolder("Turbo Kart", GameSfo());
        var game = b.TryRead(Path.Combine(root, "PS3_GAME", "PARAM.SFO"))!;
        Assert.Null(b.IconFor(game));
        File.WriteAllBytes(Path.Combine(root, "PS3_GAME", "ICON0.PNG"), [0x89, 0x50]);
        Assert.Equal(Path.Combine(root, "PS3_GAME", "ICON0.PNG"), b.IconFor(game));
    }
}
