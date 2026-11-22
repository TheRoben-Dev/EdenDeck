using EdenDeck.Backends.Eden.Config;
using Xunit;

namespace EdenDeck.Backends.Eden.Tests;

public class QtIniTests
{
    [Fact]
    public void RoundTripIsLossless()
    {
        foreach (var dir in Directory.EnumerateDirectories(Path.Combine(AppContext.BaseDirectory, "Fixtures")))
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*.ini"))
            {
                var text = File.ReadAllText(f);
                Assert.Equal(text, QtIni.Parse(text).ToString());
            }
        }
    }

    [Fact]
    public void SetUpdatesInPlaceAndAppendsWithinSection()
    {
        var ini = QtIni.Parse("[A]\nx=1\nunknown_future_key=7\n\n[B]\ny=2\n");
        ini.Set("A", "x", "5");
        ini.Set("A", "z", "9");
        ini.Set("C", "w", "hello");
        Assert.Equal("[A]\nx=5\nunknown_future_key=7\nz=9\n\n[B]\ny=2\n\n[C]\nw=hello\n", ini.ToString());
    }

    [Fact]
    public void QuotesSpecialCharactersLikeEden()
    {
        var ini = new QtIni();
        ini.Set("S", "a", "plain value");
        ini.Set("S", "b", "has,comma");
        Assert.Contains("a=plain value\n", ini.ToString());
        Assert.Contains("b=\"has,comma\"\n", ini.ToString());
        Assert.Equal("has,comma", ini.Get("S", "b"));
    }

    [Fact]
    public void BackslashKeysAreLiteral()
    {
        var ini = QtIni.Parse("[Renderer]\nresolution_setup\\default=false\nresolution_setup=6\n");
        Assert.Equal("false", ini.Get("Renderer", "resolution_setup\\default"));
        Assert.Equal("6", ini.Get("Renderer", "resolution_setup"));
    }
}

public class SchemaTests
{
    [Fact]
    public void BothSnapshotsAreEmbedded()
    {
        Assert.Contains(EdenSchema.All, s => s.Label == "v0.2.1");
        Assert.Contains(EdenSchema.All, s => s.Label.StartsWith("nightly-"));
    }

    [Fact]
    public void GpuAccuracyIndexDiffersBetweenVersions()
    {
        var stable = EdenSchema.Select("stable", "v0.2.1");
        var nightly = EdenSchema.Select("nightly", null);
        Assert.Equal(2, stable.EnumIndex("GpuAccuracy", "High"));
        Assert.Equal(1, nightly.EnumIndex("GpuAccuracy", "High"));
        Assert.Null(nightly.EnumIndex("GpuAccuracy", "Medium"));
        Assert.Equal("Medium", stable.EnumName("GpuAccuracy", 1));
        Assert.Equal("High", nightly.EnumName("GpuAccuracy", 1));
    }

    [Fact]
    public void MaxEnumIsNeverAValue()
    {
        var stable = EdenSchema.Select("stable", "v0.2.1");
        Assert.Null(stable.EnumIndex("ScalingFilter", "MaxEnum"));
        Assert.Null(stable.EnumName("ScalingFilter", 13));
    }

    [Fact]
    public void SectionsMatchTranslateCategory()
    {
        var stable = EdenSchema.Select("stable", "v0.2.1");
        var nightly = EdenSchema.Select("nightly", null);
        Assert.Equal("Cpu", stable.Find("fast_cpu_time")!.Section);
        Assert.Equal("System", nightly.Find("fast_cpu_time")!.Section);
        Assert.Equal("Renderer", nightly.Find("resolution_setup")!.Section);
        Assert.Equal("System", nightly.Find("use_docked_mode")!.Section);
        Assert.Null(stable.Find("frame_gen"));
        Assert.True(nightly.Find("frame_gen")!.PerGame);
    }

    [Fact]
    public void UnknownBuildFallsBackToNewestOfChannel()
    {
        Assert.Equal("v0.2.1", EdenSchema.Select("stable", "v9.9.9").Label);
        Assert.StartsWith("nightly-", EdenSchema.Select("nightly", "abc1234").Label);
    }
}

public class SettingsTests
{
    private static EdenSettings Make(TestEnv env, string fixture, bool running = false) =>
        new(() => env.Paths(), () => TestEnv.SchemaFor(fixture), () => running);

