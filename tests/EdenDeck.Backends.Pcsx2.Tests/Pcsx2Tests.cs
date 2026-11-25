using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using EdenDeck.Backends.Pcsx2;
using EdenDeck.Core;
using EdenDeck.Core.Backends;
using EdenDeck.Core.Launch;
using EdenDeck.Core.Library;
using EdenDeck.Core.Profiles;
using Xunit;

namespace EdenDeck.Backends.Pcsx2.Tests;

public sealed class Pcsx2Tests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "scanline-pcsx2-" + Guid.NewGuid().ToString("N")[..10]);
    private readonly Dictionary<string, string?> _vars = new();

    public Pcsx2Tests()
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

    private Pcsx2Backend Backend() => new(new AppPaths(Env), env: Env);

    // ---- a tiny ISO9660 image: SYSTEM.CNF and a boot "ELF" in the root ----

    public static readonly byte[] Elf = Enumerable.Range(0, 6000).Select(i => (byte)(i * 7 + 3)).ToArray();

    public static uint ExpectedCrc()
    {
        uint crc = 0;
        for (var i = 0; i + 4 <= Elf.Length; i += 4)
        {
            crc ^= BitConverter.ToUInt32(Elf, i);
        }
        return crc;
    }

    private static void DirRecord(byte[] buf, ref int pos, uint extent, uint size, bool dir, string id)
    {
        var name = Encoding.ASCII.GetBytes(id);
        var len = 33 + name.Length + (name.Length % 2 == 0 ? 1 : 0);
        buf[pos] = (byte)len;
        BitConverter.GetBytes(extent).CopyTo(buf, pos + 2);
        BitConverter.GetBytes(size).CopyTo(buf, pos + 10);
        buf[pos + 25] = (byte)(dir ? 2 : 0);
        buf[pos + 32] = (byte)name.Length;
        name.CopyTo(buf, pos + 33);
        pos += len;
    }

    /// <summary>A 2048-byte-sector image with SYSTEM.CNF (<paramref name="cnf"/>) and the ELF as SLUS_209.46.</summary>
    public static byte[] Iso(string cnf = "BOOT2 = cdrom0:\\SLUS_209.46;1\r\nVER = 1.03\r\nVMODE = NTSC\r\n")
    {
        const int S = 2048;
        var cnfBytes = Encoding.ASCII.GetBytes(cnf);
        var elfSectors = (Elf.Length + S - 1) / S;
        var iso = new byte[(21 + elfSectors) * S];
        var pvd = 16 * S;
        iso[pvd] = 1;
        Encoding.ASCII.GetBytes("CD001").CopyTo(iso, pvd + 1);
        iso[pvd + 6] = 1;
        var p = pvd + 156;
        DirRecord(iso, ref p, 18, S, true, "\0");
        iso[17 * S] = 255;
        Encoding.ASCII.GetBytes("CD001").CopyTo(iso, 17 * S + 1);
        var root = new byte[S];
        var r = 0;
        DirRecord(root, ref r, 18, S, true, "\0");
        DirRecord(root, ref r, 18, S, true, "\u0001");
        DirRecord(root, ref r, 19, (uint)cnfBytes.Length, false, "SYSTEM.CNF;1");
        DirRecord(root, ref r, 20, (uint)Elf.Length, false, "SLUS_209.46;1");
        root.CopyTo(iso, 18 * S);
        cnfBytes.CopyTo(iso, 19 * S);
        Elf.CopyTo(iso, 20 * S);
        return iso;
    }

    /// <summary>The same image as raw 2352-byte mode 2 sectors (sync, header, subheader, data, EDC/ECC).</summary>
    public static byte[] RawBin(byte[] iso)
    {
        var sectors = iso.Length / 2048;
        var bin = new byte[sectors * 2352];
        for (var i = 0; i < sectors; i++)
        {
            var o = i * 2352;
            bin[o] = 0;
            for (var k = 1; k <= 10; k++)
            {
                bin[o + k] = 0xFF;
            }
            bin[o + 15] = 2;
            Array.Copy(iso, i * 2048, bin, o + 24, 2048);
        }
        return bin;
    }

    /// <summary>A CSO (deflate) or ZSO (LZ4, literals only) of the image, every other block stored as-is.</summary>
    public static byte[] Compressed(byte[] iso, bool zso)
    {
        const int block = 2048;
        var blocks = iso.Length / block;
        var header = new byte[24];
        Encoding.ASCII.GetBytes(zso ? "ZISO" : "CISO").CopyTo(header, 0);
        BitConverter.GetBytes(24u).CopyTo(header, 4);
        BitConverter.GetBytes((long)iso.Length).CopyTo(header, 8);
        BitConverter.GetBytes((uint)block).CopyTo(header, 16);
        header[20] = 1;
        var index = new uint[blocks + 1];
        var data = new MemoryStream();
        var start = 24 + (blocks + 1) * 4;
        for (var i = 0; i < blocks; i++)
        {
            var chunk = iso.AsSpan(i * block, block).ToArray();
            index[i] = (uint)(start + data.Length);
            if (i % 2 == 1)
            {
                index[i] |= 0x80000000;
                data.Write(chunk);
                continue;
            }
            if (zso)
            {
                data.WriteByte(0xF0);  // 15+ literals, no match
                var rest = chunk.Length - 15;
                while (rest >= 255)
                {
                    data.WriteByte(255);
                    rest -= 255;
                }
                data.WriteByte((byte)rest);
                data.Write(chunk);
            }
            else
            {
                using var z = new DeflateStream(data, CompressionLevel.Optimal, leaveOpen: true);
                z.Write(chunk);
            }
        }
        index[blocks] = (uint)(start + data.Length);
        var result = new MemoryStream();
        result.Write(header);
        result.Write(MemoryMarshal.AsBytes(index.AsSpan()));
        result.Write(data.ToArray());
        return result.ToArray();
    }

    private string WriteFile(string relative, byte[] bytes)
    {
        var path = Path.Combine(_home, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void ReadsSerialVersionAndElfCrcFromAnIso()
    {
        var info = Ps2Disc.Read(WriteFile("g/game.iso", Iso()));
        Assert.NotNull(info);
        Assert.Equal("SLUS-20946", info.Serial);
        Assert.Equal("1.03", info.Version);
        Assert.Equal(ExpectedCrc(), info.Crc);
    }

    [Fact]
    public void ReadsRawBinThroughItsCue()
    {
        WriteFile("g/Game (Track 1).bin", RawBin(Iso()));
        var cue = Path.Combine(_home, "g", "Game.cue");
        File.WriteAllText(cue, "FILE \"Game (Track 1).bin\" BINARY\n  TRACK 01 MODE2/2352\n    INDEX 01 00:00:00\n");
        var info = Ps2Disc.Read(cue);
        Assert.Equal("SLUS-20946", info?.Serial);
        Assert.Equal(ExpectedCrc(), info?.Crc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsCsoAndZso(bool zso)
    {
        var info = Ps2Disc.Read(WriteFile(zso ? "g/game.zso" : "g/game.cso", Compressed(Iso(), zso)));
        Assert.Equal("SLUS-20946", info?.Serial);
        Assert.Equal(ExpectedCrc(), info?.Crc);
    }

    [Fact]
    public void Lz4BlockCopiesOverlappingMatches()
    {
        // "ab" as literals, then a 6-byte match at offset 2: "abababab".
        byte[] src = [0x22, (byte)'a', (byte)'b', 0x02, 0x00];
        var dst = new byte[16];
        var n = Ps2Disc.Lz4Block(src, dst);
        Assert.Equal("abababab", Encoding.ASCII.GetString(dst, 0, n));
    }

    [Theory]
    [InlineData("cdrom0:\\SLUS_209.46;1", "SLUS-20946")]
    [InlineData("cdrom0:\\SCES_503.30;1", "SCES-50330")]
    [InlineData("cdrom:SLPM_650.02", "SLPM-65002")]
    [InlineData("cdrom0:\\GAME\\MAIN.ELF;1", null)]
    public void SerialsComeFromTheBootPath(string boot, string? serial) => Assert.Equal(serial, Ps2Disc.Serial(boot));

    [Fact]
    public void Ps1DiscsAreNotPs2Games()
    {
        var path = WriteFile("g/ps1.iso", Iso("BOOT = cdrom:\\SLUS_000.01;1\r\n"));
        Assert.Null(Ps2Disc.Read(path));
        Assert.Null(Backend().TryRead(path));
    }

    [Fact]
    public void LibraryEntryUsesTheRedumpName()
    {
        var path = WriteFile("g/gtasa.iso", Iso());
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-1));
        var e = Backend().TryRead(path);
        Assert.NotNull(e);
        Assert.Equal("SLUS-20946", e.TitleId);
        Assert.Equal("Grand Theft Auto - San Andreas", e.Title);
        Assert.Equal("1.03", e.Version);
    }

    [Theory]
    [InlineData("Grand Theft Auto - San Andreas (USA) (v1.03)", "Grand Theft Auto - San Andreas")]
    [InlineData("0 Story (Japan) (Disc 2)", "0 Story (Disc 2)")]
    [InlineData("Getaway, The (Europe) (En,Fr,De)", "The Getaway")]
    [InlineData("shadow_of_rome", "Shadow Of Rome")]
    public void TitlesAreCleaned(string name, string title) => Assert.Equal(title, Ps2Names.CleanTitle(name));

    /// <summary>A v5 CHD header with this raw SHA-1 and one metadata entry ("DVD " or "CHT2").</summary>
    private static byte[] Chd(string rawSha1, string metaTag)
    {
        var h = new byte[124 + 16 + 8];
        Encoding.ASCII.GetBytes("MComprHD").CopyTo(h, 0);
        h[11] = 124;
        h[15] = 5;
        h[0x30 + 7] = 124;  // metadata right after the header
        Convert.FromHexString(rawSha1).CopyTo(h, 0x40);
        Encoding.ASCII.GetBytes(metaTag).CopyTo(h, 124);
        return h;
    }

    [Fact]
    public void ChdsAreKnownByTheirDvdHashOrTheirFolder()
    {
        var b = Backend();
        var known = WriteFile("roms/misc/gtasa.chd", Chd("31933aae559944c9" + new string('0', 24), "DVD "));
        Assert.Equal("SLUS-20946", b.TryRead(known)?.TitleId);
        Assert.Equal("Grand Theft Auto - San Andreas", b.TryRead(known)?.Title);

        var cdPs1 = WriteFile("roms/psx/Some Game.chd", Chd(new string('1', 40), "CHT2"));
        Assert.Null(b.TryRead(cdPs1));
        var cdAnywhere = WriteFile("roms/misc/Another Game.chd", Chd(new string('2', 40), "CHT2"));
        Assert.Null(b.TryRead(cdAnywhere));

        var cdPs2 = WriteFile("roms/ps2/Ps2 Cd Game (USA).chd", Chd(new string('3', 40), "CHT2"));
        var e = b.TryRead(cdPs2);
        Assert.NotNull(e);
        Assert.Equal("", e.TitleId);
        Assert.Equal("Ps2 Cd Game", e.Title);

        var dvdUnknown = WriteFile("roms/misc/Homebrew Disc.chd", Chd(new string('4', 40), "DVD "));
        Assert.NotNull(b.TryRead(dvdUnknown));
        var psp = WriteFile("roms/psp/Psp Game.chd", Chd(new string('5', 40), "DVD "));
        Assert.Null(b.TryRead(psp));
    }

    [Fact]
    public void ReadsRealChdHeaders()
    {
        var dvd = ChdInfo.Read(Path.Combine(AppContext.BaseDirectory, "Data", "tiny-dvd.chd"));
        Assert.NotNull(dvd);
        Assert.True(dvd.Dvd);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Iso())).ToLowerInvariant(), dvd.RawSha1);  // the .iso's own SHA-1
        var cd = ChdInfo.Read(Path.Combine(AppContext.BaseDirectory, "Data", "tiny-cd.chd"));
        Assert.NotNull(cd);
        Assert.False(cd.Dvd);
    }

    private const string Log = """
        [    0.1234] Loading config from /x/PCSX2.ini.
        [    1.0000] Disc changed to game.chd.
        [    1.0000]   Name: Grand Theft Auto - San Andreas
        [    1.0001]   Serial: SLUS-20946
        [    1.0001]   Version: 1.03
        [    1.0002]   CRC: 399A49CA
        """;

    [Fact]
    public void LearnsTheCrcFromPcsx2sLogAfterTheFirstRun()
    {
        Assert.Equal(("SLUS-20946", 0x399A49CAu), Pcsx2Discs.ParseLog(Log));
        var b = Backend();
        var chd = WriteFile("ps2/gtasa.chd", Chd("31933aae559944c9" + new string('0', 24), "DVD "));
        var game = b.TryRead(chd)!;
        Assert.Null(b.CrcOf(game));
        Assert.Throws<InvalidOperationException>(() => b.Settings.WritePerGame(game.TitleId, new Dictionary<string, string?> { ["EmuCore/GS/upscale_multiplier"] = "2" }));

        Assert.True(b.Discs.Learn(chd, "SLUS-20946", 0x399A49CA));
        Assert.Equal(0x399A49CAu, b.CrcOf(game));
        b.Settings.WritePerGame(game.TitleId, new Dictionary<string, string?> { ["EmuCore/GS/upscale_multiplier"] = "2" });
        Assert.True(File.Exists(Path.Combine(_home, ".config/PCSX2/gamesettings/SLUS-20946_399A49CA.ini")));
        Assert.Equal("2", Backend().Settings.ReadPerGame("SLUS-20946")["EmuCore/GS/upscale_multiplier"]);
        Assert.Equal("SLUS-20946", Backend().TryRead(chd)?.TitleId);  // a rescan keeps the same game
    }

    [Fact]
    public void GlobalSettingsStartAValidPcsx2Ini()
    {
        var b = Backend();
        b.Settings.WriteGlobal(new Dictionary<string, string?> { ["EmuCore/GS/upscale_multiplier"] = "3", ["EmuCore/EnableWideScreenPatches"] = "true" });
        var ini = Pcsx2Ini.Load(b.Paths.ConfigFile);
        Assert.Equal("1", ini.Get("UI", "SettingsVersion"));  // or PCSX2 resets the file
        Assert.Equal("false", ini.Get("UI", "SetupWizardIncomplete"));
        Assert.Equal("3", ini.Get("EmuCore/GS", "upscale_multiplier"));
        Assert.Equal("true", ini.Get("EmuCore", "EnableWideScreenPatches"));

        File.WriteAllText(b.Paths.ConfigFile, "[UI]\nSettingsVersion = 1\n\n[EmuCore/GS]\nupscale_multiplier = 2.000000\nVsyncEnable = True\n");
        var read = b.Settings.ReadGlobal();
        Assert.Equal("2", read["EmuCore/GS/upscale_multiplier"]);
        Assert.Equal("true", read["EmuCore/GS/VsyncEnable"]);
    }

    [Fact]
    public void IniKeepsRepeatedKeysAndUntouchedLines()
    {
        var ini = Pcsx2Ini.Parse("; comment\n[Cheats]\nEnable = Infinite Health\nEnable = Max Money\n\n[Other]\nKey = Value\n");
        Assert.Equal(["Infinite Health", "Max Money"], ini.GetAll("Cheats", "Enable"));
        ini.SetAll("Cheats", "Enable", ["Max Money", "All Weapons"]);
        ini.Set("New", "A", "1");
        Assert.Equal("; comment\n[Cheats]\nEnable = Max Money\nEnable = All Weapons\n\n[Other]\nKey = Value\n\n[New]\nA = 1\n", ini.ToString());
        ini.Remove("Cheats", "Enable");
        Assert.DoesNotContain("[Cheats]", ini.ToString());
    }

    [Fact]
    public void ControllerIsMappedOnlyWhenPlayerOneIsUnset()
    {
        var b = Backend();
        Assert.True(Pcsx2Pads.Ensure(b.Paths));
        var ini = Pcsx2Ini.Load(b.Paths.ConfigFile);
        Assert.Equal("SDL-0/FaceSouth", ini.Get("Pad1", "Cross"));
        Assert.Equal("SDL-0/+RightTrigger", ini.Get("Pad1", "R2"));
        Assert.Equal("1", ini.Get("UI", "SettingsVersion"));

        File.WriteAllText(b.Paths.ConfigFile, "[UI]\nSettingsVersion = 1\n[Pad1]\nType = DualShock2\nCross = Keyboard/Return\n");
        Assert.True(Pcsx2Pads.Ensure(b.Paths));
        File.WriteAllText(b.Paths.ConfigFile, "[UI]\nSettingsVersion = 1\n[Pad1]\nType = DualShock2\nCross = SDL-1/FaceEast\n");
        Assert.False(Pcsx2Pads.Ensure(b.Paths));
        Assert.Equal("SDL-1/FaceEast", Pcsx2Ini.Load(b.Paths.ConfigFile).Get("Pad1", "Cross"));
    }

    private const string Pnach = """
        gametitle=Grand Theft Auto - San Andreas SLUS_209.46

        [Widescreen 16:9]
        gsaspectratio=16:9
        description=Renders in 16:9.
        author=Someone
        patch=1,EE,00100000,word,00000000

        [No-Interlacing]
        patch=1,EE,00100004,word,00000000

        [Empty group]
        description=No patch lines
        """;

    [Fact]
    public void PatchesAndCheatsAreSwitchedInTheGamesSettings()
    {
        var groups = Pcsx2Patches.Parse(Pnach);
        Assert.Equal(["Widescreen 16:9", "No-Interlacing"], groups.Select(g => g.Name));
        Assert.Equal("Renders in 16:9.", groups[0].Description);
        Assert.Equal("Someone", groups[0].Author);

        var b = Backend();
        var p = b.Patches;
        const uint crc = 0x399A49CA;
        b.Settings.WriteGlobal(new Dictionary<string, string?> { ["EmuCore/EnableWideScreenPatches"] = "true" });
        Assert.True(p.IsPatchOn("SLUS-20946", crc, "Widescreen 16:9"));  // on for every game by the global option
        p.SetPatch("SLUS-20946", crc, "Widescreen 16:9", false);
        p.SetPatch("SLUS-20946", crc, "No-Interlacing", true);
        var game = Pcsx2Ini.Load(b.Paths.GameSettingsFile("SLUS-20946", crc));
        Assert.Equal(["Widescreen 16:9"], game.GetAll("Patches", "Disable"));
        Assert.Equal(["No-Interlacing"], game.GetAll("Patches", "Enable"));
        Assert.False(p.IsPatchOn("SLUS-20946", crc, "Widescreen 16:9"));

        var cheatFile = WriteFile("Downloads/gta.pnach", Encoding.ASCII.GetBytes("[Infinite Health]\npatch=1,EE,20000000,extended,00000001\n[Max Money]\npatch=1,EE,20000004,extended,00000001\n"));
        Assert.Equal(2, p.ImportCheats("SLUS-20946", crc, cheatFile));
        Assert.Throws<InvalidDataException>(() => p.ImportCheats("SLUS-20946", crc, WriteFile("Downloads/x.pnach", "nothing"u8.ToArray())));
        p.SetCheat("SLUS-20946", crc, "Max Money", true);
        Assert.True(p.IsCheatOn("SLUS-20946", crc, "Max Money"));
        Assert.False(p.IsCheatOn("SLUS-20946", crc, "Infinite Health"));
        game = Pcsx2Ini.Load(b.Paths.GameSettingsFile("SLUS-20946", crc));
        Assert.Equal("true", game.Get("EmuCore", "EnableCheats"));
        p.SetCheat("SLUS-20946", crc, "Max Money", false);
        Assert.Null(Pcsx2Ini.Load(b.Paths.GameSettingsFile("SLUS-20946", crc)).Get("EmuCore", "EnableCheats"));
    }

    [Fact]
    public void SavesBackUpTheCardAndThisGamesStates()
    {
        var b = Backend();
        var paths = b.Paths;
        WriteFile(".config/PCSX2/memcards/Mcd001.ps2", [1, 2, 3]);
        WriteFile(".config/PCSX2/sstates/SLUS-20946 (399A49CA).01.p2s", [4]);
        WriteFile(".config/PCSX2/sstates/SLUS-29999 (11111111).01.p2s", [5]);
        var game = new GameEntry { BackendId = "pcsx2", TitleId = "SLUS-20946", Title = "GTA", Path = "/x.iso" };
        Assert.Equal(["memcards/Mcd001.ps2", "sstates/SLUS-20946 (399A49CA).01.p2s"], b.Saves.Saves(game).Select(s => s.Relative));
        var backup = b.Saves.Backup(game)!;
        File.WriteAllBytes(Path.Combine(paths.MemoryCardsDir, "Mcd001.ps2"), [9]);
        b.Saves.Restore(game, backup.Path);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(paths.MemoryCardsDir, "Mcd001.ps2")));
        b.Saves.Delete(game);
        Assert.True(File.Exists(Path.Combine(paths.MemoryCardsDir, "Mcd001.ps2")));  // shared by every game
        Assert.False(File.Exists(Path.Combine(paths.SaveStatesDir, "SLUS-20946 (399A49CA).01.p2s")));
        Assert.True(File.Exists(Path.Combine(paths.SaveStatesDir, "SLUS-29999 (11111111).01.p2s")));

        var bad = Path.Combine(_home, "bad.zip");
        using (var z = ZipFile.Open(bad, ZipArchiveMode.Create))
        {
            z.CreateEntry("sstates/SLUS-29999 (11111111).01.p2s");
        }
        Assert.Throws<InvalidDataException>(() => b.Saves.Restore(game, bad));
    }

    [Fact]
    public void BiosImportChecksTheRomdirAndSelectsIt()
    {
        var rom = new byte[4 << 20];
        Encoding.ASCII.GetBytes("RESET\0").CopyTo(rom, 0x2700);
        Encoding.ASCII.GetBytes("ROMDIR").CopyTo(rom, 0x2710);
        var path = WriteFile("dumps/SCPH-70012.bin", rom);
        var b = Backend();
        Assert.False(b.BiosInstalled);
        Assert.Equal("SCPH-70012.bin", b.ImportBios(path));
        Assert.True(b.BiosInstalled);
        Assert.True(File.Exists(Path.Combine(_home, ".config/PCSX2/bios/SCPH-70012.bin")));
        Assert.Throws<InvalidDataException>(() => b.ImportBios(WriteFile("dumps/notes.bin", new byte[4 << 20])));
    }

    [Fact]
    public void PlanStartsTheDiscInBatchModeAndLearnsItAfterwards()
    {
        var b = Backend();
        var iso = WriteFile("g/gtasa.iso", Iso());
        var game = b.TryRead(iso)!;
        b.Installs.UseExternal(new InstalledEmulator("stable", "2.6.3", WriteFile("Applications/pcsx2.AppImage", [0]), false));
        Assert.Throws<InvalidOperationException>(() => b.Plan(game, new GameProfile(), new LaunchOptions()));  // no BIOS yet
        var rom = new byte[4 << 20];
        Encoding.ASCII.GetBytes("RESET\0").CopyTo(rom, 0x2700);
        Encoding.ASCII.GetBytes("ROMDIR").CopyTo(rom, 0x2710);
        b.ImportBios(WriteFile("dumps/bios.bin", rom));

        var plan = b.Plan(game, new GameProfile(), new LaunchOptions { Gamepads = ["Steam Deck"] });
        Assert.Equal(["-batch", "-nogui", "-fullscreen", "-logfile", b.RunLog, "--", iso], plan.Arguments);
        foreach (var step in plan.Prepare)
        {
            step();
        }
        Assert.Equal("SDL-0/FaceSouth", Pcsx2Ini.Load(b.Paths.ConfigFile).Get("Pad1", "Cross"));
        File.WriteAllText(b.RunLog, Log);
        foreach (var step in plan.Cleanup)
        {
            step();
        }
        Assert.Equal(0x399A49CAu, b.Discs.CrcFor("SLUS-20946"));  // what PCSX2 says wins
    }

    [Fact]
    public void InstallPicksTheLinuxAppImageFromPcsx2sGitHub()
    {
        var release = Pcsx2InstallManager.Parse("""
            {"tag_name":"v2.6.3","published_at":"2026-01-01T00:00:00Z","assets":[
              {"name":"pcsx2-v2.6.3-windows-x64-Qt.7z","size":1,"digest":null,"browser_download_url":"https://github.com/PCSX2/pcsx2/releases/download/v2.6.3/w.7z"},
              {"name":"pcsx2-v2.6.3-linux-appimage-x64-Qt.AppImage","size":90000000,"digest":"sha256:ABCDEF","browser_download_url":"https://github.com/PCSX2/pcsx2/releases/download/v2.6.3/pcsx2-v2.6.3-linux-appimage-x64-Qt.AppImage"}]}
            """);
        var a = Pcsx2InstallManager.ToAvailable(release, Architecture.X64)!;
        Assert.Equal("2.6.3", a.Version);
        Assert.Equal("abcdef", a.Sha256);
        Assert.EndsWith("linux-appimage-x64-Qt.AppImage", a.DownloadUrl);
        Assert.Null(Pcsx2InstallManager.ToAvailable(release, Architecture.Arm64));
        Assert.True(Pcsx2InstallManager.IsOfficial(a.DownloadUrl));
        Assert.False(Pcsx2InstallManager.IsOfficial("https://github.com/someone/pcsx2/releases/download/x.AppImage"));
    }

    [Fact]
    public void IsoFilesGoToWhicheverBackendCanReadThem()
    {
        var rpcs3 = new EdenDeck.Backends.Rpcs3.Rpcs3Backend(new AppPaths(Env), env: Env);
        var library = new LibraryService([rpcs3, Backend()]);
        var iso = WriteFile("games/gtasa.iso", Iso());
        File.SetLastWriteTimeUtc(iso, DateTime.UtcNow.AddHours(-1));
        var games = library.Scan([new LibraryFolder(Path.Combine(_home, "games"))]);
        Assert.Equal("pcsx2:SLUS-20946", Assert.Single(games).GameId);
    }
}
