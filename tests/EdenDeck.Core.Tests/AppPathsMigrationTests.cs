using EdenDeck.Core;
using Xunit;

namespace EdenDeck.Core.Tests;

public class AppPathsMigrationTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "scanline-mig-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    private string? Env(string name) => name == "HOME" ? _home : null;

    [Fact]
    public void MovesEdenDeckFoldersAndLeavesLinks()
    {
        var oldData = Path.Combine(_home, ".local/share/edendeck");
        Directory.CreateDirectory(Path.Combine(oldData, "keysets", "mine"));
        File.WriteAllText(Path.Combine(oldData, "keysets", "mine", "prod.keys"), "k");
        var oldConfig = Path.Combine(_home, ".config/edendeck");
        Directory.CreateDirectory(oldConfig);
        File.WriteAllText(Path.Combine(oldConfig, "settings.json"), "{}");

        var moved = AppPaths.MigrateFromEdenDeck(Env);

        Assert.Equal(2, moved.Count);
        var paths = new AppPaths(Env);
        Assert.Equal("k", File.ReadAllText(Path.Combine(paths.KeySetsDir, "mine", "prod.keys")));
        Assert.True(File.Exists(paths.SettingsFile));
        // Old absolute paths still resolve.
        Assert.NotNull(new DirectoryInfo(oldData).LinkTarget);
        Assert.Equal("k", File.ReadAllText(Path.Combine(oldData, "keysets", "mine", "prod.keys")));
        // Second run: nothing to do.
        Assert.Empty(AppPaths.MigrateFromEdenDeck(Env));
    }

    [Fact]
    public void LeavesBothAloneWhenTheNewFolderExists()
    {
        Directory.CreateDirectory(Path.Combine(_home, ".config/edendeck"));
        Directory.CreateDirectory(Path.Combine(_home, ".config/scanline"));
        Assert.Empty(AppPaths.MigrateFromEdenDeck(Env));
        Assert.Null(new DirectoryInfo(Path.Combine(_home, ".config/edendeck")).LinkTarget);
    }
}
