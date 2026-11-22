using EdenDeck.Backends.Eden.Storage;
using EdenDeck.Core.Library;
using Xunit;

namespace EdenDeck.Backends.Eden.Tests;

public class StorageTests
{
    [Fact]
    public void ListsAndClearsShaderCaches()
    {
        using var env = new TestEnv();
        var p = env.Paths();
        Assert.StartsWith(env.Home, p.CacheDir);
        void Write(string rel, int bytes)
        {
            var f = Path.Combine(ShaderCache.Root(p), rel);
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            File.WriteAllBytes(f, new byte[bytes]);
        }
        Write("0100000000010000/vulkan.bin", 300);
        Write("0100000000010000/vulkan_pipelines.bin", 100);
        Write("0100000000020000/vulkan.bin", 1000);
        Write("not-a-game/keep.bin", 50);

        var all = ShaderCache.All(p);
        Assert.Equal(["0100000000020000", "0100000000010000"], all.Select(e => e.TitleId));
        Assert.Equal(400, ShaderCache.Bytes(p, "0100000000010000"));
        Assert.Equal(400, ShaderCache.Clear(p, "0100000000010000"));
        Assert.Equal(0, ShaderCache.Bytes(p, "0100000000010000"));
        Assert.Equal(1000, ShaderCache.ClearAll(p));
        Assert.Empty(ShaderCache.All(p));
        Assert.True(File.Exists(Path.Combine(ShaderCache.Root(p), "not-a-game/keep.bin")));
        Assert.Equal(0, ShaderCache.Clear(p, "0100000000099000"));  // nothing there is fine
    }

    [Fact]
    public void ReportsAGamesPartsAndOlderUpdates()
    {
        using var env = new TestEnv();
        var p = env.Paths();
        string File(string name, int bytes)
        {
            var f = Path.Combine(env.Home, "games", name);
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            System.IO.File.WriteAllBytes(f, new byte[bytes]);
            return f;
        }
        GameEntry E(string path, ContentKind kind) => new() { BackendId = "eden", TitleId = "0100000000010000", Title = "T", Path = path, Kind = kind };
        var game = new LibraryGame(E(File("base.nsp", 1000), ContentKind.Base),
            [E(File("v2.nsp", 300), ContentKind.Update), E(File("v1.nsp", 200), ContentKind.Update), E(Path.Combine(env.Home, "gone.nsp"), ContentKind.Update)],
            [E(File("dlc.nsp", 50), ContentKind.Dlc)]);
        Directory.CreateDirectory(ShaderCache.Dir(p, "0100000000010000"));
        System.IO.File.WriteAllBytes(Path.Combine(ShaderCache.Dir(p, "0100000000010000"), "vulkan.bin"), new byte[7]);
        var u = StorageReport.For(game, p);
        Assert.Equal((1000, 500, 200, 50, 7), (u.Base, u.Updates, u.OlderUpdates, u.Dlc, u.Shaders));
        Assert.Equal(1557, u.Total);
        Assert.Equal([Path.Combine(env.Home, "games", "v1.nsp")], u.OlderUpdateFiles);  // the missing one is skipped
    }

    [Fact]
    public void FindsTheDriveAFolderIsOn()
    {
        var disks = StorageReport.Disks([Path.GetTempPath(), Path.GetTempPath(), "/"]);
        Assert.NotEmpty(disks);
        Assert.Equal(disks.Count, disks.Select(d => d.Mount).Distinct().Count());
        Assert.All(disks, d => Assert.True(d.Total > 0 && d.Free <= d.Total));
        Assert.Equal("1.5 GB", StorageReport.Format(3L << 29));
        Assert.Equal("12 MB", StorageReport.Format(12L << 20));
        Assert.Equal("1 KB", StorageReport.Format(10));
        Assert.Equal("0 KB", StorageReport.Format(0));
    }
}
