using EdenDeck.Core.Library;

namespace EdenDeck.Backends.Eden.Storage;

/// <summary>What one game takes up on disk, by part.</summary>
public sealed record GameUsage(LibraryGame Game, long Base, long Updates, long OlderUpdates, long Dlc, long Shaders, long Mods)
{
    public long Total => Base + Updates + Dlc + Shaders + Mods;

    /// <summary>Update files older than the newest one: Eden only ever loads the newest.</summary>
    public IReadOnlyList<string> OlderUpdateFiles => Game.Updates.Skip(1).Select(u => u.Path).Where(File.Exists).ToList();
}

/// <summary>A drive (mount point) with its free and total space.</summary>
public sealed record Disk(string Mount, long Free, long Total);

/// <summary>Sizes for the Storage view.</summary>
public static class StorageReport
{
    public static GameUsage For(LibraryGame game, EdenPaths paths)
    {
        var tid = game.Base.TitleId;
        var updates = game.Updates.Select(u => FileBytes(u.Path)).ToList();
        return new GameUsage(game,
            FileBytes(game.Base.Path),
            updates.Sum(),
            updates.Skip(1).Sum(),
            game.Dlc.Sum(d => FileBytes(d.Path)),
            string.IsNullOrEmpty(tid) ? 0 : ShaderCache.Bytes(paths, tid),
            string.IsNullOrEmpty(tid) ? 0 : ShaderCache.DirBytes(paths.ModDir(tid)));
    }

    /// <summary>Every game, largest first.</summary>
    public static IReadOnlyList<GameUsage> Games(IEnumerable<LibraryGame> games, EdenPaths paths) =>
        games.Select(g => For(g, paths)).OrderByDescending(u => u.Total).ToList();

    /// <summary>All of Eden's saves (every game and profile).</summary>
    public static long Saves(EdenPaths paths) => ShaderCache.DirBytes(Path.Combine(paths.NandDir, "user", "save"));

    public static long FileBytes(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : Directory.Exists(path) ? ShaderCache.DirBytes(path) : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    /// <summary>The drives these folders are on, each once (the deepest mount point holding the folder).</summary>
    public static IReadOnlyList<Disk> Disks(IEnumerable<string> folders)
    {
        var drives = DriveInfo.GetDrives().Where(d =>
        {
            try
            {
                return d.IsReady && d.TotalSize > 0;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }).ToList();
        var result = new List<Disk>();
        foreach (var folder in folders.Where(f => !string.IsNullOrEmpty(f)))
        {
            var full = Path.GetFullPath(folder);
            var drive = drives
                .Where(d => full == d.Name.TrimEnd('/') || full.StartsWith(d.Name.EndsWith('/') ? d.Name : d.Name + "/", StringComparison.Ordinal))
                .MaxBy(d => d.Name.Length);
            if (drive != null && result.All(r => r.Mount != drive.Name))
            {
                result.Add(new Disk(drive.Name, drive.AvailableFreeSpace, drive.TotalSize));
            }
        }
        return result;
    }

    /// <summary>"1.2 GB", "340 MB", "12 KB".</summary>
    public static string Format(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB"
        : bytes >= 1 << 20 ? $"{bytes / (double)(1 << 20):0} MB"
        : $"{Math.Max(bytes > 0 ? 1 : 0, bytes / 1024)} KB";
}
