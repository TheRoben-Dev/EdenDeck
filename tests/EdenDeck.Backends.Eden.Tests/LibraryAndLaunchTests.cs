using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using EdenDeck.Backends.Eden.Config;
using EdenDeck.Backends.Eden.Install;
using EdenDeck.Backends.Eden.Library;
using EdenDeck.Backends.Eden.SystemFiles;
using EdenDeck.Core.Library;
using EdenDeck.Core.Profiles;
using EdenDeck.Core.Launch;
using EdenDeck.Core.Backends;
using Xunit;

namespace EdenDeck.Backends.Eden.Tests;

/// <summary>Builds synthetic NSPs (PFS0 with fake, properly XTS-encrypted NCA headers).</summary>
internal static class FakeNsp
{
    public static readonly byte[] HeaderKey = RandomNumberGenerator.GetBytes(32);

    public static byte[] NcaHeader(byte contentType, ulong programId, byte keyGeneration = 0)
    {
        var plain = new byte[0xC00];
        Encoding.ASCII.GetBytes("NCA3").CopyTo(plain, 0x200);
        plain[0x205] = contentType;
        plain[0x220] = keyGeneration;
        BinaryPrimitives.WriteUInt64LittleEndian(plain.AsSpan(0x210), programId);
        return NcaHeaderReader.XtsEncrypt(plain, HeaderKey, 0x200);
    }

    public static void Write(string path, params (string name, byte[] data)[] files)
    {
        var strings = new MemoryStream();
        var nameOffsets = new List<int>();
        foreach (var (name, _) in files)
        {
            nameOffsets.Add((int)strings.Length);
            strings.Write(Encoding.UTF8.GetBytes(name + "\0"));
        }
        while (strings.Length % 0x20 != 0)
        {
            strings.WriteByte(0);
        }
        using var fs = File.Create(path);
        var w = new BinaryWriter(fs);
        w.Write(Encoding.ASCII.GetBytes("PFS0"));
        w.Write(files.Length);
        w.Write((int)strings.Length);
        w.Write(0);
        long offset = 0;
        for (var i = 0; i < files.Length; i++)
        {
            w.Write(offset);
            w.Write((long)files[i].data.Length);
            w.Write(nameOffsets[i]);
            w.Write(0);
            offset += files[i].data.Length;
        }
        w.Write(strings.ToArray());
        foreach (var (_, data) in files)
        {
            w.Write(data);
        }
    }
}

public class LibraryTests : IDisposable
{
    private readonly TestEnv _env = new();

    public void Dispose() => _env.Dispose();

    [Fact]
    public void XtsRoundTrip()
    {
        var data = RandomNumberGenerator.GetBytes(0xC00);
        var enc = NcaHeaderReader.XtsEncrypt(data, FakeNsp.HeaderKey, 0x200);
        Assert.NotEqual(data, enc);
        Assert.Equal(data, NcaHeaderReader.XtsDecrypt(enc, FakeNsp.HeaderKey, 0x200));
    }

    [Fact]
    public void XtsMatchesIeeeVector()
    {
        // IEEE 1619 XTS-AES-128 vector 4 (first block), data unit 0: key1 = 2718..., key2 = 3141...
        var key = Convert.FromHexString("27182818284590452353602874713526" + "31415926535897932384626433832795");
        var plain = new byte[512];
        for (var i = 0; i < 512; i++)
        {
            plain[i] = (byte)i;
        }
        var enc = NcaHeaderReader.XtsEncrypt(plain, key, 512);
        // Nintendo's tweak is the big-endian sector number; for sector 0 that equals IEEE's.
        Assert.Equal("27A7479BEFA1D476489F308CD4CFA6E2", Convert.ToHexString(enc, 0, 16));
    }

    [Fact]
    public void ReadsTitleIdFromMetaNcaHeader()
    {
        var path = Path.Combine(_env.Home, "Some Game [v0].nsp");
        FakeNsp.Write(path,
            ("aaaa.nca", FakeNsp.NcaHeader(0, 0x0100ABCD00000000)),
            ("bbbb.cnmt.nca", FakeNsp.NcaHeader(1, 0x0100ABCD00000000)));
        var scanner = new EdenLibraryScanner(() => FakeNsp.HeaderKey);
        var e = scanner.TryRead(path)!;
        Assert.Equal("0100ABCD00000000", e.TitleId);
        Assert.Equal("nca-header", e.TitleIdSource);
        Assert.Equal(ContentKind.Base, e.Kind);
        Assert.Equal("Some Game", e.Title);
        Assert.Equal("eden:0100ABCD00000000", e.GameId);
    }

