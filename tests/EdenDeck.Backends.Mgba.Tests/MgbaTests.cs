using System.Runtime.InteropServices;
using System.Text;
using EdenDeck.Backends.Mgba;
using EdenDeck.Core;
using EdenDeck.Core.Config;
using EdenDeck.Core.Launch;
using EdenDeck.Core.Profiles;
using Xunit;

namespace EdenDeck.Backends.Mgba.Tests;

public sealed class MgbaTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "scanline-mgba-" + Guid.NewGuid().ToString("N")[..10]);
    private readonly Dictionary<string, string?> _vars = new();

    public MgbaTests()
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

    private MgbaBackend Backend() => new(new AppPaths(Env), env: Env);

    /// <summary>A minimal GBA ROM: header title, game code, maker, 0x96 and a checksum.</summary>
    public static byte[] Gba(string title, string code, byte checksum = 0x42)
    {
        var b = new byte[0x200];
        Encoding.ASCII.GetBytes(title).CopyTo(b, 0xA0);
        Encoding.ASCII.GetBytes(code).CopyTo(b, 0xAC);
        Encoding.ASCII.GetBytes("01").CopyTo(b, 0xB0);
        b[0xB2] = 0x96;
        b[0xBD] = checksum;
        return b;
    }

    public static byte[] Gb(string title, bool color, byte checksum = 0x11)
    {
        var b = new byte[0x8000];
        new byte[] { 0xCE, 0xED, 0x66, 0x66, 0xCC, 0x0D, 0x00, 0x0B }.CopyTo(b, 0x104);
        Encoding.ASCII.GetBytes(title).CopyTo(b, 0x134);
        if (color)
        {
            b[0x143] = 0x80;
        }
        b[0x14D] = checksum;
        return b;
    }

    private string Rom(string name, byte[] data)
    {
        var f = Path.Combine(_home, "roms", name);
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllBytes(f, data);
        return f;
    }

    [Fact]
    public void ReadsCartridgeHeaders()
    {
        var b = Backend();
        var gba = b.TryRead(Rom("Legend of Pixels, The - Tiny Quest (USA, Europe) [!].gba", Gba("TINYQUEST", "BTQE")))!;
        Assert.Equal("BTQE-42", gba.TitleId);
        Assert.Equal("The Legend of Pixels - Tiny Quest", gba.Title);
        Assert.Equal("mgba:BTQE-42", gba.GameId);
        Assert.Equal("Game Boy Advance", MgbaBackend.SystemOf(gba.TitleId));

        var gbc = b.TryRead(Rom("Bloop (World).gbc", Gb("BLOOP", color: true)))!;
        Assert.Equal("GBC_BLOOP-11", gbc.TitleId);
        Assert.Equal("Game Boy Color", MgbaBackend.SystemOf(gbc.TitleId));
        var gb = b.TryRead(Rom("Blip.gb", Gb("BLIP", color: false)))!;
        Assert.Equal("Game Boy", MgbaBackend.SystemOf(gb.TitleId));

        Assert.Null(b.TryRead(Rom("junk.gb", new byte[0x200])));  // no Nintendo logo
        Assert.Null(b.TryRead(Rom("tiny.gba", new byte[16])));
        // Same game code, another revision: its own id.
        Assert.NotEqual(gba.TitleId, b.TryRead(Rom("rev1.gba", Gba("TINYQUEST", "BTQE", 0x43)))!.TitleId);
    }

    [Fact]
    public void GlobalSettingsUseMgbasOwnFormat()
    {
        var b = Backend();
        Assert.Equal(Path.Combine(_home, ".config", "mgba", "config.ini"), b.ConfigFile);
        Directory.CreateDirectory(Path.GetDirectoryName(b.ConfigFile)!);
        File.WriteAllText(b.ConfigFile, "[ports.qt]\nlockAspectRatio=1\nbios=/home/me/My \"bios\", copy.bin\nshowLibrary=1\n");
        Assert.Equal("true", b.Settings.ReadGlobal()["lockAspectRatio"]);
        b.Settings.WriteGlobal(new Dictionary<string, string?> { ["lockIntegerScaling"] = "true", ["lockAspectRatio"] = null });
        var ini = QtIni.Load(b.ConfigFile, quoting: false);
        Assert.Equal("1", ini.Get("ports.qt", "lockIntegerScaling"));
        Assert.Null(ini.Get("ports.qt", "lockAspectRatio"));
        Assert.Contains("bios=/home/me/My \"bios\", copy.bin\n", File.ReadAllText(b.ConfigFile));  // untouched, not re-quoted
        Assert.Throws<ArgumentException>(() => b.Settings.WriteGlobal(new Dictionary<string, string?> { ["nope"] = "1" }));
    }

    [Fact]
    public void LaunchesFullscreenWithPerGameOverrides()
    {
        var apps = Path.Combine(_home, "Applications");
        Directory.CreateDirectory(apps);
        var exe = Path.Combine(apps, "mGBA-0.10.5-appimage-x64.appimage");
        File.WriteAllText(exe, "#!/bin/sh\n");
        var b = Backend();
        Assert.Equal("0.10.5", b.Install.Current!.Version);
        var rom = b.TryRead(Rom("Tiny Quest.gba", Gba("TINYQUEST", "BTQE")))!;
        b.Settings.WritePerGame(rom.TitleId, new Dictionary<string, string?> { ["interframeBlending"] = "true", ["resampleVideo"] = "false" });
        var plan = b.Launch.Plan(rom, new GameProfile(), new LaunchOptions());
        Assert.Equal(exe, plan.Executable);
        Assert.Equal(["-f", "-C", "interframeBlending=1", "-C", "resampleVideo=0", rom.Path], plan.Arguments);
        // Overrides survive a restart and go away one by one.
        var again = Backend();
        Assert.Equal("true", again.Settings.ReadPerGame(rom.TitleId)["interframeBlending"]);
        again.Settings.WritePerGame(rom.TitleId, new Dictionary<string, string?> { ["interframeBlending"] = null, ["resampleVideo"] = null });
        Assert.Empty(again.Settings.ReadPerGame(rom.TitleId));
        Assert.Equal(["-f", rom.Path], again.Launch.Plan(rom, new GameProfile(), new LaunchOptions()).Arguments);
    }

    [Fact]
    public void NoMgbaMeansAClearMessage()
    {
        var b = Backend();
        Assert.Null(b.Install.Current);
        var rom = b.TryRead(Rom("Tiny Quest.gba", Gba("TINYQUEST", "BTQE")))!;
        var e = Assert.Throws<InvalidOperationException>(() => b.Launch.Plan(rom, new GameProfile(), new LaunchOptions()));
        Assert.Contains("isn't installed", e.Message);
    }

    [Fact]
    public void PicksTheOfficialAppImage()
    {
        var json = """
        {"tag_name":"0.10.5","published_at":"2025-03-01T00:00:00Z","assets":[
          {"name":"mGBA-0.10.5-win64.7z","size":1,"browser_download_url":"https://github.com/mgba-emu/mgba/releases/download/0.10.5/mGBA-0.10.5-win64.7z"},
          {"name":"mGBA-0.10.5-appimage-x64.appimage","size":2,"digest":"sha256:ABCD","browser_download_url":"https://github.com/mgba-emu/mgba/releases/download/0.10.5/mGBA-0.10.5-appimage-x64.appimage"}]}
        """;
        var a = MgbaInstallManager.ToAvailable(MgbaInstallManager.Parse(json), Architecture.X64)!;
        Assert.Equal("0.10.5", a.Version);
        Assert.Equal("abcd", a.Sha256);
        Assert.EndsWith(".appimage", a.DownloadUrl);
        Assert.True(MgbaInstallManager.IsOfficial(a.DownloadUrl));
        Assert.False(MgbaInstallManager.IsOfficial("https://github.com/someone/mgba/releases/download/x.appimage"));
        Assert.Null(MgbaInstallManager.ToAvailable(MgbaInstallManager.Parse(json), Architecture.Arm64));
    }

    [Fact]
    public void BiosIsTheUsersOwnAndChecked()
    {
        var b = Backend();
        Assert.False(b.Status().Single().Present);
        var wrong = Rom("bios.txt", new byte[100]);
        Assert.Throws<InvalidDataException>(() => b.SetBios(wrong));
        var bios = Rom("gba_bios.bin", new byte[16384]);
        b.SetBios(bios);
        Assert.Equal(bios, b.BiosPath);
        Assert.True(b.Status().Single().Present);
        b.SetBios(null);
        Assert.Null(b.BiosPath);
    }
}
