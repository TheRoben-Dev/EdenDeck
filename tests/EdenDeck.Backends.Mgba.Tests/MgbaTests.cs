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

    private string Zip(string name, params (string Entry, byte[] Data)[] files)
    {
        var f = Path.Combine(_home, "roms", name);
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        using var zip = System.IO.Compression.ZipFile.Open(f, System.IO.Compression.ZipArchiveMode.Create);
        foreach (var (entry, data) in files)
        {
            using var s = zip.CreateEntry(entry).Open();
            s.Write(data);
        }
        return f;
    }

    [Theory]
    [InlineData("Pokemon - Emerald Version (USA, Europe) [!].gba", "Pokemon - Emerald Version")]
    [InlineData("1986 - Pokemon Emerald (U)(TrashMan).gba", "Pokemon Emerald")]
    [InlineData("pokemon_emerald.gba", "Pokemon Emerald")]
    [InlineData("Golden.Sun.gba", "Golden Sun")]
    [InlineData("MARIO KART SUPER CIRCUIT.gba", "Mario Kart Super Circuit")]
    [InlineData("Legend of Zelda, The - The Minish Cap (Europe) (En,Fr,De,Es,It).gba", "The Legend of Zelda - The Minish Cap")]
    [InlineData("Mr. Driller 2 (Japan).gba", "Mr. Driller 2")]
    [InlineData("F-Zero - Maximum Velocity (Rev 1).gba", "F-Zero - Maximum Velocity")]
    public void CleansFileNamesIntoTitles(string file, string title) => Assert.Equal(title, MgbaRom.CleanTitle(file));

    [Fact]
    public void NamesComeFromTheOfflineList()
    {
        Assert.Equal("Pokemon - Emerald Version (USA, Europe)", MgbaNames.Find(null, "BPEE"));
        Assert.Equal("Pokemon - Emerald Version (USA, Europe)", MgbaNames.Find(0x1F1C08FB, null));
        Assert.Null(MgbaNames.Find(0x12345678, "ZZZZ"));
        Assert.Equal(0xCBF43926u, MgbaRom.Crc32(new MemoryStream("123456789"u8.ToArray())));
        // A badly named file still gets the real name through its game code.
        var b = Backend();
        Assert.Equal("Pokemon - Emerald Version", b.TryRead(Rom("pkmn_em_1986.gba", Gba("POKEMON EMER", "BPEE")))!.Title);
        Assert.Equal("Pokemon - Emerald Version", b.TryRead(Zip("1986.zip", ("1986.gba", Gba("POKEMON EMER", "BPEE"))))!.Title);
    }

    [Fact]
    public void WritesGamepadButtonsUnlessThePlayerHasTheirOwn()
    {
        var b = Backend();
        var cfg = b.ConfigFile;
        Directory.CreateDirectory(Path.GetDirectoryName(cfg)!);
        File.WriteAllText(cfg, "[ports.qt]\nfullscreen=1\n");
        Assert.True(MgbaPad.Ensure(cfg, b.PadStampFile));
        var ini = EdenDeck.Core.Config.QtIni.Load(cfg, quoting: false);
        Assert.Equal("1", ini.Get(MgbaPad.Section, "keyA"));   // right face button
        Assert.Equal("0", ini.Get(MgbaPad.Section, "keyB"));   // bottom face button
        Assert.Equal("6", ini.Get(MgbaPad.Section, "hat0Up"));
        Assert.Equal("1", ini.Get("ports.qt", "fullscreen"));
        Assert.False(MgbaPad.Ensure(cfg, b.PadStampFile));     // already there

        // The player remaps in mGBA: left alone from then on.
        ini.Set(MgbaPad.Section, "keyA", "2");
        ini.Save(cfg);
        Assert.False(MgbaPad.Ensure(cfg, b.PadStampFile));
        Assert.Equal("2", EdenDeck.Core.Config.QtIni.Load(cfg, quoting: false).Get(MgbaPad.Section, "keyA"));

        // A mapping that was there before Scanline ever wrote one is the player's too.
        var other = Path.Combine(_home, "other.ini");
        File.WriteAllText(other, "[gba.input.SDLB]\nkeyA=3\n");
        Assert.False(MgbaPad.Ensure(other, Path.Combine(_home, "stamp2")));
    }

    [Fact]
    public void Player1IsThePadInTheDock()
    {
        var cfg = Path.Combine(_home, "dock.ini");
        var stamp = Path.Combine(_home, "dock.stamp");
        Assert.True(MgbaPad.Ensure(cfg, stamp, "Xbox Series X Controller"));
        Assert.Equal("Xbox Series X Controller", EdenDeck.Core.Config.QtIni.Load(cfg, quoting: false).Get(MgbaPad.Section, "device0"));
        Assert.False(MgbaPad.Ensure(cfg, stamp, "Xbox Series X Controller"));
        // Changing pads keeps Scanline's button mapping (the device isn't part of it).
        Assert.True(MgbaPad.Ensure(cfg, stamp, "Steam Deck"));
        var ini = EdenDeck.Core.Config.QtIni.Load(cfg, quoting: false);
        Assert.Equal("Steam Deck", ini.Get(MgbaPad.Section, "device0"));
        Assert.Equal("1", ini.Get(MgbaPad.Section, "keyA"));
    }

    [Fact]
    public void ReadsGamesInsideZips()
    {
        var b = Backend();
        Assert.Contains(".zip", b.FileExtensions);
        var zipped = b.TryRead(Zip("Tiny Quest (USA).zip", ("readme.txt", new byte[0x400]), ("Tiny Quest (USA).GBA", Gba("TINYQUEST", "BTQE"))))!;
        Assert.Equal("BTQE-42", zipped.TitleId);
        Assert.Equal("Tiny Quest", zipped.Title);
        Assert.EndsWith(".zip", zipped.Path);
        var gbc = b.TryRead(Zip("Bloop.zip", ("Bloop.gbc", Gb("BLOOP", color: true))))!;
        Assert.Equal("GBC_BLOOP-11", gbc.TitleId);

        Assert.Null(b.TryRead(Zip("photos.zip", ("cat.jpg", new byte[0x400]))));
        Assert.Null(b.TryRead(Rom("broken.zip", new byte[0x400])));
        // mGBA names saves after the .zip: "Tiny Quest (USA).sav".
        var dir = Path.GetDirectoryName(zipped.Path)!;
        File.WriteAllText(Path.Combine(dir, "Tiny Quest (USA).sav"), "save");
        Assert.Equal(["Tiny Quest (USA).sav"], b.Saves.Saves(zipped).Select(s => s.Relative).ToArray());
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
        var plan = b.Launch.Plan(rom, new GameProfile(), new LaunchOptions { HandheldFrame = false });
        Assert.Equal(exe, plan.Executable);
        Assert.Equal(["-f", "-C", "interframeBlending=1", "-C", "resampleVideo=0", rom.Path], plan.Arguments);
        // Overrides survive a restart and go away one by one.
        var again = Backend();
        Assert.Equal("true", again.Settings.ReadPerGame(rom.TitleId)["interframeBlending"]);
        again.Settings.WritePerGame(rom.TitleId, new Dictionary<string, string?> { ["interframeBlending"] = null, ["resampleVideo"] = null });
        Assert.Empty(again.Settings.ReadPerGame(rom.TitleId));
        Assert.Equal(["-f", rom.Path], again.Launch.Plan(rom, new GameProfile(), new LaunchOptions { HandheldFrame = false }).Arguments);
    }

    [Fact]
    public void NoMgbaMeansAClearMessage()
    {
        var b = Backend();
        Assert.Null(b.Install.Current);
        var rom = b.TryRead(Rom("Tiny Quest.gba", Gba("TINYQUEST", "BTQE")))!;
        var e = Assert.Throws<InvalidOperationException>(() => b.Launch.Plan(rom, new GameProfile(), new LaunchOptions { HandheldFrame = false }));
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

    [Fact]
    public void SavesAndStatesBackUpAndRestore()
    {
        var b = Backend();
        var rom = b.TryRead(Rom("Tiny Quest.gba", Gba("TINYQUEST", "BTQE")))!;
        Assert.Null(b.Saves.Backup(rom));  // nothing to keep yet
        var dir = Path.GetDirectoryName(rom.Path)!;
        File.WriteAllText(Path.Combine(dir, "Tiny Quest.sav"), "save-1");
        File.WriteAllText(Path.Combine(dir, "Tiny Quest.ss1"), "state-1");
        File.WriteAllText(Path.Combine(dir, "Tiny Quest 2.sav"), "other game");
        Assert.Equal(["Tiny Quest.sav", "Tiny Quest.ss1"], b.Saves.Saves(rom).Select(s => s.Relative).Order().ToArray());
        var first = b.Saves.Backup(rom, false, new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc))!;

        File.WriteAllText(Path.Combine(dir, "Tiny Quest.sav"), "save-2");
        File.Delete(Path.Combine(dir, "Tiny Quest.ss1"));
        File.WriteAllText(Path.Combine(dir, "Tiny Quest.ss2"), "state-2");
        var safety = b.Saves.Restore(rom, first.Path);
        Assert.NotNull(safety);
        Assert.Equal("save-1", File.ReadAllText(Path.Combine(dir, "Tiny Quest.sav")));
        Assert.True(File.Exists(Path.Combine(dir, "Tiny Quest.ss1")));
        Assert.False(File.Exists(Path.Combine(dir, "Tiny Quest.ss2")));  // the restored set, not a mix
        Assert.Equal("other game", File.ReadAllText(Path.Combine(dir, "Tiny Quest 2.sav")));
        Assert.Equal(2, b.Saves.Backups(rom).Count);

        b.Saves.Delete(rom);
        Assert.Empty(b.Saves.Saves(rom));
        Assert.True(File.Exists(Path.Combine(dir, "Tiny Quest 2.sav")));
    }

    [Fact]
    public void PatchesAreCheckedAndPassedAtLaunch()
    {
        var apps = Path.Combine(_home, "Applications");
        Directory.CreateDirectory(apps);
        File.WriteAllText(Path.Combine(apps, "mGBA-0.10.5-appimage-x64.appimage"), "#!/bin/sh\n");
        var b = Backend();
        var rom = b.TryRead(Rom("Tiny Quest.gba", Gba("TINYQUEST", "BTQE")))!;
        var fake = Rom("fake.ips", Encoding.ASCII.GetBytes("NOPE!"));
        Assert.Throws<InvalidDataException>(() => b.Mods.InstallPatch(rom.TitleId, fake));
        Assert.Throws<InvalidDataException>(() => b.Mods.InstallPatch(rom.TitleId, Rom("readme.txt", [1, 2, 3])));
        var patch = b.Mods.InstallPatch(rom.TitleId, Rom("Translation.ips", Encoding.ASCII.GetBytes("PATCHxxxEOF")));
        Assert.Equal(patch, b.Mods.ActivePatch(rom.TitleId));
        Assert.Equal(["-f", "-p", patch, rom.Path], b.Launch.Plan(rom, new GameProfile(), new LaunchOptions { Fullscreen = true, HandheldFrame = false }).Arguments);
        b.Mods.SetActivePatch(rom.TitleId, null);
        Assert.DoesNotContain("-p", b.Launch.Plan(rom, new GameProfile(), new LaunchOptions { HandheldFrame = false }).Arguments);
        b.Mods.RemovePatch(rom.TitleId, patch);
        Assert.Empty(b.Mods.Patches(rom.TitleId));
    }

    [Fact]
    public void OnlyCheatsThatAreOnGoToMgba()
    {
        var apps = Path.Combine(_home, "Applications");
        Directory.CreateDirectory(apps);
        File.WriteAllText(Path.Combine(apps, "mGBA-0.10.5-appimage-x64.appimage"), "#!/bin/sh\n");
        var b = Backend();
        var rom = b.TryRead(Rom("Tiny Quest.gba", Gba("TINYQUEST", "BTQE")))!;
        var cheats = MgbaMods.ParseCht("""
            cheats = 2

            cheat0_desc = "Infinite Health"
            cheat0_code = "82000000+0063"
            cheat0_enable = false

            cheat1_desc = "Max \"Gold\""
            cheat1_code = "82000010+FFFF"
            cheat1_enable = false
            """);
        Assert.Equal(["Infinite Health", "Max \"Gold\""], cheats.Select(c => c.Name).ToArray());
        b.Mods.SaveCheats(rom.TitleId, cheats);
        Assert.DoesNotContain("-c", b.Launch.Plan(rom, new GameProfile(), new LaunchOptions { HandheldFrame = false }).Arguments);

        b.Mods.SaveCheats(rom.TitleId, cheats.Select((c, i) => c with { Enabled = i == 1 }).ToList());
        var plan = b.Launch.Plan(rom, new GameProfile(), new LaunchOptions { HandheldFrame = false });
        var file = plan.Arguments[plan.Arguments.ToList().IndexOf("-c") + 1];
        foreach (var p in plan.Prepare)
        {
            p();
        }
        Assert.Equal("1", EdenDeck.Core.Config.QtIni.Load(b.ConfigFile, quoting: false).Get(MgbaPad.Section, "keyA"));  // gamepad written at launch
        var on = MgbaMods.ParseCht(File.ReadAllText(file));
        Assert.Equal("82000010+FFFF", Assert.Single(on).Code);
        Assert.True(on[0].Enabled);
    }

    [Fact]
    public void CheatFilesMatchTheRomName()
    {
        string[] files =
        [
            "Tiny Quest (USA, Europe) (Code Breaker).cht",
            "Tiny Quest (USA, Europe) (GameShark).cht",
            "Tiny Quest (Japan) (Code Breaker).cht",
            "Tiny Quest 2 (USA).cht",
        ];
        Assert.Equal(2, MgbaCheatDatabase.Match(files, "Tiny Quest (USA, Europe)", "Tiny Quest").Count());
        // A renamed ROM falls back to its clean title.
        Assert.Equal(3, MgbaCheatDatabase.Match(files, "tinyquest_final", "Tiny Quest").Count());
        Assert.Empty(MgbaCheatDatabase.Match(files, "Other", "Other"));
    }

    [Fact]
    public void CrtIsAnMgbaShaderPerLevel()
    {
        var apps = Path.Combine(_home, "Applications");
        Directory.CreateDirectory(apps);
        File.WriteAllText(Path.Combine(apps, "mGBA-0.10.5-appimage-x64.appimage"), "#!/bin/sh\n");
        var b = Backend();
        var rom = b.TryRead(Rom("Tiny Quest.gba", Gba("TINYQUEST", "BTQE")))!;
        Assert.DoesNotContain("-C", b.Launch.Plan(rom, new GameProfile { Crt = 0 }, new LaunchOptions { HandheldFrame = false }).Arguments);
        var plan = b.Launch.Plan(rom, new GameProfile { Crt = 2 }, new LaunchOptions { HandheldFrame = false });
        var arg = plan.Arguments[plan.Arguments.ToList().IndexOf("-C") + 1];
        Assert.StartsWith("shader=", arg);
        foreach (var p in plan.Prepare)
        {
            p();
        }
        var dir = arg["shader=".Length..];
        var manifest = File.ReadAllText(Path.Combine(dir, "manifest.ini"));
        Assert.Contains("passes=1", manifest);
        Assert.Contains("fragmentShader=scanline.fs", manifest);
        Assert.Contains("[pass.0.uniform.strength]\ntype=float\ndefault=1\n", manifest);
        Assert.Contains("[pass.0.uniform.frame]\ntype=float\ndefault=0\n", manifest);
        Assert.Contains("gl_FragColor", File.ReadAllText(Path.Combine(dir, "scanline.fs")));
        Assert.DoesNotContain("lockAspectRatio=0", plan.Arguments);
    }

    [Fact]
    public void HandheldFrameIsOnByDefaultAndPerGame()
    {
        var apps = Path.Combine(_home, "Applications");
        Directory.CreateDirectory(apps);
        File.WriteAllText(Path.Combine(apps, "mGBA-0.10.5-appimage-x64.appimage"), "#!/bin/sh\n");
        var b = Backend();
        var rom = b.TryRead(Rom("Tiny Quest.gba", Gba("TINYQUEST", "BTQE")))!;
        var plan = b.Launch.Plan(rom, new GameProfile(), new LaunchOptions { Accent = "#3DFF8F" });
        Assert.Contains("lockAspectRatio=0", plan.Arguments);   // the shader places the picture itself
        foreach (var p in plan.Prepare)
        {
            p();
        }
        var dir = plan.Arguments.First(a => a.StartsWith("shader=", StringComparison.Ordinal))["shader=".Length..];
        Assert.EndsWith("scanline-c0-f3dff8f", dir);
        var manifest = File.ReadAllText(Path.Combine(dir, "manifest.ini"));
        Assert.Contains("[pass.0.uniform.frame]\ntype=float\ndefault=1\n", manifest);
        Assert.Contains("[pass.0.uniform.accent]\ntype=float3\ndefault[0]=0.239\ndefault[1]=1\ndefault[2]=0.561\n", manifest);
        Assert.Contains("[pass.0.uniform.strength]\ntype=float\ndefault=0\n", manifest);

        // Off for this game, or globally with no per-game choice.
        Assert.DoesNotContain("-C", b.Launch.Plan(rom, new GameProfile { Frame = false }, new LaunchOptions()).Arguments);
        Assert.Contains("lockAspectRatio=0", b.Launch.Plan(rom, new GameProfile { Frame = true }, new LaunchOptions { HandheldFrame = false }).Arguments);
        // Frame and CRT share one shader.
        Assert.EndsWith("scanline-c3-f6c5ce7", MgbaShader.DirFor("/x", new(3, true)));
        Assert.Null(MgbaShader.DirFor("/x", new(0, false)));
    }

    [Fact]
    public void LooksAreOneTapPresets()
    {
        var b = Backend();
        var set = b.Presets!;
        Assert.Equal(EdenDeck.Core.Settings.PresetSet.Global, set.Current(b.Settings.ReadPerGame("X-1"), b.Settings.Options));
        set.Apply(b.Settings, "X-1", "lcd");
        Assert.Equal("true", b.Settings.ReadPerGame("X-1")["interframeBlending"]);
        Assert.Equal("lcd", set.Current(b.Settings.ReadPerGame("X-1"), b.Settings.Options));
        b.Settings.WritePerGame("X-1", new Dictionary<string, string?> { ["resampleVideo"] = "true" });
        Assert.Equal(EdenDeck.Core.Settings.PresetSet.Custom, set.Current(b.Settings.ReadPerGame("X-1"), b.Settings.Options));
        set.Apply(b.Settings, "X-1", EdenDeck.Core.Settings.PresetSet.Global);
        Assert.Empty(b.Settings.ReadPerGame("X-1"));
    }
}
