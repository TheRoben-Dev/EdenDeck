namespace EdenDeck.Host.Updates;

/// <summary>
/// Moves a portable install from the app's old name to the new one, once, the first time the
/// renamed app starts: ~/Applications/EdenDeck becomes ~/Applications/Scanline and the old path
/// stays as a link, so Steam shortcuts and the menu entry that point at it keep working. The menu
/// entry and the launcher on PATH get their new names too. Everything is best effort: on any
/// failure the app simply keeps running from where it is.
/// </summary>
public static class InstallMigration
{
    private const string OldFolder = "EdenDeck";
    private const string NewFolder = "Scanline";

    /// <summary>
    /// Returns the app binary to restart into when the install folder was moved (the caller then
    /// relaunches and quits), or null when nothing needed moving.
    /// </summary>
    public static string? Run(string installDir, string? home = null)
    {
        home ??= Environment.GetEnvironmentVariable("HOME");
        installDir = installDir.TrimEnd('/');
        string? relaunch = null;
        var dir = installDir;
        try
        {
            var info = new DirectoryInfo(installDir);
            var parent = info.Parent?.FullName;
            if (parent != null && info.Name == OldFolder && info.LinkTarget == null &&
                File.Exists(Path.Combine(installDir, "Scanline.x86_64")))
            {
                var target = Path.Combine(parent, NewFolder);
                if (!Directory.Exists(target) && !File.Exists(target))
                {
                    Directory.Move(installDir, target);
                    Directory.CreateSymbolicLink(installDir, target);
                    dir = target;
                    relaunch = Path.Combine(target, "Scanline.x86_64");
                    var oldPrev = installDir + ".previous";
                    var newPrev = target + ".previous";
                    if (Directory.Exists(oldPrev) && !Directory.Exists(newPrev))
                    {
                        Directory.Move(oldPrev, newPrev);
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        if (home != null && File.Exists(Path.Combine(dir, "Scanline.x86_64")))
        {
            RenameShortcuts(home, dir);
        }
        return relaunch;
    }

    /// <summary>edendeck.desktop -> scanline.desktop, and `scanline` next to `edendeck` on PATH (installer-made only).</summary>
    private static void RenameShortcuts(string home, string dir)
    {
        try
        {
            var apps = Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? x : Path.Combine(home, ".local/share"), "applications");
            var oldDesktop = Path.Combine(apps, "edendeck.desktop");
            if (File.Exists(oldDesktop))
            {
                File.WriteAllText(Path.Combine(apps, "scanline.desktop"),
                    "[Desktop Entry]\nType=Application\nName=Scanline\nComment=Controller-first frontend for the Eden emulator\n" +
                    $"Exec=\"{Path.Combine(dir, "Scanline.x86_64")}\"\nIcon={Path.Combine(dir, "scanline.svg")}\nTerminal=false\nCategories=Game;Emulator;\n");
                File.Delete(oldDesktop);
            }
            var bin = Path.Combine(home, ".local/bin");
            var newBin = Path.Combine(bin, "scanline");
            if (File.Exists(Path.Combine(bin, "edendeck")) && !File.Exists(newBin) && new FileInfo(newBin).LinkTarget == null)
            {
                File.CreateSymbolicLink(newBin, Path.Combine(dir, "scanline"));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
