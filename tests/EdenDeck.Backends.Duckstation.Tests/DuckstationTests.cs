using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using EdenDeck.Backends.Pcsx2;
using EdenDeck.Backends.Pcsx2.Tests;
using EdenDeck.Core;
using EdenDeck.Core.Backends;
using EdenDeck.Core.Launch;
using EdenDeck.Core.Library;
using EdenDeck.Core.Profiles;
using Xunit;

namespace EdenDeck.Backends.Duckstation.Tests;

public sealed class DuckstationTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "scanline-ds-" + Guid.NewGuid().ToString("N")[..10]);
    private readonly Dictionary<string, string?> _vars = new();

    public DuckstationTests()
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

    private DuckstationBackend Backend() => new(new AppPaths(Env), env: Env);

    private string DataRoot => Path.Combine(_home, ".local/share/duckstation");

    private const string Sotn = "BOOT = cdrom:\\SLUS_000.67;1\r\nTCB = 4\r\nEVENT = 10\r\nSTACK = 801FFFF0\r\n";

    private string WriteFile(string relative, byte[] bytes)
    {
        var path = Path.Combine(_home, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string WriteText(string relative, string text) => WriteFile(relative, Encoding.UTF8.GetBytes(text));

    // ---- discs ----

    [Fact]
    public void ReadsTheSerialFromAnIsoAndARawBin()
    {
        Assert.Equal("SLUS-00067", Ps1Disc.Read(WriteFile("g/sotn.iso", Pcsx2Tests.Iso(Sotn)))!.Serial);
        var bin = WriteFile("g/sotn (Track 1).bin", Pcsx2Tests.RawBin(Pcsx2Tests.Iso(Sotn)));
        WriteFile("g/sotn (Track 2).bin", new byte[2352 * 4]);
        var cue = WriteText("g/sotn.cue", "FILE \"sotn (Track 1).bin\" BINARY\n  TRACK 01 MODE2/2352\n    INDEX 01 00:00:00\nFILE \"sotn (Track 2).bin\" BINARY\n  TRACK 02 AUDIO\n    INDEX 01 00:00:00\n");
        Assert.Equal("SLUS-00067", Ps1Disc.Read(cue)!.Serial);
        Assert.Equal(new FileInfo(bin).Length + 2352 * 4, DuckstationBackend.CueData(cue));
    }

    [Fact]
    public void Ps2DiscsAreNotPs1Games()
    {
        Assert.Null(Ps1Disc.Read(WriteFile("g/ps2.iso", Pcsx2Tests.Iso())));
        Assert.Null(Backend().TryRead(WriteFile("g/ps2b.iso", Pcsx2Tests.Iso())));
        Assert.Null(Ps1Disc.Read(WriteFile("g/blank.iso", new byte[40 * 2048])));
    }

    [Theory]
    [InlineData("cdrom:\\SLUS_000.67;1", "SLUS-00067")]
    [InlineData("cdrom:SCES_123.45;1", "SCES-12345")]
    [InlineData("cdrom:\\GAME\\slps_011.23", "SLPS-01123")]
    [InlineData("cdrom:\\PSX.EXE;1", null)]
    public void SerialsFollowDuckStationsRule(string boot, string? serial) => Assert.Equal(serial, Ps1Disc.Serial(boot));

    [Fact]
    public void ReadsAPbpsDiscId()
    {
        var keys = Encoding.ASCII.GetBytes("DISC_ID\0TITLE\0\0\0");
        var id = Encoding.ASCII.GetBytes("SLUS00067\0\0\0");
        var title = Encoding.UTF8.GetBytes("Castlevania\0");
        var sfo = new MemoryStream();
        var w = new BinaryWriter(sfo);
        w.Write(Encoding.ASCII.GetBytes("\0PSF"));
        w.Write(0x0101);
        w.Write(20 + 32);                    // key table
        w.Write(20 + 32 + keys.Length);      // data table
        w.Write(2);
        w.Write((ushort)0); w.Write((ushort)0x0204); w.Write(10); w.Write(id.Length); w.Write(0);
        w.Write((ushort)8); w.Write((ushort)0x0204); w.Write(title.Length); w.Write(title.Length); w.Write(id.Length);
        w.Write(keys);
        w.Write(id);
        w.Write(title);
        var sfoBytes = sfo.ToArray();
        var pbp = new byte[40 + sfoBytes.Length];
        Encoding.ASCII.GetBytes("\0PBP").CopyTo(pbp, 0);
        BitConverter.GetBytes(40).CopyTo(pbp, 8);
        BitConverter.GetBytes(40 + sfoBytes.Length).CopyTo(pbp, 12);
        sfoBytes.CopyTo(pbp, 40);
        var info = Ps1Disc.Read(WriteFile("g/EBOOT.PBP", pbp))!;
        Assert.Equal("SLUS-00067", info.Serial);
        Assert.Equal("Castlevania", info.Title);
    }

    [Fact]
    public void LibraryEntryUsesTheRedumpName()
    {
        var game = Backend().TryRead(WriteFile("g/sotn.iso", Pcsx2Tests.Iso(Sotn)))!;
        Assert.Equal("SLUS-00067", game.TitleId);
        Assert.Equal("Castlevania - Symphony of the Night", game.Title);
        Assert.Equal("duckstation:SLUS-00067", game.GameId);
    }

    [Fact]
    public void ChdsAreKnownByTheirRedumpNameOrTheirFolder()
    {
        var cd = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "tiny-cd.chd"));
        var dvd = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "tiny-dvd.chd"));
        var b = Backend();
        var named = b.TryRead(WriteFile("roms/Final Fantasy VII (USA) (Disc 1).chd", cd))!;
        Assert.Equal("SCUS-94163", named.TitleId);
        Assert.Equal("Final Fantasy VII (Disc 1)", named.Title);
        Assert.Null(b.TryRead(WriteFile("roms/Some Saturn Game.chd", cd)));  // could be any CD system
        var inFolder = b.TryRead(WriteFile("roms/psx/My Game.chd", cd))!;
        Assert.Equal("", inFolder.TitleId);
        Assert.Equal("duckstation:My Game.chd", inFolder.GameId);
        Assert.Null(b.TryRead(WriteFile("roms/psx/Final Fantasy VII (USA) (Disc 1) dvd.chd", dvd)));  // a DVD is never PS1
    }

    private const string Log = """
        I(Host): Loading config from /x/settings.ini.
        I(System): Game: Castlevania - Symphony of the Night | SLUS-00067 | HASH-EE47DA5C5D35A9B4
        I(System): Game started at entry point 0x80018000.
        """;

    [Fact]
    public void LearnsTheSerialFromDuckStationsLog()
    {
        Assert.Equal(("Castlevania - Symphony of the Night", "SLUS-00067"), DuckstationDiscs.ParseLog(Log));
        Assert.Null(DuckstationDiscs.ParseLog("I(System): Game: Homebrew | HASH-1234 | HASH-1234\n"));
        var b = Backend();
        var cd = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "tiny-cd.chd"));
        var path = WriteFile("roms/ps1/sotn.chd", cd);
        Assert.Equal("", b.TryRead(path)!.TitleId);
        Assert.True(b.Discs.Learn(path, "SLUS-00067", "Castlevania - Symphony of the Night"));
        var again = b.TryRead(path)!;
        Assert.Equal("SLUS-00067", again.TitleId);
        Assert.Equal("Castlevania - Symphony of the Night", again.Title);
        Assert.Equal("Castlevania - Symphony of the Night", b.Discs.SaveTitleFor("SLUS-00067"));
    }

    // ---- settings, controller ----

    [Fact]
    public void SettingsGoToDuckStationsFilesWithoutTheWizard()
    {
        var b = Backend();
        b.Settings.WriteGlobal(new Dictionary<string, string?> { ["GPU/ResolutionScale"] = "3", ["GPU/PGXPEnable"] = "true" });
        var ini = Pcsx2Ini.Load(Path.Combine(DataRoot, "settings.ini"));
        Assert.Equal("3", ini.Get("GPU", "ResolutionScale"));
        Assert.Equal("false", ini.Get("Main", "SetupWizardIncomplete"));
        Assert.Equal("true", b.Settings.ReadGlobal()["GPU/PGXPEnable"]);

        b.Settings.WritePerGame("SLUS-00067", new Dictionary<string, string?> { ["Display/AspectRatio"] = "16:9" });
        Assert.Equal("16:9", Pcsx2Ini.Load(Path.Combine(DataRoot, "gamesettings/SLUS-00067.ini")).Get("Display", "AspectRatio"));
        Assert.Equal("16:9", b.Settings.ReadPerGame("SLUS-00067")["Display/AspectRatio"]);
        b.Settings.WritePerGame("SLUS-00067", new Dictionary<string, string?> { ["Display/AspectRatio"] = null });
        Assert.False(File.Exists(Path.Combine(DataRoot, "gamesettings/SLUS-00067.ini")));
        Assert.Throws<InvalidOperationException>(() => b.Settings.WritePerGame("", new Dictionary<string, string?> { ["GPU/PGXPEnable"] = "true" }));

        _vars["XDG_CONFIG_HOME"] = Path.Combine(_home, "cfg");
        Assert.Equal(Path.Combine(_home, "cfg/duckstation/settings.ini"), Backend().Paths.ConfigFile);
    }

    [Fact]
    public void ControllerIsMappedOnlyWhenPlayerOneIsUnset()
    {
        var p = Backend().Paths;
        Assert.True(DuckstationPads.Ensure(p));
        var ini = Pcsx2Ini.Load(p.ConfigFile);
        Assert.Equal("AnalogController", ini.Get("Pad1", "Type"));
        Assert.Equal("SDL-0/A", ini.Get("Pad1", "Cross"));
        Assert.Equal("SDL-0/+RightTrigger", ini.Get("Pad1", "R2"));
        Assert.Equal("SDL-0/-LeftY", ini.Get("Pad1", "LUp"));

        ini.Set("Pad1", "Cross", "SDL-0/B");
        ini.Save(p.ConfigFile);
        Assert.False(DuckstationPads.Ensure(p));  // the player's own mapping stays
        Assert.Equal("SDL-0/B", Pcsx2Ini.Load(p.ConfigFile).Get("Pad1", "Cross"));

        var keyboard = Pcsx2Ini.Parse("[Pad1]\nType = AnalogController\nCross = Keyboard/I\n");
        Assert.True(DuckstationPads.Untouched(keyboard));
        Assert.False(DuckstationPads.Untouched(Pcsx2Ini.Parse("[Pad1]\nType = GunCon\n")));
    }

    // ---- patches and cheats ----

    private const string Patches = """
        # Castlevania (USA) SLUS-00067
        [Widescreen 16:9]
        Type = Gameshark
        Activation = EndFrame
        Author = Someone
        Description = Renders the game in 16:9.
        OverrideAspectRatio = 16:9
        A7060D58 10000C00

        [60 FPS]
        Type = Gameshark
        80097BEE 0FFF ; trailing comment

        [Only Metadata]
        Description = no code lines, not a code
        """;

    [Fact]
    public void ParsesChtCodes()
    {
        var codes = DuckstationCheats.Parse(Patches);
        Assert.Equal(["Widescreen 16:9", "60 FPS"], codes.Select(c => c.Name));
        Assert.Equal("16:9", codes[0].AspectRatio);
        Assert.Equal("Someone", codes[0].Author);
    }

    [Fact]
    public void PatchesAndCheatsAreSwitchedInTheGamesSettings()
    {
        var b = Backend();
        var cheats = b.Cheats;
        WriteText("data-patches.cht", Patches);
        Directory.CreateDirectory(b.Paths.PatchesDir);
        File.Copy(Path.Combine(_home, "data-patches.cht"), Path.Combine(b.Paths.PatchesDir, "SLUS-00067.cht"));
        var patches = cheats.Patches("SLUS-00067");
        Assert.Equal(2, patches.Count);

        cheats.SetPatch("SLUS-00067", patches[1], true);
        var game = Pcsx2Ini.Load(b.Paths.GameSettingsFile("SLUS-00067"));
        Assert.Equal(["60 FPS"], game.GetAll("Patches", "Enable"));
        Assert.True(cheats.IsPatchOn("SLUS-00067", patches[1]));

        // Widescreen rendering with a 16:9 picture: DuckStation turns the widescreen patch on itself.
        Assert.False(cheats.IsPatchOn("SLUS-00067", patches[0]));
        b.Settings.WritePerGame("SLUS-00067", new Dictionary<string, string?> { ["GPU/WidescreenHack"] = "true", ["Display/AspectRatio"] = "16:9" });
        Assert.True(cheats.IsPatchOn("SLUS-00067", patches[0]));
        Assert.True(cheats.IsAutomatic("SLUS-00067", patches[0]));
        Assert.Throws<InvalidOperationException>(() => cheats.SetPatch("SLUS-00067", patches[0], false));

        var own = WriteText("dl/mine.cht", "[Infinite HP]\n80097BA0 270F\n");
        Assert.Equal(1, cheats.ImportCheats("SLUS-00067", own));
        Assert.True(File.Exists(Path.Combine(DataRoot, "cheats/SLUS-00067.cht")));
        cheats.SetCheat("SLUS-00067", "Infinite HP", true);
        game = Pcsx2Ini.Load(b.Paths.GameSettingsFile("SLUS-00067"));
        Assert.Equal("true", game.Get("Cheats", "EnableCheats"));
        Assert.Equal(["Infinite HP"], game.GetAll("Cheats", "Enable"));
        Assert.Equal((2, 1), cheats.EnabledCount("SLUS-00067"));
        cheats.SetCheat("SLUS-00067", "Infinite HP", false);
        Assert.Null(Pcsx2Ini.Load(b.Paths.GameSettingsFile("SLUS-00067")).Get("Cheats", "EnableCheats"));
        Assert.Throws<InvalidDataException>(() => cheats.ImportCheats("SLUS-00067", WriteText("dl/notes.cht", "hello\n")));
    }

    // ---- saves ----

    [Fact]
    public void SavesBackUpThisGamesCardsAndStates()
    {
        var b = Backend();
        var game = b.TryRead(WriteFile("g/sotn.iso", Pcsx2Tests.Iso(Sotn)))!;
        WriteText(".local/share/duckstation/memcards/Castlevania - Symphony of the Night_1.mcd", "card");
        WriteText(".local/share/duckstation/memcards/Other Game_1.mcd", "other");
        WriteText(".local/share/duckstation/savestates/SLUS-00067_1.sav", "state");
        WriteText(".local/share/duckstation/savestates/SLUS-00068_1.sav", "not ours");
        Assert.Equal(["memcards/Castlevania - Symphony of the Night_1.mcd", "savestates/SLUS-00067_1.sav"], b.Saves.Saves(game).Select(s => s.Relative));
        Assert.False(b.Saves.UsesSharedCard());

        var backup = b.Saves.Backup(game)!;
        using (var zip = ZipFile.OpenRead(backup.Path))
        {
            Assert.Equal(2, zip.Entries.Count);
        }
        b.Saves.Delete(game);
        Assert.Empty(b.Saves.Saves(game));
        Assert.True(File.Exists(Path.Combine(DataRoot, "memcards/Other Game_1.mcd")));
        b.Saves.Restore(game, backup.Path);
        Assert.Equal("card", File.ReadAllText(Path.Combine(DataRoot, "memcards/Castlevania - Symphony of the Night_1.mcd")));
        Assert.Equal("state", File.ReadAllText(Path.Combine(DataRoot, "savestates/SLUS-00067_1.sav")));
    }

    [Fact]
    public void ASharedCardIsBackedUpButNeverDeleted()
    {
        var b = Backend();
        var game = b.TryRead(WriteFile("g/sotn.iso", Pcsx2Tests.Iso(Sotn)))!;
        WriteText(".local/share/duckstation/settings.ini", "[MemoryCards]\nCard1Type = Shared\n");
        WriteText(".local/share/duckstation/memcards/shared_card_1.mcd", "everyone");
        Assert.True(b.Saves.UsesSharedCard());
        Assert.Equal(["memcards/shared_card_1.mcd"], b.Saves.Saves(game).Select(s => s.Relative));
        b.Saves.Delete(game);
        Assert.True(File.Exists(Path.Combine(DataRoot, "memcards/shared_card_1.mcd")));
    }

    // ---- BIOS, launch, install ----

    private static byte[] Ps1Bios()
    {
        var rom = new byte[512 << 10];
        Encoding.ASCII.GetBytes("Sony Computer Entertainment Inc.").CopyTo(rom, 0x108);
        return rom;
    }

    [Fact]
    public void BiosImportChecksItIsAPs1Bios()
    {
        var b = Backend();
        Assert.False(b.BiosInstalled);
        Assert.Equal("SCPH-1001.bin", b.ImportBios(WriteFile("dumps/SCPH-1001.bin", Ps1Bios())));
        Assert.True(File.Exists(Path.Combine(DataRoot, "bios/SCPH-1001.bin")));
        Assert.True(b.BiosInstalled);
        Assert.Throws<InvalidDataException>(() => b.ImportBios(WriteFile("dumps/notes.bin", new byte[512 << 10])));
        Assert.Throws<InvalidDataException>(() => b.ImportBios(WriteFile("dumps/small.bin", new byte[1000])));
    }

    [Fact]
    public void PlanStartsTheDiscInBatchModeAndLearnsItAfterwards()
    {
        var b = Backend();
        var iso = WriteFile("g/sotn.iso", Pcsx2Tests.Iso(Sotn));
        var game = b.TryRead(iso)!;
        b.Installs.UseExternal(new InstalledEmulator("stable", "0.1-9226", WriteFile("Applications/DuckStation-x64.AppImage", [0]), false));
        Assert.Throws<InvalidOperationException>(() => b.Plan(game, new GameProfile(), new LaunchOptions()));  // no BIOS yet
        b.ImportBios(WriteFile("dumps/SCPH-1001.bin", Ps1Bios()));

        var plan = b.Plan(game, new GameProfile(), new LaunchOptions { Gamepads = ["Steam Deck"] });
        Assert.Equal(["-batch", "-nogui", "-fullscreen", "--", iso], plan.Arguments);
        foreach (var step in plan.Prepare)
        {
            step();
        }
        var ini = Pcsx2Ini.Load(b.Paths.ConfigFile);
        Assert.Equal("SDL-0/A", ini.Get("Pad1", "Cross"));
        Assert.Equal("true", ini.Get("Logging", "LogToFile"));
        File.WriteAllText(b.Paths.LogFile, Log);
        foreach (var step in plan.Cleanup)
        {
            step();
        }
        Assert.Equal("false", Pcsx2Ini.Load(b.Paths.ConfigFile).Get("Logging", "LogToFile"));  // put back as it was
        Assert.Equal("Castlevania - Symphony of the Night", b.Discs.SaveTitleFor("SLUS-00067"));
    }

    [Fact]
    public void InstallPicksTheAppImageFromDuckStationsGitHub()
    {
        var release = DuckstationInstallManager.Parse("""
            {"tag_name":"latest","name":"Latest Rolling Release","body":"Version v0.1-9226 built from abc","published_at":"2026-11-01T00:00:00Z","assets":[
              {"name":"duckstation-windows-x64-release.zip","size":1,"digest":null,"updated_at":"2026-11-01T00:00:00Z","browser_download_url":"https://github.com/stenzek/duckstation/releases/download/latest/w.zip"},
              {"name":"DuckStation-arm64.AppImage","size":2,"digest":null,"updated_at":"2026-11-01T00:00:00Z","browser_download_url":"https://github.com/stenzek/duckstation/releases/download/latest/DuckStation-arm64.AppImage"},
              {"name":"DuckStation-x64.AppImage","size":60000000,"digest":"sha256:ABCDEF","updated_at":"2026-11-01T00:00:00Z","browser_download_url":"https://github.com/stenzek/duckstation/releases/download/latest/DuckStation-x64.AppImage"}]}
            """);
        var a = DuckstationInstallManager.ToAvailable(release, Architecture.X64)!;
        Assert.Equal("0.1-9226", a.Version);
        Assert.Equal("abcdef", a.Sha256);
        Assert.EndsWith("DuckStation-x64.AppImage", a.DownloadUrl);
        Assert.EndsWith("DuckStation-arm64.AppImage", DuckstationInstallManager.ToAvailable(release, Architecture.Arm64)!.DownloadUrl);
        Assert.True(DuckstationInstallManager.IsOfficial(a.DownloadUrl));
        Assert.False(DuckstationInstallManager.IsOfficial("https://github.com/someone/duckstation/releases/download/x.AppImage"));

        var dated = DuckstationInstallManager.Parse("""
            {"tag_name":"latest","name":"Latest","body":"","assets":[
              {"name":"DuckStation-x64.AppImage","size":1,"digest":null,"updated_at":"2026-11-20T10:00:00Z","browser_download_url":"https://github.com/stenzek/duckstation/releases/download/latest/DuckStation-x64.AppImage"}]}
            """);
        Assert.Equal("2026.11.20", DuckstationInstallManager.ToAvailable(dated, Architecture.X64)!.Version);
    }

    [Fact]
    public void DiscsGoToWhicheverBackendCanReadThem()
    {
        var pcsx2 = new Pcsx2Backend(new AppPaths(Env), env: Env);
        var library = new LibraryService([pcsx2, Backend()]);
        var ps1 = WriteFile("games/sotn.iso", Pcsx2Tests.Iso(Sotn));
        var ps2 = WriteFile("games/gtasa.iso", Pcsx2Tests.Iso());
        File.SetLastWriteTimeUtc(ps1, DateTime.UtcNow.AddHours(-1));
        File.SetLastWriteTimeUtc(ps2, DateTime.UtcNow.AddHours(-1));
        var games = library.Scan([new LibraryFolder(Path.Combine(_home, "games"))]);
        Assert.Equal(["duckstation:SLUS-00067", "pcsx2:SLUS-20946"], games.Select(g => g.GameId).Order());
    }
}