    [Fact]
    public void UpdateAndDlcAreRecognised()
    {
        var upd = Path.Combine(_env.Home, "upd.nsp");
        FakeNsp.Write(upd, ("m.cnmt.nca", FakeNsp.NcaHeader(1, 0x0100ABCD00000800)));
        var dlc = Path.Combine(_env.Home, "dlc.nsp");
        FakeNsp.Write(dlc, ("m.cnmt.nca", FakeNsp.NcaHeader(1, 0x0100ABCD00001001)));
        var s = new EdenLibraryScanner(() => FakeNsp.HeaderKey);
        Assert.Equal(ContentKind.Update, s.TryRead(upd)!.Kind);
        Assert.Equal(ContentKind.Dlc, s.TryRead(dlc)!.Kind);
    }

    [Fact]
    public void WrongKeyFallsBackToFilename()
    {
        var path = Path.Combine(_env.Home, "Game Name [0100ABCD00000000][v65536].nsp");
        FakeNsp.Write(path, ("m.cnmt.nca", FakeNsp.NcaHeader(1, 0x0100ABCD00000000)));
        var e = new EdenLibraryScanner(() => RandomNumberGenerator.GetBytes(32)).TryRead(path)!;
        Assert.Equal("0100ABCD00000000", e.TitleId);
        Assert.Equal("filename", e.TitleIdSource);
        Assert.Equal("65536", e.Version);
        Assert.Equal("Game Name", e.Title);
    }

    [Fact]
    public void NoKeysNoTagIsStillListed()
    {
        var path = Path.Combine(_env.Home, "mystery.xci");
        File.WriteAllBytes(path, new byte[0x400]);
        var e = new EdenLibraryScanner(() => null).TryRead(path)!;
        Assert.Equal("", e.TitleId);
        Assert.Equal(ContentKind.Unknown, e.Kind);
    }

    [Fact]
    public void GroupsUpdatesAndDlcUnderBase()
    {
        var dir = Path.Combine(_env.Home, "games");
        Directory.CreateDirectory(dir);
        FakeNsp.Write(Path.Combine(dir, "base.nsp"), ("m.cnmt.nca", FakeNsp.NcaHeader(1, 0x0100ABCD00000000)));
        FakeNsp.Write(Path.Combine(dir, "upd.nsp"), ("m.cnmt.nca", FakeNsp.NcaHeader(1, 0x0100ABCD00000800)));
        FakeNsp.Write(Path.Combine(dir, "dlc.nsp"), ("m.cnmt.nca", FakeNsp.NcaHeader(1, 0x0100ABCD00001003)));
        File.WriteAllText(Path.Combine(dir, "readme.txt"), "ignored");
        var backend = new FakeBackend(new EdenLibraryScanner(() => FakeNsp.HeaderKey));
        var lib = new LibraryService([backend]) { InProgressWindow = TimeSpan.Zero }.Scan([new LibraryFolder(dir)]);
        var g = Assert.Single(lib);
        Assert.Single(g.Updates);
        Assert.Single(g.Dlc);
    }

    [Fact]
    public void EachGameIsListedOnceAndCopiesInProgressAreSkipped()
    {
        var dir = Path.Combine(_env.Home, "games");
        var sub = Path.Combine(dir, "switch");
        Directory.CreateDirectory(sub);
        var old = DateTime.UtcNow.AddMinutes(-5);
        void Nsp(string path, ulong tid)
        {
            FakeNsp.Write(path, ("m.cnmt.nca", FakeNsp.NcaHeader(1, tid)));
            File.SetLastWriteTimeUtc(path, old);
        }
        Nsp(Path.Combine(sub, "game.nsp"), 0x0100ABCD00000000);
        Nsp(Path.Combine(sub, "game copy.nsp"), 0x0100ABCD00000000);       // second copy of the same title
        Nsp(Path.Combine(sub, "game [v65536].nsp"), 0x0100ABCD00000800);
        Nsp(Path.Combine(sub, "game again [v65536].nsp"), 0x0100ABCD00000800);
        FakeNsp.Write(Path.Combine(sub, "other.nsp"), ("m.cnmt.nca", FakeNsp.NcaHeader(1, 0x0100EEEE00000000)));  // still copying
        File.WriteAllBytes(Path.Combine(sub, "third.nsp.filepart"), new byte[16]);
        var alias = Path.Combine(_env.Home, "alias");
        Directory.CreateSymbolicLink(alias, dir);                           // like /home -> /var/home on Bazzite

        var backend = new FakeBackend(new EdenLibraryScanner(() => FakeNsp.HeaderKey));
        var service = new LibraryService([backend]);
        var lib = service.Scan([new LibraryFolder(dir), new LibraryFolder(alias + "/"), new LibraryFolder(sub)]);

        var g = Assert.Single(lib);
        Assert.Equal("0100ABCD00000000", g.Base.TitleId);
        Assert.Single(g.Updates);
        Assert.Equal(2, service.LastSkippedInProgress);
    }

