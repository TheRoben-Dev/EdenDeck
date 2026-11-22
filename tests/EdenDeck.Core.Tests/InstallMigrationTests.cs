using EdenDeck.Host.Updates;
using Xunit;

namespace EdenDeck.Core.Tests;

public class InstallMigrationTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "scanline-inst-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    [Fact]
    public void MovesTheOldInstallFolderAndKeepsTheOldPathWorking()
    {
        var old = Path.Combine(_home, "Applications", "EdenDeck");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "Scanline.x86_64"), "app");
        File.CreateSymbolicLink(Path.Combine(old, "EdenDeck.x86_64"), "Scanline.x86_64");
        Directory.CreateDirectory(old + ".previous");
        var apps = Path.Combine(_home, ".local/share/applications");
        Directory.CreateDirectory(apps);
        File.WriteAllText(Path.Combine(apps, "edendeck.desktop"), "[Desktop Entry]");

        var exe = InstallMigration.Run(old, _home);

        var target = Path.Combine(_home, "Applications", "Scanline");
        Assert.Equal(Path.Combine(target, "Scanline.x86_64"), exe);
        Assert.NotNull(new DirectoryInfo(old).LinkTarget);
        Assert.Equal("app", File.ReadAllText(Path.Combine(old, "EdenDeck.x86_64")));  // old shortcut path
        Assert.True(Directory.Exists(target + ".previous"));
        Assert.False(File.Exists(Path.Combine(apps, "edendeck.desktop")));
        Assert.Contains("Name=Scanline", File.ReadAllText(Path.Combine(apps, "scanline.desktop")));
        // Running again from the new folder (or through the link) does nothing.
        Assert.Null(InstallMigration.Run(target, _home));
        Assert.Null(InstallMigration.Run(old, _home));
    }

    [Fact]
    public void LeavesOldBuildsAndOtherFoldersAlone()
    {
        var old = Path.Combine(_home, "Applications", "EdenDeck");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "EdenDeck.x86_64"), "0.3");  // a pre-rename build
        Assert.Null(InstallMigration.Run(old, _home));
        Assert.Null(new DirectoryInfo(old).LinkTarget);
    }
}
