using EdenDeck.Core.Config;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using EdenDeck.Backends.Duckstation;
using EdenDeck.Backends.Pcsx2;
using EdenDeck.Backends.Pcsx2.Tests;
using EdenDeck.Core;
using EdenDeck.Core.Backends;
using EdenDeck.Core.Launch;
using EdenDeck.Core.Profiles;
using Xunit;

namespace EdenDeck.Backends.Xemu.Tests;

public sealed class XemuTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "scanline-xemu-" + Guid.NewGuid().ToString("N")[..10]);
    private readonly Dictionary<string, string?> _vars = new();

    public XemuTests()
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

    private XemuBackend Backend() => new(new AppPaths(Env), env: Env);

    private string WriteFile(string relative, byte[] bytes)
    {
        var path = Path.Combine(_home, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>A minimal XBE: header, then a certificate with the title id and name.</summary>
    public static byte[] Xbe(uint titleId, string name)
    {
        const uint baseAddr = 0x10000;
        var xbe = new byte[0x1000];
        Encoding.ASCII.GetBytes("XBEH").CopyTo(xbe, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(xbe.AsSpan(0x104), baseAddr);
        BinaryPrimitives.WriteUInt32LittleEndian(xbe.AsSpan(0x118), baseAddr + 0x400);
        BinaryPrimitives.WriteUInt32LittleEndian(xbe.AsSpan(0x400 + 0x08), titleId);
        Encoding.Unicode.GetBytes(name).CopyTo(xbe, 0x400 + 0x0C);
        BinaryPrimitives.WriteUInt32LittleEndian(xbe.AsSpan(0x400 + 0xA0), 1);
        return xbe;
    }

    /// <summary>A minimal XISO: the volume descriptor at sector 32, a root directory with a few files, default.xbe among them.</summary>
    public static byte[] Xiso(uint titleId, string name, long partition = 0)
    {
        const int s = XboxDisc.Sector;
        var xbe = Xbe(titleId, name);
        var image = new byte[partition + 36L * s + xbe.Length];
        var p = (int)partition;
        Encoding.ASCII.GetBytes(XboxDisc.Magic).CopyTo(image, p + 32 * s);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(p + 32 * s + 20), 33);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(p + 32 * s + 24), s);
        Encoding.ASCII.GetBytes(XboxDisc.Magic).CopyTo(image, p + 32 * s + 0x7EC);
        // Root: "Audio" (dir) at 0 with right → "default.xbe" at 20 bytes (5 dwords) with left → "media" at 48 (12 dwords).
        var dirAt = p + 33 * s;
        image.AsSpan(dirAt, s).Fill(0xFF);
        void Entry(int at, ushort left, ushort right, uint sector, uint size, byte attr, string n)
        {
            var dir = image.AsSpan(dirAt, s);
            BinaryPrimitives.WriteUInt16LittleEndian(dir[at..], left);
            BinaryPrimitives.WriteUInt16LittleEndian(dir[(at + 2)..], right);
            BinaryPrimitives.WriteUInt32LittleEndian(dir[(at + 4)..], sector);
            BinaryPrimitives.WriteUInt32LittleEndian(dir[(at + 8)..], size);
            dir[at + 12] = attr;
            dir[at + 13] = (byte)n.Length;
            Encoding.ASCII.GetBytes(n).CopyTo(dir[(at + 14)..]);
        }
        Entry(0, 0, 5, 34, 0, 0x10, "Audio");
        Entry(20, 12, 0, 36, (uint)xbe.Length, 0x20, "default.xbe");
        Entry(48, 0, 0, 35, 16, 0x20, "media");
        xbe.CopyTo(image, p + 36 * s);
        return image;
    }

    // ---- discs ----

    [Fact]
    public void ReadsTheTitleIdAndNameFromAnXiso()
    {
        var info = XboxDisc.Read(WriteFile("g/halo.iso", Xiso(0x4D530004, "Halo")))!;
        Assert.Equal("4D530004", info.Hex);
        Assert.Equal("MS-004", info.Serial);
        Assert.Equal("Halo", info.Name);
    }

    [Fact]
    public void FindsTheGamePartitionOfAFullDump()
    {
        using var f = File.Create(Path.Combine(_home, "full.iso"));
        var part = XboxDisc.PartitionOffsets[3];
        f.SetLength(part);  // sparse: the video partition
        f.Position = part;
        f.Write(Xiso(0x4D530004, "Halo"));
        f.Flush();
        f.Position = 0;
        Assert.Equal(part, XboxDisc.FindPartition(f));
    }

    [Fact]
    public void LibraryNamesGamesFromRedumpThenTheXbe()
    {
        var b = Backend();
        var halo = b.TryRead(WriteFile("g/a.iso", Xiso(0x4D530004, "halo")))!;
        Assert.Equal("4D530004", halo.TitleId);
        Assert.Equal("Halo - Combat Evolved", halo.Title);
        Assert.Equal("Zylo Drift", b.TryRead(WriteFile("g/b.xiso", Xiso(0x5A590001, "Zylo Drift")))!.Title);
        Assert.Null(b.TryRead(WriteFile("g/notes.iso", new byte[100 << 10])));
    }

    [Fact]
    public void XboxAndPlayStationReadersIgnoreEachOthersDiscs()
    {
        var b = Backend();
        var app = new AppPaths(Env);
        var xiso = WriteFile("g/x.iso", Xiso(0x4D530004, "Halo"));
        var ps2 = WriteFile("g/p.iso", Pcsx2Tests.Iso());
        Assert.Null(b.TryRead(ps2));
        Assert.Null(new Pcsx2Backend(app, env: Env).TryRead(xiso));
        Assert.Null(new DuckstationBackend(app, env: Env).TryRead(xiso));
    }

    [Fact]
    public void SerialNeedsLettersOrDigits() => Assert.Null(XboxDisc.Serial(0x00010002));

    // ---- config ----

    [Fact]
    public void TomlEditsKeepTheRestOfTheFile()
    {
        var t = TomlFile.Parse("""
            # xemu
            [general]
            show_welcome = true

            [input.bindings]
            port1 = 'keyboard'

            [[input.gamepad_mappings]]
            port1 = 'other'
            """);
        Assert.Equal("keyboard", t.Get("input.bindings.port1"));
        XemuBackend.Ready(t, gamepad: true);
        t.SetInt("display.quality.surface_scale", 2);
        var text = t.ToString();
        Assert.Contains("# xemu", text);
        Assert.Contains("show_welcome = false", text);
        Assert.Null(t.Get("input.bindings.port1"));
        Assert.Contains("port1 = 'other'", text);  // inside [[array]], untouched
        var again = TomlFile.Parse(text);
        Assert.Equal("2", again.Get("display.quality.surface_scale"));
        Assert.False(again.GetBool("general.updates.check", true));
    }

    [Fact]
    public void SettingsGoToTomlFileAndPerGameToScanline()
    {
        var b = Backend();
        b.Settings.WriteGlobal(new Dictionary<string, string?> { ["display.quality.surface_scale"] = "2", ["display.window.vsync"] = "false" });
        var t = b.Paths.Load();
        Assert.Equal("2", t.Get("display.quality.surface_scale"));
        Assert.Contains("surface_scale = 2", File.ReadAllText(b.Paths.ConfigFile));  // a number, not a string
        Assert.Equal("false", b.Settings.ReadGlobal()["display.window.vsync"]);

        b.Settings.WritePerGame("4D530004", new Dictionary<string, string?> { ["display.renderer"] = "VULKAN" });
        Assert.Equal("VULKAN", b.Settings.ReadPerGame("4d530004")["display.renderer"]);
        b.Settings.WritePerGame("4D530004", new Dictionary<string, string?> { ["display.renderer"] = null });
        Assert.Empty(b.Settings.ReadPerGame("4D530004"));
        Assert.Throws<ArgumentException>(() => b.Settings.WriteGlobal(new Dictionary<string, string?> { ["nope"] = "1" }));
    }

    // ---- system files ----

    private static byte[] Flash() => Enumerable.Range(0, 256 << 10).Select(i => (byte)(i * 31 + 7)).ToArray();

    private static byte[] Qcow2() => [(byte)'Q', (byte)'F', (byte)'I', 0xFB, 0, 0, 0, 3, .. new byte[1020]];

    [Fact]
    public void ImportsThePlayersOwnSystemFiles()
    {
        var b = Backend();
        Assert.All(b.Status(), s => Assert.False(s.Present));
        Assert.Throws<InvalidDataException>(() => b.ImportBootRom(WriteFile("d/mcpx.bin", new byte[1024])));
        Assert.Throws<InvalidDataException>(() => b.ImportFlash(WriteFile("d/blank.bin", new byte[256 << 10])));
        Assert.Throws<InvalidDataException>(() => b.UseHdd(WriteFile("d/hdd.img", new byte[2048])));

        b.ImportBootRom(WriteFile("d/mcpx_1.0.bin", Enumerable.Range(0, 512).Select(i => (byte)i).ToArray()));
        b.ImportFlash(WriteFile("d/Complex_4627.bin", Flash()));
        var hdd = WriteFile("d/xbox_hdd.qcow2", Qcow2());
        b.UseHdd(hdd);
        Assert.All(b.Status(), s => Assert.True(s.Present));
        var t = b.Paths.Load();
        Assert.Equal(Path.Combine(b.Paths.SystemDir, "mcpx.bin"), t.Get(XemuBackend.BootRomKey));
        Assert.Equal(hdd, t.Get(XemuBackend.HddKey));  // used in place
        Assert.False(t.GetBool("general.show_welcome", true));
    }

    [Fact]
    public void HardDiskBackupsKeepTheLastFew()
    {
        var b = Backend();
        Assert.Null(b.BackupHardDisk());
        b.UseHdd(WriteFile("d/xbox_hdd.qcow2", Qcow2()));
        for (var i = 0; i < 5; i++)
        {
            Assert.NotNull(b.BackupHardDisk(new DateTime(2026, 11, 26, 10, i, 0)));
        }
        var kept = b.DiskBackups();
        Assert.Equal(XemuBackend.KeepDiskBackups, kept.Count);
        Assert.Equal("xbox_hdd-20261126-100400.qcow2", kept[0].Name);
    }

    // ---- launch ----

    private (XemuBackend b, Core.Library.GameEntry game, string iso) Ready()
    {
        var b = Backend();
        var iso = WriteFile("g/halo.iso", Xiso(0x4D530004, "Halo"));
        var game = b.TryRead(iso)!;
        b.Installs.UseExternal(new InstalledEmulator("stable", "0.8.110", WriteFile("Applications/xemu.AppImage", [0]), false));
        return (b, game, iso);
    }

    [Fact]
    public void PlanNeedsTheSystemFilesThenStartsTheDiscFullScreen()
    {
        var (b, game, iso) = Ready();
        Assert.Throws<InvalidOperationException>(() => b.Plan(game, new GameProfile(), new LaunchOptions()));
        b.ImportBootRom(WriteFile("d/mcpx.bin", new byte[512]));
        b.ImportFlash(WriteFile("d/flash.bin", Flash()));
        File.WriteAllText(b.Paths.ConfigFile, File.ReadAllText(b.Paths.ConfigFile) + "\n[input.bindings]\nport1 = 'keyboard'\n");

        var plan = b.Plan(game, new GameProfile(), new LaunchOptions { Gamepads = ["Steam Deck"] });
        Assert.Equal(["-full-screen", "-dvd_path", iso], plan.Arguments);
        Assert.Contains(plan.Notes, n => n.Contains("no Xbox hard disk"));
        foreach (var step in plan.Prepare)
        {
            step();
        }
        var t = b.Paths.Load();
        Assert.False(t.GetBool("general.show_welcome", true));
        Assert.Null(t.Get("input.bindings.port1"));
        Assert.False(File.Exists(b.RunConfig));
    }

    [Fact]
    public void AGamesOwnSettingsAndVulkanGoInARunConfig()
    {
        var (b, game, _) = Ready();
        b.ImportBootRom(WriteFile("d/mcpx.bin", new byte[512]));
        b.ImportFlash(WriteFile("d/flash.bin", Flash()));
        b.Settings.WritePerGame(game.TitleId, new Dictionary<string, string?> { ["display.quality.surface_scale"] = "3" });

        var plan = b.Plan(game, new GameProfile { Crt = 1 }, new LaunchOptions { Fullscreen = false });
        Assert.Equal(["-config_path", b.RunConfig, "-dvd_path", game.Path], plan.Arguments);
        foreach (var step in plan.Prepare)
        {
            step();
        }
        var run = TomlFile.Load(b.RunConfig);
        Assert.Equal("3", run.Get("display.quality.surface_scale"));
        Assert.Equal("VULKAN", run.Get("display.renderer"));
        Assert.Null(b.Paths.Load().Get("display.quality.surface_scale"));  // the player's own config is untouched

        b.Settings.WriteGlobal(new Dictionary<string, string?> { ["display.renderer"] = "OPENGL" });
        plan = b.Plan(game, new GameProfile { Crt = 1 }, new LaunchOptions());
        Assert.Contains(plan.Notes, n => n.Contains("need xemu's Vulkan renderer"));
    }

    [Fact]
    public void InstallPicksTheAppImageFromXemusGitHub()
    {
        var release = XemuInstallManager.Parse("""
            {"tag_name":"v0.8.110","published_at":"2026-11-01T00:00:00Z","assets":[
              {"name":"xemu-0.8.110-dbg-x86_64.AppImage","size":1,"digest":null,"browser_download_url":"https://github.com/xemu-project/xemu/releases/download/v0.8.110/dbg.AppImage"},
              {"name":"xemu-0.8.110-aarch64.AppImage","size":2,"digest":null,"browser_download_url":"https://github.com/xemu-project/xemu/releases/download/v0.8.110/xemu-0.8.110-aarch64.AppImage"},
              {"name":"xemu-0.8.110-x86_64.AppImage","size":30000000,"digest":"sha256:ABCDEF","browser_download_url":"https://github.com/xemu-project/xemu/releases/download/v0.8.110/xemu-0.8.110-x86_64.AppImage"}]}
            """);
        var a = XemuInstallManager.ToAvailable(release, Architecture.X64)!;
        Assert.Equal("0.8.110", a.Version);
        Assert.Equal("abcdef", a.Sha256);
        Assert.EndsWith("xemu-0.8.110-x86_64.AppImage", a.DownloadUrl);
        Assert.EndsWith("aarch64.AppImage", XemuInstallManager.ToAvailable(release, Architecture.Arm64)!.DownloadUrl);
    }

    [Fact]
    public void ConfigLivesInSdlsPreferenceFolder()
    {
        Assert.Equal(Path.Combine(_home, ".local/share/xemu/xemu/xemu.toml"), Backend().Paths.ConfigFile);
        _vars["XDG_DATA_HOME"] = Path.Combine(_home, "xdg");
        Assert.Equal(Path.Combine(_home, "xdg/xemu/xemu/xemu.toml"), Backend().Paths.ConfigFile);
    }
}