    [Fact]
    public void KeysTooOldForGameAreReportedBeforeLaunch()
    {
        var game = Path.Combine(_env.Home, "new.nsp");
        FakeNsp.Write(game, ("m.cnmt.nca", FakeNsp.NcaHeader(1, 0x0100ABCD00000000, keyGeneration: 0x13)),
                            ("p.nca", FakeNsp.NcaHeader(0, 0x0100ABCD00000000, keyGeneration: 0x14)));
        Assert.Equal(0x13, KeyRequirements.Required(game, FakeNsp.HeaderKey));

        string Keys(int maxMaster)
        {
            var lines = new List<string> { "header_key = " + Convert.ToHexString(FakeNsp.HeaderKey) };
            for (var i = 0; i <= maxMaster; i++)
            {
                lines.Add($"master_key_{i:x2} = {new string('1', 32)}");
            }
            lines.Add("master_key_14 = " + new string('0', 32));  // all-zero placeholder doesn't count
            var path = Path.Combine(_env.Home, $"prod{maxMaster}.keys");
            File.WriteAllLines(path, lines);
            return path;
        }

        var e = Assert.Throws<KeysTooOldException>(() => KeyRequirements.Check("New Game", [game], Keys(0x11), "Your keys"));
        Assert.Equal(0x13, e.Needed);
        Assert.Equal(0x11, e.Have);
        Assert.Contains("firmware 20.0.0", e.Message);
        Assert.Contains("firmware 18.0.0", e.Message);
        KeyRequirements.Check("New Game", [game], Keys(0x13), "Your keys");  // new enough: no throw
        KeyRequirements.Check("New Game", [game], Path.Combine(_env.Home, "missing.keys"), "Your keys");
    }

    private sealed class FakeBackend(ILibraryScanner scanner) : IEmulatorBackend
    {
        public string Id => "eden";
        public string DisplayName => "Eden";
        public string SystemName => "Switch";
        public IReadOnlyCollection<string> FileExtensions => EdenLibraryScanner.Extensions;
        public IInstallManager Install => throw new NotSupportedException();
        public ILibraryScanner Library => scanner;
        public ISettingsProvider Settings => throw new NotSupportedException();
        public ILaunchPlanner Launch => throw new NotSupportedException();
        public ISystemFiles? SystemFiles => null;
    }
}

public class KeySetTests : IDisposable
{
    private readonly TestEnv _env = new();

    public void Dispose() => _env.Dispose();

    private string WriteProdKeys(string name = "prod.keys", bool withHeaderKey = true)
    {
        var p = Path.Combine(_env.Home, name);
        File.WriteAllText(p, (withHeaderKey ? $"header_key = {Convert.ToHexString(FakeNsp.HeaderKey).ToLowerInvariant()}\n" : "") +
                             "master_key_00 = 00112233445566778899aabbccddeeff\n");
        return p;
    }

    [Fact]
    public void ImportValidatesProdKeys()
    {
        var store = new KeySetStore(Path.Combine(_env.Home, "keysets"));
        Assert.Throws<KeyImportException>(() => store.Import("bad", WriteProdKeys("bad.keys", withHeaderKey: false), null, null));
        Assert.Throws<KeyImportException>(() => store.Import("missing", "/nope/prod.keys", null, null));
        var set = store.Import("FW 19 keys", WriteProdKeys(), null, null);
        Assert.Equal("fw-19-keys", set.Id);
        Assert.Equal("FW 19 keys", store.Get(set.Id)!.Label);
        Assert.False(set.HasFirmware);
    }