    [Theory]
    [MemberData(nameof(TestEnv.Fixtures), MemberType = typeof(TestEnv))]
    public void ReadsGlobalValuesByName(string fixture)
    {
        using var env = new TestEnv(fixture);
        var g = Make(env, fixture).ReadGlobal();
        Assert.Equal("High", g["gpu_accuracy"]);  // stored as 2 on Stable, 1 on Nightly
        Assert.False(g.ContainsKey("use_vsync") && fixture.StartsWith("stable"));  // \default=true -> not overridden
    }

    [Theory]
    [MemberData(nameof(TestEnv.Fixtures), MemberType = typeof(TestEnv))]
    public void WritesGlobalAsVersionIndexAndKeepsUnknownKeys(string fixture)
    {
        using var env = new TestEnv(fixture);
        var s = Make(env, fixture);
        var before = File.ReadAllText(env.ConfigFile);
        s.WriteGlobal(new Dictionary<string, string?> { ["gpu_accuracy"] = "Low", ["resolution_setup"] = "Res2X" });
        var ini = QtIni.Load(env.ConfigFile);
        Assert.Equal("0", ini.Get("Renderer", "gpu_accuracy"));
        Assert.Equal("6", ini.Get("Renderer", "resolution_setup"));
        Assert.Equal("false", ini.Get("Renderer", "resolution_setup\\default"));
        // every untouched line survives
        var after = File.ReadAllText(env.ConfigFile);
        foreach (var line in before.Split('\n').Where(l => !l.StartsWith("gpu_accuracy") && !l.StartsWith("resolution_setup")))
        {
            Assert.Contains(line, after);
        }
        Assert.Equal("Low", s.ReadGlobal()["gpu_accuracy"]);
    }

    [Theory]
    [MemberData(nameof(TestEnv.Fixtures), MemberType = typeof(TestEnv))]
    public void PerGameOverridesUseEdenFormat(string fixture)
    {
        using var env = new TestEnv(fixture);
        var s = Make(env, fixture);
        var tid = "0100000000010000";
        Assert.Equal("High", s.ReadPerGame(tid)["gpu_accuracy"]);

        s.WritePerGame(tid, new Dictionary<string, string?> { ["use_docked_mode"] = "Handheld", ["gpu_accuracy"] = null });
        var ini = QtIni.Load(env.Paths().PerGameConfigFile(tid));
        Assert.Equal("false", ini.Get("System", "use_docked_mode\\use_global"));
        Assert.Equal("0", ini.Get("System", "use_docked_mode"));
        Assert.Equal("true", ini.Get("Renderer", "gpu_accuracy\\use_global"));
        Assert.Null(ini.Get("Renderer", "gpu_accuracy"));
        var read = s.ReadPerGame(tid);
        Assert.Equal("Handheld", read["use_docked_mode"]);
        Assert.False(read.ContainsKey("gpu_accuracy"));
    }

    [Fact]
    public void NewPerGameFileIsCreatedUppercase()
    {
        using var env = new TestEnv("stable-v0.2.1");
        Make(env, "stable-v0.2.1").WritePerGame("0100abcd00002000", new Dictionary<string, string?> { ["resolution_setup"] = "Res3_2X" });
        Assert.True(File.Exists(Path.Combine(env.Home, ".config/eden/custom/0100ABCD00002000.ini")));
    }

    [Fact]
    public void OptionsFollowTheInstalledVersion()
    {
        using var env = new TestEnv();
        var stable = Make(env, "stable-v0.2.1").Options;
        var nightly = Make(env, "nightly-467ab644").Options;
        Assert.Contains(stable.Single(o => o.Id == "gpu_accuracy").Choices, c => c.Value == "Medium");
        Assert.DoesNotContain(nightly.Single(o => o.Id == "gpu_accuracy").Choices, c => c.Value == "Medium");
        Assert.DoesNotContain(stable.Single(o => o.Id == "scaling_filter").Choices, c => c.Value == "Sgsr");
        Assert.Contains(nightly.Single(o => o.Id == "scaling_filter").Choices, c => c.Value == "Sgsr");
    }

