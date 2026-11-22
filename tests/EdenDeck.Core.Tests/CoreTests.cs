using EdenDeck.Core.Launch;
using EdenDeck.Core.Library;
using EdenDeck.Core.Profiles;
using Xunit;

namespace EdenDeck.Core.Tests;

public class FrameGenPolicyTests
{
    [Theory]
    [InlineData(FrameGenMode.Off, 30, false)]
    [InlineData(FrameGenMode.Builtin, 30, true)]
    [InlineData(FrameGenMode.Builtin, null, true)]
    [InlineData(FrameGenMode.Builtin, 60, false)]
    [InlineData(FrameGenMode.LsfgVk, 120, false)]
    public void Only2xTo60(FrameGenMode mode, int? fps, bool enabled)
    {
        var d = FrameGenPolicy.Decide(mode, fps);
        Assert.Equal(enabled, d.Enabled);
        if (enabled)
        {
            Assert.Equal(2, d.Multiplier);
            Assert.Equal(60, d.TargetFps);
        }
    }
}

public class LibraryGroupingTests
{
    private static GameEntry E(string tid, ContentKind kind, string title = "x") => new()
    {
        BackendId = "eden", TitleId = tid, Title = title, Path = "/g/" + tid, Kind = kind,
    };

    [Fact]
    public void GroupsByTitleIdRules()
    {
        var games = LibraryService.Group([
            E("0100AAAA00000000", ContentKind.Base, "Beta"),
            E("0100AAAA00000800", ContentKind.Update),
            E("0100AAAA00001001", ContentKind.Dlc),
            E("0100BBBB00000000", ContentKind.Base, "Alpha"),
            E("0100CCCC00000800", ContentKind.Update),  // orphan update: not a game
        ]);
        Assert.Equal(["Alpha", "Beta"], games.Select(g => g.Base.Title));
        var beta = games[1];
        Assert.Single(beta.Updates);
        Assert.Single(beta.Dlc);
    }

    [Fact]
    public void GameIdsRoundTrip()
    {
        Assert.True(GameIds.TryParse("eden:0100AAAA00000000", out var b, out var t));
        Assert.Equal(("eden", "0100AAAA00000000"), (b, t));
        Assert.False(GameIds.TryParse("nocolon", out _, out _));
    }
}

public class ProfileStoreTests
{
    [Fact]
    public void PersistsProfiles()
    {
        var file = Path.Combine(Path.GetTempPath(), "edendeck-" + Guid.NewGuid() + ".json");
        try
        {
            var s = new ProfileStore(file);
            s.Set("eden:1", new GameProfile { KeySet = "old", FrameGen = FrameGenMode.Builtin, NativeFps = 30 });
            var again = new ProfileStore(file).Get("eden:1");
            Assert.Equal("old", again.KeySet);
            Assert.Equal(FrameGenMode.Builtin, again.FrameGen);
            Assert.Contains("\"Builtin\"", File.ReadAllText(file));
            Assert.Equal(new GameProfile(), new ProfileStore(file).Get("eden:missing"));
        }
        finally
        {
            File.Delete(file);
        }
    }
}

public class ProcessLauncherTests
{
    [Fact]
    public async Task RunsPrepareAndCleanupEvenOnFailure()
    {
        var log = new List<string>();
        var plan = new LaunchPlan
        {
            Executable = "/bin/sh",
            Arguments = ["-c", "exit 3"],
            WorkingDirectory = Path.GetTempPath(),
            Environment = new Dictionary<string, string> { ["X"] = "1" },
            Prepare = [() => log.Add("prepare")],
            Cleanup = [() => log.Add("cleanup")],
        };
        var code = await new ProcessLauncher().RunAsync(plan);
        Assert.Equal(3, code);
        Assert.Equal(["prepare", "cleanup"], log);
    }

    [Fact]
    public void CleanupRunsWhenStartFails()
    {
        var cleaned = false;
        var plan = new LaunchPlan
        {
            Executable = "/nonexistent/eden",
            Arguments = [],
            WorkingDirectory = Path.GetTempPath(),
            Cleanup = [() => cleaned = true],
        };
        Assert.ThrowsAny<Exception>(() => new ProcessLauncher().Start(plan));
        Assert.True(cleaned);
    }

    [Fact]
    public void AppPathsFollowXdg()
    {
        var p = new AppPaths(n => n switch { "HOME" => "/h", "XDG_CONFIG_HOME" => "/c", "XDG_RUNTIME_DIR" => "/run/user/1000", _ => null });
        Assert.Equal("/c/edendeck", p.Config);
        Assert.Equal("/h/.local/share/edendeck", p.Data);
        Assert.Equal("/run/user/1000/edendeck", p.Runtime);
    }
}
