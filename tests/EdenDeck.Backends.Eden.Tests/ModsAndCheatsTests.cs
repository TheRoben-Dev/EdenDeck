using System.IO.Compression;
using EdenDeck.Backends.Eden.Config;
using EdenDeck.Backends.Eden.Mods;
using Xunit;

namespace EdenDeck.Backends.Eden.Tests;

public class ModsAndCheatsTests
{
    private const string Tid = "0100000000010000";

    private static EdenPaths Paths(TestEnv env) => env.Paths();

    /// <summary>A fixture config, with its load_directory override pointed into the throwaway home.</summary>
    private static TestEnv NewEnv()
    {
        var env = new TestEnv("nightly-467ab644");
        var cfg = Path.Combine(env.Home, ".config", "eden", "qt-config.ini");
        var ini = QtIni.Load(cfg);
        foreach (var section in ini.Sections.ToList())
        {
            if (ini.Get(section, "load_directory") != null)
            {
                ini.Set(section, "load_directory", Path.Combine(env.Home, "mods"));
            }
        }
        ini.Save(cfg);
        Assert.StartsWith(env.Home, env.Paths().LoadDir);
        return env;
    }

    private static string Zip(TestEnv env, string name, params (string Path, string Content)[] files)
    {
        var path = Path.Combine(env.Home, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (p, c) in files)
        {
            using var w = new StreamWriter(zip.CreateEntry(p).Open());
            w.Write(c);
        }
        return path;
    }

    [Theory]
    [InlineData("Better Water/romfs/Shader/water.bin", "Better Water")]          // Mod/romfs
    [InlineData("0100000000010000/HD Pack/romfs/a.bin", "HD Pack")]              // TITLEID/Mod/romfs
    [InlineData("atmosphere/contents/0100000000010000/romfs/a.bin", "pack")]     // atmosphere layout: named after the zip
    [InlineData("romfs/a.bin", "pack")]                                          // parts at the top
    public void InstallsZipsHoweverTheyNestTheMod(string entry, string expected)
    {
        using var env = NewEnv();
        var paths = Paths(env);
        var names = EdenMods.InstallZip(paths, Tid, Zip(env, "pack.zip", (entry, "x"), ("readme.txt", "hi")));
        Assert.Equal([expected], names);
        var mods = EdenMods.List(paths, Tid);
        var mod = Assert.Single(mods);
        Assert.Equal(expected, mod.Name);
        Assert.Equal(["LayeredFS"], mod.Kinds);
        Assert.True(mod.Enabled);
        Assert.False(File.Exists(Path.Combine(mod.Path, "readme.txt")));
        Assert.Empty(Directory.EnumerateDirectories(paths.ModDir(Tid), ".scanline-install-*"));
    }

    [Fact]
    public void RefusesModsForAnotherGameAndDuplicates()
    {
        using var env = NewEnv();
        var paths = Paths(env);
        var other = Zip(env, "other.zip", ("0100000000020000/Mod/romfs/a.bin", "x"));
        Assert.Throws<InvalidDataException>(() => EdenMods.InstallZip(paths, Tid, other));
        Assert.Throws<InvalidDataException>(() => EdenMods.InstallZip(paths, Tid, Zip(env, "empty.zip", ("docs/readme.txt", "x"))));
        var z = Zip(env, "m.zip", ("Mod/exefs/patch.ips", "x"));
        EdenMods.InstallZip(paths, Tid, z);
        Assert.Throws<IOException>(() => EdenMods.InstallZip(paths, Tid, z));
        Assert.Equal(["Mod"], EdenMods.InstallZip(paths, Tid, z, replace: true));
        Assert.Equal(["IPS"], EdenMods.List(paths, Tid)[0].Kinds);
    }

    [Fact]
    public void TogglesThroughEdensDisabledAddOns()
    {
        using var env = NewEnv();
        var paths = Paths(env);
        EdenMods.InstallZip(paths, Tid, Zip(env, "a.zip", ("A/romfs/x.bin", "1")));
        EdenMods.SetEnabled(paths.GlobalConfigFile, Tid, "A", false);
        Assert.False(EdenMods.List(paths, Tid)[0].Enabled);
        var ini = QtIni.Load(paths.GlobalConfigFile);
        Assert.Contains("A", DisabledAddOns.Read(ini)[DisabledAddOns.ParseTitleId(Tid)]);
        EdenMods.SetEnabled(paths.GlobalConfigFile, Tid, "A", true);
        Assert.True(EdenMods.List(paths, Tid)[0].Enabled);
        EdenMods.SetEnabled(paths.GlobalConfigFile, Tid, "A", false);
        EdenMods.Remove(paths, Tid, "A");
        Assert.Empty(EdenMods.List(paths, Tid));
        Assert.False(DisabledAddOns.Read(QtIni.Load(paths.GlobalConfigFile)).ContainsKey(DisabledAddOns.ParseTitleId(Tid)));
    }

