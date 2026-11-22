using System.Text.RegularExpressions;

namespace EdenDeck.Backends.Eden.Storage;

/// <summary>
/// Eden's per-game shader caches: <c>&lt;cache&gt;/shader/&lt;titleid lowercase&gt;/</c> holding
/// vulkan.bin, vulkan_pipelines.bin and opengl.bin. Clearing one makes Eden rebuild it the next time
/// the game runs (some stutter at first), which fixes glitches after a driver or Eden update.
/// </summary>
public static partial class ShaderCache
{
    public sealed record Entry(string TitleId, string Dir, long Bytes);

    public static string Root(EdenPaths paths) => Path.Combine(paths.CacheDir, "shader");

    public static string Dir(EdenPaths paths, string titleId) => Path.Combine(Root(paths), titleId.ToLowerInvariant());

    public static long Bytes(EdenPaths paths, string titleId) => DirBytes(Dir(paths, titleId));

    /// <summary>Every game's cache, largest first. Title ids come back uppercase.</summary>
    public static IReadOnlyList<Entry> All(EdenPaths paths)
    {
        var root = Root(paths);
        if (!Directory.Exists(root))
        {
            return [];
        }
        return Directory.EnumerateDirectories(root)
            .Where(d => TitleDir().IsMatch(Path.GetFileName(d)))
            .Select(d => new Entry(Path.GetFileName(d).ToUpperInvariant(), d, DirBytes(d)))
            .Where(e => e.Bytes > 0)
            .OrderByDescending(e => e.Bytes)
            .ToList();
    }

    /// <summary>Deletes one game's cache; returns the bytes freed.</summary>
    public static long Clear(EdenPaths paths, string titleId)
    {
        var dir = Dir(paths, titleId);
        var bytes = DirBytes(dir);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
        return bytes;
    }

    /// <summary>Deletes every game's cache (other folders under shader/ are left alone).</summary>
    public static long ClearAll(EdenPaths paths) => All(paths).Sum(e => Clear(paths, e.TitleId));

    /// <summary>Total size of the files under a folder; 0 if it doesn't exist or can't be read.</summary>
    public static long DirBytes(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return 0;
        }
        try
        {
            return new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .Sum(f => f.Length);
        }
        catch (IOException)
        {
            return 0;
        }
    }

    [GeneratedRegex("^[0-9a-fA-F]{16}$")]
    private static partial Regex TitleDir();
}
