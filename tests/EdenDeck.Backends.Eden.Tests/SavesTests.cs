using System.IO.Compression;
using EdenDeck.Backends.Eden.Saves;
using Xunit;

namespace EdenDeck.Backends.Eden.Tests;

public class SavesTests
{
    private const string Tid = "0100000000010000";
    private const string Profile = "00000000000000000000000000000001";

    private static (TestEnv Env, EdenPaths Paths, string Data) Setup()
    {
        var env = new TestEnv();
        var paths = env.Paths();
        Assert.StartsWith(env.Home, paths.NandDir);
        return (env, paths, Path.Combine(env.Home, "scanline-data"));
    }

    private static string Save(EdenPaths p, string rel, string content)
    {
        var f = Path.Combine(p.NandDir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.WriteAllText(f, content);
        return f;
    }

    [Fact]
    public void FindsTheGamesSaveFoldersOnly()
    {
        var (env, p, _) = Setup();
        using var _env = env;
        Save(p, $"user/save/0000000000000000/{Profile}/{Tid}/progress.bin", "a");
        Save(p, $"user/save/account/aaaa-bbbb/{Tid}/0/progress.bin", "b");
        Save(p, $"user/save/0000000000000000/{Profile}/0100000000020000/other.bin", "x");  // another game
        Save(p, $"user/save/cache/{Tid}/cache.bin", "c");                                  // cache isn't a save
        Save(p, $"user/save/{Tid}/00000000000000000000000000000000/sys.bin", "s");        // top-level id is a save id
        var found = EdenSaves.Find(p, Tid);
        Assert.Equal([$"user/save/0000000000000000/{Profile}/{Tid}", $"user/save/account/aaaa-bbbb/{Tid}"], found.Select(f => f.Relative));
        Assert.All(found, f => Assert.True(f.Bytes > 0));
    }

    [Fact]
    public void BacksUpRestoresAndUndoes()
    {
        var (env, p, data) = Setup();
        using var _env = env;
        var file = Save(p, $"user/save/0000000000000000/{Profile}/{Tid}/progress.bin", "chapter 1");
        var first = EdenSaves.Backup(p, data, Tid, now: new DateTime(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc))!;
        Assert.Equal("20261122-100000", first.Name);
        File.WriteAllText(file, "chapter 5");
        Save(p, $"user/save/0000000000000000/{Profile}/{Tid}/extra.bin", "new file");

        var safety = EdenSaves.Restore(p, data, Tid, first.Path);
        Assert.Equal("chapter 1", File.ReadAllText(file));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(file)!, "extra.bin")));  // restore replaces, not merges
        Assert.NotNull(safety);

        EdenSaves.Restore(p, data, Tid, safety!.Path);  // undo
        Assert.Equal("chapter 5", File.ReadAllText(file));
        Assert.Equal(3, EdenSaves.Backups(data, Tid).Count);
    }

    [Fact]
    public void NoSaveMeansNoBackup()
    {
        var (env, p, data) = Setup();
        using var _env = env;
        Assert.Null(EdenSaves.Backup(p, data, Tid));
        Assert.Empty(EdenSaves.Backups(data, Tid));
    }

    [Fact]
    public void KeepsOnlyTheLatestAutomaticBackups()
    {
        var (env, p, data) = Setup();
        using var _env = env;
        Save(p, $"user/save/0000000000000000/{Profile}/{Tid}/progress.bin", "x");
        EdenSaves.Backup(p, data, Tid, now: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));  // manual, kept
        for (var i = 0; i < EdenSaves.KeepAutomatic + 3; i++)
        {
            EdenSaves.Backup(p, data, Tid, automatic: true, now: new DateTime(2026, 2, 1, 0, 0, i, DateTimeKind.Utc));
        }
        var all = EdenSaves.Backups(data, Tid);
        Assert.Equal(EdenSaves.KeepAutomatic, all.Count(b => b.Automatic));
        Assert.Single(all, b => !b.Automatic);
        Assert.Equal(new DateTime(2026, 2, 1, 0, 0, EdenSaves.KeepAutomatic + 2, DateTimeKind.Utc), all[0].CreatedUtc);
    }

    [Fact]
    public void RefusesBackupsOfAnotherGameOrUnsafePaths()
    {
        var (env, p, data) = Setup();
        using var _env = env;
        var file = Save(p, $"user/save/0000000000000000/{Profile}/{Tid}/progress.bin", "mine");
        foreach (var entry in new[] { $"user/save/0000000000000000/{Profile}/0100000000020000/x.bin", $"user/save/../../{Tid}/x.bin", "evil.txt" })
        {
            var zip = Path.Combine(env.Home, Guid.NewGuid().ToString("N") + ".zip");
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using var w = new StreamWriter(z.CreateEntry(entry).Open());
                w.Write("x");
            }
            Assert.Throws<InvalidDataException>(() => EdenSaves.Restore(p, data, Tid, zip));
        }
        Assert.Equal("mine", File.ReadAllText(file));
        Assert.Empty(EdenSaves.Backups(data, Tid));  // nothing touched, not even a safety backup
    }

    [Fact]
    public void DeletingSavesKeepsABackup()
    {
        var (env, p, data) = Setup();
        using var _env = env;
        Save(p, $"user/save/0000000000000000/{Profile}/{Tid}/progress.bin", "x");
        var backup = EdenSaves.DeleteSaves(p, data, Tid);
        Assert.NotNull(backup);
        Assert.Empty(EdenSaves.Find(p, Tid));
        EdenSaves.Restore(p, data, Tid, backup!.Path);
        Assert.Single(EdenSaves.Find(p, Tid));
    }
}