    [Fact]
    public void ReportsFilesTwoEnabledModsReplace()
    {
        using var env = NewEnv();
        var paths = Paths(env);
        EdenMods.InstallZip(paths, Tid, Zip(env, "b.zip", ("B/romfs/Data/same.bin", "b"), ("B/exefs/x.ips", "b")));
        EdenMods.InstallZip(paths, Tid, Zip(env, "a.zip", ("A/romfs/Data/same.bin", "a"), ("A/romfs/Data/only-a.bin", "a"), ("A/exefs/y.ips", "a")));
        var conflict = Assert.Single(EdenMods.Conflicts(EdenMods.List(paths, Tid)));
        Assert.Equal("romfs/Data/same.bin", conflict.File);
        Assert.Equal("A", conflict.Winner);
        EdenMods.SetEnabled(paths.GlobalConfigFile, Tid, "B", false);
        Assert.Empty(EdenMods.Conflicts(EdenMods.List(paths, Tid)));
    }

    [Fact]
    public void ParsesAndFormatsCheatText()
    {
        var cheats = CheatText.Parse("{Master}\n580F0000 01234567\n\n[Infinite HP]\n04000000 00ABCDEF 00000063\r\n[--- Items ---]\n\n[Max Coins]\n02100000 619ED858 0000270f\n");
        Assert.Equal(["Master", "Infinite HP", "--- Items ---", "Max Coins"], cheats.Select(c => c.Name));
        Assert.True(cheats[0].IsMaster);
        Assert.False(CheatText.HasCode(cheats[2]));
        Assert.Equal(cheats, CheatText.Parse(CheatText.Format(cheats)));
    }

    [Fact]
    public void ReadsTheDatabaseFormatAndMatchesBuildsByVersion()
    {
        const string cheats = """
            { "3CA12DFAAF9C82DA": { "[9999 Coins]": "[9999 Coins]\n02100000 619ED858 0000270f\n\n", "[--- Section ---]": "[--- Section ---]\n" },
              "B424BE150A8E7D78": { "[Moon Jump]": "[Moon Jump]\n80000040\n20000000\n" } }
            """;
        const string versions = """{ "0": "3CA12DFAAF9C82DA", "262144": "B424BE150A8E7D78", "327680": "B424BE150A8E7D78", "393216": "FFFFFFFFFFFFFFFF", "latest": 393216, "title": "x" }""";
        var g = CheatDatabase.Parse(Tid, cheats, versions);
        Assert.Equal("3CA12DFAAF9C82DA", g.BuildFor(0));
        Assert.Equal(["9999 Coins"], g.For(g.BuildFor(0)).Select(c => c.Name));
        Assert.Equal(["Moon Jump"], g.For(g.BuildFor(327680)).Select(c => c.Name));
        Assert.Empty(g.For(g.BuildFor(393216)));
        Assert.Equal([0u, 262144u, 327680u], g.CoveredVersions);
    }

    [Fact]
    public async Task FetchesOneGameOnRequest()
    {
        var handler = new StubHandler(new()
        {
            ["https://db.test/cheats/0100000000010000.json"] = """{ "3CA12DFAAF9C82DA": { "[A]": "[A]\n04000000 00000000 00000001\n" } }""",
            ["https://db.test/versions/0100000000010000.json"] = """{ "0": "3CA12DFAAF9C82DA" }""",
        });
        var db = new CheatDatabase(new HttpClient(handler), "https://db.test");
        var g = await db.Fetch("0100000000010000");
        Assert.Equal("A", Assert.Single(g!.For(g.BuildFor(0))).Name);
        Assert.Null(await db.Fetch("0100000000099000"));
    }

    [Fact]
    public void SavesPickedCheatsAsOneModEdenReads()
    {
        using var env = NewEnv();
        var paths = Paths(env);
        var picked = new[] { new Cheat("Infinite HP", "04000000 00ABCDEF 00000063") };
        ScanlineCheats.Write(paths, Tid, "3ca12dfaaf9c82da", picked);
        Assert.True(File.Exists(Path.Combine(paths.ModDir(Tid), ScanlineCheats.ModName, "cheats", "3CA12DFAAF9C82DA.txt")));
        Assert.Equal(picked, ScanlineCheats.Read(paths, Tid, "3CA12DFAAF9C82DA"));
        var mod = Assert.Single(EdenMods.List(paths, Tid));
        Assert.Equal(["Cheats"], mod.Kinds);
        ScanlineCheats.Write(paths, Tid, "3CA12DFAAF9C82DA", []);
        Assert.Empty(EdenMods.List(paths, Tid));
        ScanlineCheats.SetEnabledByDefault(paths.GlobalConfigFile, false);
        Assert.False(ScanlineCheats.EnabledByDefault(paths.GlobalConfigFile));
    }

    private sealed class StubHandler(Dictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(responses.TryGetValue(request.RequestUri!.ToString(), out var body)
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) }
                : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }
}