    [Fact]
    public void FarmSwapsKeysAndFirmwareOnlyAndCleansUpSafely()
    {
        var real = _env.Paths();
        // Real Eden data: keys, saves, system saves, current firmware, mods.
        Directory.CreateDirectory(real.KeysDir);
        File.WriteAllText(Path.Combine(real.KeysDir, "prod.keys"), "default");
        var userSave = Path.Combine(real.DataDir, "nand", "user", "save", "0000000000000000");
        Directory.CreateDirectory(userSave);
        File.WriteAllText(Path.Combine(userSave, "save.bin"), "precious");
        var sysSave = Path.Combine(real.DataDir, "nand", "system", "save");
        Directory.CreateDirectory(sysSave);
        Directory.CreateDirectory(real.FirmwareDir);
        File.WriteAllText(Path.Combine(real.FirmwareDir, "old.nca"), "fw-old");
        Directory.CreateDirectory(Path.Combine(real.DataDir, "load"));

        var fwSrc = Path.Combine(_env.Home, "fw");
        Directory.CreateDirectory(fwSrc);
        File.WriteAllText(Path.Combine(fwSrc, "new.nca"), "fw-new");
        var set = new KeySetStore(Path.Combine(_env.Home, "keysets")).Import("old games", WriteProdKeys(), null, fwSrc);
        Assert.True(set.HasFirmware);

        var farm = KeySetFarm.Build(real, set, Path.Combine(_env.Home, "run"), "0100ABCD00000000");
        var portable = env(farm.Root);
        Assert.True(portable.Portable);
        Assert.Contains("header_key", File.ReadAllText(Path.Combine(portable.KeysDir, "prod.keys")));
        Assert.True(File.Exists(Path.Combine(portable.FirmwareDir, "new.nca")));
        Assert.False(File.Exists(Path.Combine(portable.FirmwareDir, "old.nca")));
        Assert.Equal("precious", File.ReadAllText(Path.Combine(portable.NandDir, "user", "save", "0000000000000000", "save.bin")));
        Assert.True(Directory.Exists(Path.Combine(portable.NandDir, "system", "save")));
        Assert.Equal(real.ConfigDir, new DirectoryInfo(portable.ConfigDir).LinkTarget);

        // Writes through the farm land in the real dirs (shared saves).
        File.WriteAllText(Path.Combine(portable.NandDir, "user", "save", "0000000000000000", "new.bin"), "x");
        Assert.True(File.Exists(Path.Combine(userSave, "new.bin")));

        farm.Remove();
        Assert.False(Directory.Exists(farm.Root));
        Assert.Equal("precious", File.ReadAllText(Path.Combine(userSave, "save.bin")));
        Assert.Equal("default", File.ReadAllText(Path.Combine(real.KeysDir, "prod.keys")));
        Assert.True(File.Exists(Path.Combine(real.FirmwareDir, "old.nca")));
        Assert.True(File.Exists(Path.Combine(set.Directory, "firmware", "new.nca")));

        EdenPaths env(string cwd) => _env.Paths(cwd);
    }
}

public class ReleaseFeedTests
{
    private const string Json = """
        {"tag_name":"v0.2.2","name":"Eden v0.2.2","published_at":"2026-11-01T00:00:00Z","assets":[
          {"name":"Eden-Windows-v0.2.2-amd64-msvc-standard.zip","size":1,"browser_download_url":"https://git.eden-emu.dev/a.zip"},
          {"name":"Eden-Linux-v0.2.2-aarch64-standard.AppImage","size":2,"browser_download_url":"https://git.eden-emu.dev/b.AppImage"},
          {"name":"Eden-Linux-v0.2.2-amd64-pgo.AppImage","size":3,"browser_download_url":"https://git.eden-emu.dev/c.AppImage"},
          {"name":"Eden-Linux-v0.2.2-amd64-standard.AppImage","size":4,"digest":"sha256:ABCDEF","browser_download_url":"https://git.eden-emu.dev/d.AppImage"}
        ]}
        """;

    [Fact]
    public void PicksStandardAppImageForArch()
    {
        var r = EdenReleaseFeed.Parse(Json);
        Assert.Equal("Eden-Linux-v0.2.2-amd64-standard.AppImage", EdenReleaseFeed.SelectAppImage(r, Architecture.X64)!.Name);
        Assert.Equal("Eden-Linux-v0.2.2-aarch64-standard.AppImage", EdenReleaseFeed.SelectAppImage(r, Architecture.Arm64)!.Name);
        var a = EdenReleaseFeed.ToAvailable("stable", r, Architecture.X64)!;
        Assert.Equal("abcdef", a.Sha256);
        Assert.Equal("v0.2.2", a.Version);
    }

    [Theory]
    [InlineData("https://git.eden-emu.dev/eden-emu/eden/releases/download/x.AppImage", true)]
    [InlineData("https://nightly.eden-emu.dev/x.AppImage", true)]
    [InlineData("https://github.com/eden-emulator/Releases/releases/download/x.AppImage", true)]
    [InlineData("http://git.eden-emu.dev/x.AppImage", false)]
    [InlineData("https://eden-emu.dev.evil.example/x.AppImage", false)]
    [InlineData("https://github.com/someone/eden/releases/x.AppImage", false)]
    public void OnlyOfficialHosts(string url, bool ok) => Assert.Equal(ok, EdenInstallManager.IsOfficial(url));

