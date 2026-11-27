using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using EdenDeck.Core;
using EdenDeck.Core.Backends;
using EdenDeck.Core.Config;
using EdenDeck.Core.Launch;
using EdenDeck.Core.Profiles;
using Xunit;

namespace EdenDeck.Backends.MelonDs.Tests;

public sealed class MelonDsTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "scanline-melonds-" + Guid.NewGuid().ToString("N")[..10]);
    private readonly Dictionary<string, string?> _vars = new();

    public MelonDsTests()
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

    private MelonDsBackend Backend() => new(new AppPaths(Env), env: Env);

    private string WriteFile(string relative, byte[] bytes)
    {
        var path = Path.Combine(_home, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>A minimal .nds: a header with a valid checksum, and a banner whose icon is one red square on blue.</summary>
    public static byte[] Rom(string title, string code)
    {
        var rom = new byte[0x1000];
        Encoding.ASCII.GetBytes(title).CopyTo(rom, 0);
        Encoding.ASCII.GetBytes(code).CopyTo(rom, 0x0C);
        Encoding.ASCII.GetBytes("01").CopyTo(rom, 0x10);
        BinaryPrimitives.WriteUInt32LittleEndian(rom.AsSpan(0x68), 0x200);
        BinaryPrimitives.WriteUInt16LittleEndian(rom.AsSpan(0x15E), NdsRom.Crc16(rom.AsSpan(0, 0x15E)));
        var banner = rom.AsSpan(0x200);
        BinaryPrimitives.WriteUInt16LittleEndian(banner, 1);
        banner.Slice(0x20, 512).Fill(0x22);           // every pixel colour 2
        banner.Slice(0x20, 4).Fill(0x11);             // first tile's top row: colour 1
        BinaryPrimitives.WriteUInt16LittleEndian(banner[(0x220 + 2)..], 0x001F);  // 1: red
        BinaryPrimitives.WriteUInt16LittleEndian(banner[(0x220 + 4)..], 0x7C00);  // 2: blue
        return rom;
    }

    // ---- ROMs ----

    [Fact]
    public void ReadsTheHeaderAndChecksIt()
    {
        var h = NdsRom.ParseHeader(Rom("MARIOKARTDS", "AMCE"))!;
        Assert.Equal("MARIOKARTDS", h.Title);
        Assert.Equal("NTR-AMCE", h.TitleId);
        var bad = Rom("MARIOKARTDS", "AMCE");
        bad[3] ^= 1;
        Assert.Null(NdsRom.ParseHeader(bad));
        Assert.StartsWith("NTR-H", NdsRom.ParseHeader(Rom("HOMEBREW", "####"))!.TitleId);
    }

    [Fact]
    public void LibraryNamesGamesFromNoIntroThenTheFileName()
    {
        var b = Backend();
        var mk = b.TryRead(WriteFile("g/mk.nds", Rom("MARIOKARTDS", "AMCE")))!;
        Assert.Equal("Mario Kart DS", mk.Title);
        Assert.Equal("NTR-AMCE", mk.TitleId);
        Assert.Equal("Yarrow Fields", b.TryRead(WriteFile("g/Yarrow Fields (USA).nds", Rom("YARROW", "ZZZZ")))!.Title);
        Assert.Null(b.TryRead(WriteFile("g/notes.nds", new byte[4096])));
    }

    [Fact]
    public void DrawsTheBannerIconIntoAPng()
    {
        var b = Backend();
        var game = b.TryRead(WriteFile("g/mk.nds", Rom("MARIOKARTDS", "AMCE")))!;
        var rgba = NdsRom.ReadIcon(game.Path, NdsRom.ReadHeader(game.Path)!)!;
        Assert.Equal([255, 0, 0, 255], rgba[..4]);                 // top-left: red
        Assert.Equal([0, 0, 255, 255], rgba[(32 * 4 * 5)..(32 * 4 * 5 + 4)]);  // row 5: blue
        var png = b.IconFor(game)!;
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], File.ReadAllBytes(png)[..4]);
    }

    // ---- settings and launch ----

    [Fact]
    public void SettingsGoToMelonDsTomlAsNumbers()
    {
        var b = Backend();
        b.Settings.WriteGlobal(new Dictionary<string, string?> { [MelonDsSettings.Renderer] = "1", [MelonDsSettings.Layout] = "2", [MelonDsSettings.Jit] = "true" });
        var text = File.ReadAllText(Path.Combine(_home, ".config/melonDS/melonDS.toml"));
        Assert.Contains("[3D]\nRenderer = 1", text);
        Assert.Contains("[Instance0.Window0]\nScreenLayout = 2", text);
        Assert.Contains("[JIT]\nEnable = true", text);
        Assert.Equal("2", b.Settings.ReadGlobal()[MelonDsSettings.Layout]);
    }

    private (MelonDsBackend b, Core.Library.GameEntry game) Installed()
    {
        var b = Backend();
        var game = b.TryRead(WriteFile("g/mk.nds", Rom("MARIOKARTDS", "AMCE")))!;
        b.Installs.UseExternal(new InstalledEmulator("stable", "1.1", WriteFile("Applications/melonDS.AppImage", [0]), false));
        return (b, game);
    }

    [Fact]
    public void PlanStartsTheGameFullScreenWithTheControllerMapped()
    {
        var (b, game) = Installed();
        var plan = b.Plan(game, new GameProfile(), new LaunchOptions { Gamepads = ["Steam Deck"] });
        Assert.Equal(["-f", game.Path], plan.Arguments);
        Assert.False(plan.Environment.ContainsKey("XDG_CONFIG_HOME"));
        Assert.False(plan.Environment.ContainsKey("MESA_LOADER_DRIVER_OVERRIDE"));
        foreach (var step in plan.Prepare)
        {
            step();
        }
        var t = b.Paths.Load();
        Assert.Equal("1", t.Get("Instance0.Joystick.A"));
        Assert.Equal(((0x101 | 0x10000 | 1 << 24 | 1 << 20)).ToString(), t.Get("Instance0.Joystick.Up"));

        // A mapping the player made in melonDS is kept.
        t.SetInt("Instance0.Joystick.A", 3);
        t.Save(b.Paths.ConfigFile);
        foreach (var step in b.Plan(game, new GameProfile(), new LaunchOptions { Gamepads = ["x"] }).Prepare)
        {
            step();
        }
        Assert.Equal("3", b.Paths.Load().Get("Instance0.Joystick.A"));
    }

    [Fact]
    public void Player1sControllerIsTheOneInTheDock()
    {
        var (b, game) = Installed();
        var xbox = new EdenDeck.Core.Input.Gamepad { Name = "Xbox Series X Controller", Guid = "03000000de280000ff11000001000000", JoystickIndex = 1 };
        foreach (var step in b.Plan(game, new GameProfile(), new LaunchOptions { Gamepads = [xbox.Name], Players = [xbox] }).Prepare)
        {
            step();
        }
        Assert.Equal("1", b.Paths.Load().Get("Instance0.JoystickID"));
    }

    [Fact]
    public void TheCrtEffectRunsMelonDsOpenGlThroughZink()
    {
        var (b, game) = Installed();
        var plan = b.Plan(game, new GameProfile { Crt = 1 }, new LaunchOptions());
        Assert.Equal("zink", plan.Environment["MESA_LOADER_DRIVER_OVERRIDE"]);
        Assert.Equal(b.Paths.RunRoot, plan.Environment["XDG_CONFIG_HOME"]);
        foreach (var step in plan.Prepare)
        {
            step();
        }
        Assert.Equal("true", TomlFile.Load(b.Paths.RunConfigFile).Get(MelonDsBackend.UseGL));
        Assert.Null(b.Paths.Load().Get(MelonDsBackend.UseGL));
    }

    [Fact]
    public void AGamesOwnSettingsGoInARunConfig()
    {
        var (b, game) = Installed();
        b.Settings.WritePerGame(game.TitleId, new Dictionary<string, string?> { [MelonDsSettings.Scale] = "4" });
        var plan = b.Plan(game, new GameProfile(), new LaunchOptions());
        Assert.Equal(b.Paths.RunRoot, plan.Environment["XDG_CONFIG_HOME"]);
        foreach (var step in plan.Prepare)
        {
            step();
        }
        Assert.Equal("4", TomlFile.Load(b.Paths.RunConfigFile).Get(MelonDsSettings.Scale));
        Assert.Null(b.Paths.Load().Get(MelonDsSettings.Scale));
    }

    // ---- BIOS, saves, cheats ----

    private static byte[] Dump(int size) => Enumerable.Range(0, size).Select(i => (byte)(i * 13 + 1)).ToArray();

    [Fact]
    public void TheOwnBiosIsUsedOnceAllThreeAreThere()
    {
        var b = Backend();
        Assert.Throws<InvalidDataException>(() => b.ImportSystemFile(WriteFile("d/x.bin", new byte[1000])));
        Assert.Throws<InvalidDataException>(() => b.ImportSystemFile(WriteFile("d/blank.bin", new byte[4096])));
        Assert.Equal("ARM9 BIOS", b.ImportSystemFile(WriteFile("d/bios9.bin", Dump(4096))).Kind);
        b.ImportSystemFile(WriteFile("d/bios7.bin", Dump(16384)));
        Assert.False(b.UsesOwnBios);
        Assert.Null(b.Paths.Load().Get("Emu.ExternalBIOSEnable"));
        b.ImportSystemFile(WriteFile("d/firmware.bin", Dump(256 << 10)));
        Assert.True(b.UsesOwnBios);
        Assert.Equal("true", b.Paths.Load().Get("Emu.ExternalBIOSEnable"));
    }

    [Fact]
    public void SavesAreTheSavAndStatesNextToTheGame()
    {
        var b = Backend();
        var game = b.TryRead(WriteFile("g/mk.nds", Rom("MARIOKARTDS", "AMCE")))!;
        Assert.Null(b.Saves.Backup(game));
        WriteFile("g/mk.sav", [1, 2, 3]);
        WriteFile("g/mk.ml1", [4]);
        WriteFile("g/other.sav", [5]);
        Assert.Equal(["mk.sav", "mk.ml1"], b.Saves.Saves(game).Select(s => s.Relative));
        var backup = b.Saves.Backup(game)!;
        File.Delete(Path.Combine(_home, "g/mk.sav"));
        b.Saves.Restore(game, backup.Path);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(_home, "g/mk.sav")));
    }

    [Fact]
    public void CheatsFromTheDatabaseGoIntoMelonDsCheatFile()
    {
        var codes = MelonDsCheats.ParseCht("""
            cheats = 3

            cheat0_desc = "Misc Codes"

            cheat1_desc = "Infinite Coins"
            cheat1_code = "020C35DC+E5950170+D2000000+00000000"
            cheat1_enable = false

            cheat2_desc = "Broken"
            cheat2_code = "12345+"
            """);
        var coins = Assert.Single(codes);
        Assert.Equal("Infinite Coins", coins.Name);
        Assert.Equal(4, coins.Words.Count);

        var b = Backend();
        var game = b.TryRead(WriteFile("g/mk.nds", Rom("MARIOKARTDS", "AMCE")))!;
        File.WriteAllText(Path.Combine(_home, "g/mk.mch"), "CAT 0 Mine\n\nCODE 0 My code\n02000000 00000001\n\n");
        b.Cheats.Set(game, coins.Name, true, coins);
        var text = File.ReadAllText(b.Cheats.MchFile(game));
        Assert.Contains("CAT 0 Mine", text);
        Assert.Contains("CAT libretro database\n\nCODE 1 Infinite Coins\n020C35DC E5950170\nD2000000 00000000\n", text);
        Assert.Equal(1, b.Cheats.EnabledCount(game));
        b.Cheats.Set(game, "My code", true);
        b.Cheats.Set(game, coins.Name, false);
        Assert.True(b.Cheats.IsOn(game, "My code"));
        Assert.False(b.Cheats.IsOn(game, coins.Name));

        b.Installs.UseExternal(new InstalledEmulator("stable", "1.1", WriteFile("Applications/melonDS.AppImage", [0]), false));
        foreach (var step in b.Plan(game, new GameProfile(), new LaunchOptions()).Prepare)
        {
            step();
        }
        Assert.Equal("true", b.Paths.Load().Get("Instance0.EnableCheats"));
        Assert.Equal("Mario Kart DS (USA)", DsNames.CheatFile("AMCE"));
    }

    [Fact]
    public void InstallTakesTheAppImageZipFromMelonDsGitHub()
    {
        var release = MelonDsInstallManager.Parse("""
            {"tag_name":"1.1","published_at":"2026-01-01T00:00:00Z","assets":[
              {"name":"melonDS-1.1-windows-x86_64.zip","size":1,"digest":null,"browser_download_url":"https://github.com/melonDS-emu/melonDS/releases/download/1.1/w.zip"},
              {"name":"melonDS-1.1-ubuntu-x86_64.zip","size":1,"digest":null,"browser_download_url":"https://github.com/melonDS-emu/melonDS/releases/download/1.1/u.zip"},
              {"name":"melonDS-1.1-appimage-aarch64.zip","size":2,"digest":null,"browser_download_url":"https://github.com/melonDS-emu/melonDS/releases/download/1.1/melonDS-1.1-appimage-aarch64.zip"},
              {"name":"melonDS-1.1-appimage-x86_64.zip","size":30000000,"digest":"sha256:ABCDEF","browser_download_url":"https://github.com/melonDS-emu/melonDS/releases/download/1.1/melonDS-1.1-appimage-x86_64.zip"}]}
            """);
        var a = MelonDsInstallManager.ToAvailable(release, Architecture.X64)!;
        Assert.Equal("1.1", a.Version);
        Assert.Equal("abcdef", a.Sha256);
        Assert.EndsWith("appimage-x86_64.zip", a.DownloadUrl);
        Assert.EndsWith("appimage-aarch64.zip", MelonDsInstallManager.ToAvailable(release, Architecture.Arm64)!.DownloadUrl);

        var zip = Path.Combine(_home, "m.zip");
        using (var z = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
        {
            using var w = z.CreateEntry("melonDS-x86_64.AppImage").Open();
            w.Write([7, 7]);
        }
        MelonDsInstallManager.Unzip(zip, Path.Combine(_home, "out.AppImage"));
        Assert.Equal([7, 7], File.ReadAllBytes(Path.Combine(_home, "out.AppImage")));
        Assert.Equal("1.1", MelonDsInstallManager.FromFile("/x/melonDS-1.1-x86_64.AppImage", false).Version);
    }
}