    [Fact]
    public void RefusesInvalidValues()
    {
        using var env = new TestEnv("nightly-467ab644");
        var s = Make(env, "nightly-467ab644");
        Assert.Throws<ArgumentException>(() => s.WriteGlobal(new Dictionary<string, string?> { ["gpu_accuracy"] = "Medium" }));
        Assert.Throws<ArgumentException>(() => s.WriteGlobal(new Dictionary<string, string?> { ["gpu_accuracy"] = "2" }));
        Assert.Throws<KeyNotFoundException>(() => s.WriteGlobal(new Dictionary<string, string?> { ["no_such_key"] = "1" }));
    }

    [Fact]
    public void RefusesToWriteWhileEdenRuns()
    {
        using var env = new TestEnv("stable-v0.2.1");
        var s = Make(env, "stable-v0.2.1", running: true);
        Assert.Throws<EdenRunningException>(() => s.WriteGlobal(new Dictionary<string, string?> { ["use_multi_core"] = "true" }));
    }
}

public class DisabledAddOnsTests
{
    [Fact]
    public void ReadsStableFixture()
    {
        var ini = QtIni.Load(Path.Combine(TestEnv.FixtureDir("stable-v0.2.1"), "qt-config.ini"));
        var d = DisabledAddOns.Read(ini);
        Assert.Equal(["60fps Mod", "Update"], d[0x0100000000000000UL]);
    }

    [Fact]
    public void RoundTrips()
    {
        var ini = QtIni.Parse("[DisabledAddOns]\nsize=0\n\n[UI]\nx=1\n");
        var data = new Dictionary<ulong, List<string>>
        {
            [DisabledAddOns.ParseTitleId("0100ABCD00000000")] = ["ModA", "cheat_inf.txt"],
            [DisabledAddOns.ParseTitleId("0100000000010000")] = ["SDMC"],
        };
        DisabledAddOns.Write(ini, data);
        var text = ini.ToString();
        Assert.Contains("1\\title_id=72246490994573312\n", text);  // decimal, like Eden
        Assert.Contains("1\\disabled\\2\\d=cheat_inf.txt\n", text);
        Assert.Contains("[UI]\nx=1\n", text);
        var back = DisabledAddOns.Read(QtIni.Parse(text));
        Assert.Equal(data.Keys.Order(), back.Keys.Order());
        Assert.Equal(["SDMC"], back[0x0100000000010000UL]);
    }
}

public class PathTests
{
    [Fact]
    public void ResolvesXdgDefaults()
    {
        using var env = new TestEnv();
        var p = env.Paths();
        Assert.Equal(Path.Combine(env.Home, ".local/share/eden"), p.DataDir);
        Assert.Equal(Path.Combine(env.Home, ".config/eden"), p.ConfigDir);
        Assert.Equal(Path.Combine(env.Home, ".cache/eden"), p.CacheDir);
        Assert.Equal(Path.Combine(env.Home, ".local/share/eden/keys"), p.KeysDir);
        Assert.Equal(Path.Combine(env.Home, ".local/share/eden/load/0100ABCD00000000"), p.ModDir("0100abcd00000000"));
        Assert.False(p.Portable);
    }

    [Fact]
    public void HonoursXdgVariables()
    {
        using var env = new TestEnv();
        env.Vars["XDG_DATA_HOME"] = "/data";
        env.Vars["XDG_CONFIG_HOME"] = "/cfg";
        var p = env.Paths();
        Assert.Equal("/data/eden", p.DataDir);
        Assert.Equal("/cfg/eden", p.ConfigDir);
    }

    [Fact]
    public void PortableModeWhenCwdHasUserDir()
    {
        using var env = new TestEnv();
        var cwd = Path.Combine(env.Home, "portable");
        Directory.CreateDirectory(Path.Combine(cwd, "user"));
        var p = env.Paths(cwd);
        Assert.True(p.Portable);
        Assert.Equal(Path.Combine(cwd, "user", "config"), p.ConfigDir);
    }

    [Fact]
    public void AppliesDataStorageOverrides()
    {
        using var env = new TestEnv("nightly-467ab644");
        var p = env.Paths();
        Assert.Equal("/mnt/sd/eden-nand", p.NandDir);
        Assert.Equal("/home/deck/Eden Mods", p.LoadDir);
        Assert.Equal("/mnt/sd/eden-nand/system/Contents/registered", p.FirmwareDir);
        Assert.Equal(Path.Combine(env.Home, ".local/share/eden/sdmc"), p.SdmcDir);
    }
}