    [Fact]
    public void ExternalFileNameParsing()
    {
        var e = EdenInstallManager.FromFile("/home/deck/Applications/Eden-Linux-v0.2.1-amd64.AppImage", false);
        Assert.Equal("stable", e.Channel);
        Assert.Equal("v0.2.1", e.Version);
        Assert.Equal("nightly", EdenInstallManager.FromFile("/x/Eden-Nightly-abc1234.AppImage", false).Channel);
    }
}

public class LaunchTests : IDisposable
{
    private readonly TestEnv _env = new("nightly-467ab644");

    public void Dispose() => _env.Dispose();

    private EdenBackend Backend(string channel)
    {
        var app = _env.AppPaths();
        var exe = Path.Combine(_env.Home, "Applications", $"Eden-{channel}.AppImage");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, "#!/bin/sh\n");
        var b = new EdenBackend(app);
        b.Install.UseExternal(new InstalledEmulator(channel, channel == "nightly" ? "nightly-467ab644" : "v0.2.1", exe, false));
        return b;
    }

    private static GameEntry Game(string tid = "0100000000010000") => new()
    {
        BackendId = "eden", TitleId = tid, Title = "Test", Path = "/games/Test Game.nsp",
    };

    [Fact]
    public void UsesOnlyPortableFlags()
    {
        var plan = Backend("nightly").Launch.Plan(Game(), new GameProfile(), new LaunchOptions());
        Assert.Equal(["-f", "-g", "/games/Test Game.nsp"], plan.Arguments);
        Assert.False(Directory.Exists(Path.Combine(plan.WorkingDirectory, "user")));
    }

    [Fact]
    public void FrameGenIsCappedAt60And2x()
    {
        var b = Backend("nightly");
        // Eden must look closed for config writes in this test process.
        var plan = b.Launch.Plan(Game("0100ABCD00000000"), new GameProfile { FrameGen = FrameGenMode.Builtin, NativeFps = 30 }, new LaunchOptions());
        foreach (var p in plan.Prepare)
        {
            p();
        }
        var ini = QtIni.Load(b.Paths.PerGameConfigFile("0100ABCD00000000"));
        Assert.Equal("true", ini.Get("Renderer", "frame_gen"));
        Assert.Equal("2", ini.Get("Renderer", "frame_gen_multiplier"));
        Assert.Equal("60", ini.Get("Renderer", "frame_gen_target_rate"));
        Assert.Contains(plan.Notes, n => n.Contains("30 -> 60"));
    }

    [Fact]
    public void NoFrameGenForNative60Titles()
    {
        var b = Backend("nightly");
        var plan = b.Launch.Plan(Game("0100ABCD00000000"), new GameProfile { FrameGen = FrameGenMode.Builtin, NativeFps = 60 }, new LaunchOptions());
        foreach (var p in plan.Prepare)
        {
            p();
        }
        Assert.Equal("false", QtIni.Load(b.Paths.PerGameConfigFile("0100ABCD00000000")).Get("Renderer", "frame_gen"));
    }

    [Fact]
    public void FrameGenUnavailableOnStable()
    {
        var plan = Backend("stable").Launch.Plan(Game(), new GameProfile { FrameGen = FrameGenMode.Builtin }, new LaunchOptions());
        Assert.Contains(plan.Notes, n => n.Contains("Nightly only"));
        Assert.Empty(plan.Prepare.Skip(1));
    }

    [Fact]
    public void InjectsLayerEnvOnlyWhenManifestExists()
    {
        var layerDir = Path.Combine(_env.Home, "layer");
        Directory.CreateDirectory(layerDir);
        var b = Backend("nightly");
        var none = b.Launch.Plan(Game(), new GameProfile(), new LaunchOptions { LayerDirectory = layerDir });
        Assert.False(none.Environment.ContainsKey("VK_INSTANCE_LAYERS"));
        File.WriteAllText(Path.Combine(layerDir, "VkLayer_EDENDECK_fsr.json"), "{}");
        var plan = b.Launch.Plan(Game(), new GameProfile(), new LaunchOptions { LayerDirectory = layerDir, LayerKillSwitch = true });
        Assert.StartsWith(layerDir, plan.Environment["VK_ADD_LAYER_PATH"]);
        Assert.StartsWith("VK_LAYER_EDENDECK_fsr", plan.Environment["VK_INSTANCE_LAYERS"]);
        Assert.Equal("1", plan.Environment["DISABLE_EDENDECK_LAYER"]);
    }
}
